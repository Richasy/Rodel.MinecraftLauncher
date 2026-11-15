// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Sprout.Interop;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;

internal sealed record SeedSceneFrame(SeedSceneMesh Mesh, SeedSceneProjection Projection, int Width, int Height, Vector4 Background);

/// <summary>Owns an on-demand D3D11 composition surface with a coalesced frame mailbox; CPU readback is opt-in for bounded validation.</summary>
internal sealed unsafe class SeedSceneGpuRenderer
{
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<nint> _attach;
    private readonly Action<SeedSceneFrame> _submitted;
    private readonly Action<SeedSceneFrame, byte[]>? _imageReady;
    private readonly Action<string> _error;
    private SeedSceneFrame? _pending;
    private bool _stopping;
    private GraphicsDevice? _device;
    private ComPtr<IDXGISwapChain1> _chain;
    private ComPtr<ID3D11VertexShader> _vertex;
    private ComPtr<ID3D11PixelShader> _pixel;
    private ComPtr<ID3D11Buffer> _vertices, _colors, _constants;
    private ComPtr<ID3D11ShaderResourceView> _vertexView, _colorView, _atlasView;
    private ComPtr<ID3D11Texture2D> _atlas, _target, _depth, _readback;
    private ComPtr<ID3D11RenderTargetView> _targetView;
    private ComPtr<ID3D11DepthStencilView> _depthView;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<ID3D11RasterizerState> _rasterizer;
    private ComPtr<ID3D11DepthStencilState> _depthState;
    private SeedSceneMesh? _uploadedMesh;
    private SeedBlockScene? _uploadedScene;
    private int _width, _height;

    internal SeedSceneGpuRenderer(Action<nint> attach, Action<SeedSceneFrame> submitted, Action<string> error, Action<SeedSceneFrame, byte[]>? imageReady = null)
    {
        _attach = attach; _submitted = submitted; _error = error; _imageReady = imageReady;
        new Thread(Run) { IsBackground = true, Name = "Seed model D3D11" }.Start();
    }
    internal SeedSceneGpuRenderer(Action<SeedSceneFrame, byte[]> ready, Action<string> error)
        : this(_ => { }, _ => { }, error, ready) { }
    internal Task Completion => _completion.Task;
    internal void Submit(SeedSceneFrame frame) { lock (_gate) { if (_stopping) return; _pending = frame; _wake.Set(); } }
    internal void Stop() { lock (_gate) { if (_stopping) return; _stopping = true; _pending = null; _wake.Set(); } }

    private void Run()
    {
        try
        {
            while (true)
            {
                _wake.WaitOne();
                SeedSceneFrame? frame;
                lock (_gate) { if (_stopping) break; frame = _pending; _pending = null; }

                if (frame is null) continue;
                try { Draw(frame); _submitted(frame); }
                catch (Exception error) { Release(); _error(error.Message); }
            }
        }
        finally { Release(); _wake.Dispose(); _completion.TrySetResult(); }
    }

    private void Initialize()
    {
        _device = GraphicsDevice.Create();
        var assembly = typeof(SeedSceneGpuRenderer).Assembly;
        using var vertexStream = assembly.GetManifestResourceStream("SeedScene.VS")!;
        using var vertexBytes = new MemoryStream(); vertexStream.CopyTo(vertexBytes); var code = vertexBytes.ToArray();
        fixed (byte* pointer = code) { ID3D11VertexShader* shader = null; _device.D3DDevice->CreateVertexShader(pointer, (nuint)code.Length, null, &shader).ThrowIfFailed(); _vertex.Attach(shader); }

        using var pixelStream = assembly.GetManifestResourceStream("SeedScene.PS")!;
        using var pixelBytes = new MemoryStream(); pixelStream.CopyTo(pixelBytes); code = pixelBytes.ToArray();
        fixed (byte* pointer = code) { ID3D11PixelShader* shader = null; _device.D3DDevice->CreatePixelShader(pointer, (nuint)code.Length, null, &shader).ThrowIfFailed(); _pixel.Attach(shader); }

        var bufferDescription = new D3D11_BUFFER_DESC { ByteWidth = 64, Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER };
        ID3D11Buffer* buffer = null; _device.D3DDevice->CreateBuffer(&bufferDescription, null, &buffer).ThrowIfFailed(); _constants.Attach(buffer);
        var samplerDescription = new D3D11_SAMPLER_DESC
        {
            Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT,
            AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            MaxLOD = float.MaxValue
        };
        ID3D11SamplerState* sampler = null; _device.D3DDevice->CreateSamplerState(&samplerDescription, &sampler).ThrowIfFailed(); _sampler.Attach(sampler);
        var rasterDescription = new D3D11_RASTERIZER_DESC { FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID, CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE, DepthClipEnable = true };
        ID3D11RasterizerState* raster = null; _device.D3DDevice->CreateRasterizerState(&rasterDescription, &raster).ThrowIfFailed(); _rasterizer.Attach(raster);
        var depthDescription = new D3D11_DEPTH_STENCIL_DESC
        {
            DepthEnable = true,
            DepthWriteMask = D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ALL,
            DepthFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_LESS_EQUAL,
            StencilReadMask = byte.MaxValue,
            StencilWriteMask = byte.MaxValue,
            FrontFace = new()
            {
                StencilFailOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,
                StencilDepthFailOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,
                StencilPassOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,
                StencilFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_ALWAYS
            },
            BackFace = new()
            {
                StencilFailOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,
                StencilDepthFailOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,
                StencilPassOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP,
                StencilFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_ALWAYS
            }
        };
        ID3D11DepthStencilState* depthState = null; _device.D3DDevice->CreateDepthStencilState(&depthDescription, &depthState).ThrowIfFailed(); _depthState.Attach(depthState);
    }

    private void Upload(SeedSceneMesh mesh)
    {
        var device = _device!.D3DDevice;
        if (!ReferenceEquals(_uploadedScene, mesh.Scene))
        {
            _atlasView.Dispose(); _atlas.Dispose();
            var size = mesh.Scene.TextureSize;
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)size,
                Height = (uint)size,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                SampleDesc = new() { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_IMMUTABLE,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE
            };
            fixed (byte* pixels = mesh.Scene.TexturePixels.Span)
            {
                var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = pixels, SysMemPitch = (uint)(size * 4) };
                ID3D11Texture2D* texture = null; device->CreateTexture2D(&description, &initial, &texture).ThrowIfFailed(); _atlas.Attach(texture);
            }

            ID3D11ShaderResourceView* view = null; device->CreateShaderResourceView((ID3D11Resource*)_atlas.Get(), null, &view).ThrowIfFailed(); _atlasView.Attach(view);
            _uploadedScene = mesh.Scene;
        }

        if (ReferenceEquals(_uploadedMesh, mesh)) return;
        _vertexView.Dispose(); _vertices.Dispose(); _colorView.Dispose(); _colors.Dispose();
        if (mesh.Vertices.Length > 0)
        {
            var description = new D3D11_BUFFER_DESC
            {
                ByteWidth = checked((uint)mesh.Vertices.Length * sizeof(float)),
                Usage = D3D11_USAGE.D3D11_USAGE_IMMUTABLE,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                MiscFlags = (uint)D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_BUFFER_STRUCTURED,
                StructureByteStride = 24
            };
            fixed (float* vertices = mesh.Vertices.Span)
            {
                var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = vertices };
                ID3D11Buffer* buffer = null; device->CreateBuffer(&description, &initial, &buffer).ThrowIfFailed(); _vertices.Attach(buffer);
            }

            var viewDescription = new D3D11_SHADER_RESOURCE_VIEW_DESC { Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_BUFFER };
            viewDescription.Buffer.NumElements = (uint)(mesh.Vertices.Length / 6);
            ID3D11ShaderResourceView* view = null; device->CreateShaderResourceView((ID3D11Resource*)_vertices.Get(), &viewDescription, &view).ThrowIfFailed(); _vertexView.Attach(view);
            if (mesh.VertexColors.Length != mesh.Vertices.Length / 6) throw new InvalidDataException("The original model tint buffer does not match its geometry.");
            description.ByteWidth = checked((uint)mesh.VertexColors.Length * sizeof(uint));
            description.StructureByteStride = sizeof(uint);
            fixed (uint* colors = mesh.VertexColors.Span)
            {
                var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = colors };
                ID3D11Buffer* buffer = null; device->CreateBuffer(&description, &initial, &buffer).ThrowIfFailed(); _colors.Attach(buffer);
            }

            view = null; device->CreateShaderResourceView((ID3D11Resource*)_colors.Get(), &viewDescription, &view).ThrowIfFailed(); _colorView.Attach(view);
        }

        _uploadedMesh = mesh;
    }

    private void Resize(int width, int height)
    {
        if (width == _width && height == _height) return;
        var device = _device!;
        device.D3DContext->OMSetRenderTargets(0, null, null);
        _targetView.Dispose(); _target.Dispose(); _depthView.Dispose(); _depth.Dispose(); _readback.Dispose();
        if (_chain.Get() is null)
        {
            using ComPtr<IDXGIAdapter> adapter = default;
            device.DxgiDevice->GetAdapter(adapter.GetAddressOf()).ThrowIfFailed();
            using ComPtr<IDXGIFactory2> factory = default;
            var iid = typeof(IDXGIFactory2).GUID;
            adapter.Get()->GetParent(&iid, (void**)factory.GetAddressOf()).ThrowIfFailed();
            var chainDescription = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new() { Count = 1 },
                BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 2,
                Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
                AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
            };
            IDXGISwapChain1* chain = null;
            var result = factory.Get()->CreateSwapChainForComposition((IUnknown*)device.D3DDevice, &chainDescription, null, &chain);
            _chain.Attach(chain); result.ThrowIfFailed(); _attach((nint)chain);
        }
        else
        {
            _chain.Get()->ResizeBuffers(0, (uint)width, (uint)height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0).ThrowIfFailed();
        }

        var textureId = typeof(ID3D11Texture2D).GUID;
        _chain.Get()->GetBuffer(0, &textureId, (void**)_target.GetAddressOf()).ThrowIfFailed();
        ID3D11RenderTargetView* targetView = null; device.D3DDevice->CreateRenderTargetView((ID3D11Resource*)_target.Get(), null, &targetView).ThrowIfFailed(); _targetView.Attach(targetView);
        D3D11_TEXTURE2D_DESC description; _target.Get()->GetDesc(&description);
        description.Format = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;
        description.BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_DEPTH_STENCIL;
        ID3D11Texture2D* depth = null; device.D3DDevice->CreateTexture2D(&description, null, &depth).ThrowIfFailed(); _depth.Attach(depth);
        ID3D11DepthStencilView* depthView = null; device.D3DDevice->CreateDepthStencilView((ID3D11Resource*)depth, null, &depthView).ThrowIfFailed(); _depthView.Attach(depthView);
        if (_imageReady is not null)
        {
            description.Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM; description.BindFlags = 0;
            description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING; description.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            ID3D11Texture2D* readback = null; device.D3DDevice->CreateTexture2D(&description, null, &readback).ThrowIfFailed(); _readback.Attach(readback);
        }

        _width = width; _height = height;
    }

    private void Draw(SeedSceneFrame frame)
    {
        if (_device is null) Initialize();
        Resize(frame.Width, frame.Height); Upload(frame.Mesh);
        var context = _device!.D3DContext;
        var matrix = frame.Projection.Matrix; context->UpdateSubresource((ID3D11Resource*)_constants.Get(), 0, null, &matrix, 0, 0);
        var background = frame.Background; context->ClearRenderTargetView(_targetView.Get(), (float*)&background);
        context->ClearDepthStencilView(_depthView.Get(), (uint)D3D11_CLEAR_FLAG.D3D11_CLEAR_DEPTH, 1, 0);
        var viewport = new D3D11_VIEWPORT(0, 0, frame.Width, frame.Height, 0, 1); context->RSSetViewports(1, &viewport); context->RSSetState(_rasterizer.Get());
        var target = _targetView.Get(); context->OMSetRenderTargets(1, &target, _depthView.Get());
        context->OMSetDepthStencilState(_depthState.Get(), 0);
        context->IASetInputLayout(null); context->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(_vertex.Get(), null, 0); context->PSSetShader(_pixel.Get(), null, 0);
        var constants = _constants.Get(); context->VSSetConstantBuffers(0, 1, &constants);
        var view = _vertexView.Get(); context->VSSetShaderResources(0, 1, &view); view = _colorView.Get(); context->VSSetShaderResources(2, 1, &view); view = _atlasView.Get(); context->PSSetShaderResources(1, 1, &view);
        var sampler = _sampler.Get(); context->PSSetSamplers(0, 1, &sampler);
        context->Draw((uint)(frame.Mesh.Vertices.Length / 6), 0);
        if (_imageReady is null)
        {
            _chain.Get()->Present(1, 0).ThrowIfFailed();
            return;
        }

        context->CopyResource((ID3D11Resource*)_readback.Get(), (ID3D11Resource*)_target.Get());
        D3D11_MAPPED_SUBRESOURCE mapped; context->Map((ID3D11Resource*)_readback.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).ThrowIfFailed();
        var bytes = new byte[checked(frame.Width * frame.Height * 4)];
        try { for (var row = 0; row < frame.Height; row++) new ReadOnlySpan<byte>((byte*)mapped.pData + (row * mapped.RowPitch), frame.Width * 4).CopyTo(bytes.AsSpan(row * frame.Width * 4)); }
        finally { context->Unmap((ID3D11Resource*)_readback.Get(), 0); }

        _imageReady(frame, bytes);
    }

    private void Release()
    {
        if (_device is not null) _device.D3DContext->ClearState();
        _vertexView.Dispose(); _colorView.Dispose(); _atlasView.Dispose(); _vertices.Dispose(); _colors.Dispose(); _constants.Dispose(); _atlas.Dispose(); _targetView.Dispose(); _target.Dispose();
        _depthView.Dispose(); _depth.Dispose(); _depthState.Dispose(); _readback.Dispose(); _chain.Dispose(); _vertex.Dispose(); _pixel.Dispose(); _sampler.Dispose(); _rasterizer.Dispose();
        _device?.Dispose(); _device = null; _uploadedMesh = null; _uploadedScene = null; _width = _height = 0;
    }
}