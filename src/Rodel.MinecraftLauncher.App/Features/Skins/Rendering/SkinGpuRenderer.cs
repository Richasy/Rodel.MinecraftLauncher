// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Sprout.Interop;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

internal sealed record SkinGpuFrame(
    SkinRenderSnapshot Texture,
    SkinCamera Camera,
    int Parts,
    bool Outer,
    bool Grid,
    Vector4 Background,
    string DocumentId = "",
    SkinAtlasView? Atlas = null,
    SkinBrushOverlay? Overlay = null,
    SkinAnimationPose? Pose = null,
    bool Antialias = false,
    SkinRenderSnapshot? Companion = null);

/// <summary>On-demand view on the shared skin render thread. A one-slot mailbox coalesces frames, never input samples.</summary>
internal sealed unsafe partial class SkinGpuRenderer
{
    private readonly Lock _gate = new();
    private bool _queued;
    private readonly Action<nint> _attach;
    private readonly Action<SkinGpuFrame> _submitted;
    private readonly Action<string> _error;
    private readonly Action<SkinGpuFrame, byte[], int, int>? _imageReady;
    private ComPtr<ID3D11Texture2D> _readback;
    private SkinGpuFrame? _pending;
    private TaskCompletionSource? _suspension;
    private bool _stopping;
    private GraphicsDevice? _device;
    private SkinRenderThread.DeviceLease? _deviceLease;
    private ComPtr<IDXGISwapChain1> _chain;
    private ComPtr<ID3D11RenderTargetView> _target;
    private ComPtr<ID3D11VertexShader> _vertex;
    private ComPtr<ID3D11PixelShader> _pixel;
    private ComPtr<ID3D11PixelShader> _resolve;
    private ComPtr<ID3D11Texture2D> _softwareTexture;
    private ComPtr<ID3D11RenderTargetView> _softwareTarget;
    private ComPtr<ID3D11ShaderResourceView> _softwareView;
    private ComPtr<ID3D11Buffer> _constants;
    private ComPtr<ID3D11Texture2D> _texture;
    private ComPtr<ID3D11ShaderResourceView> _textureView;
    private ComPtr<ID3D11RasterizerState> _rasterizer;
    private SkinRenderSnapshot? _uploaded;
    private SkinBrushOverlay? _uploadedOverlay;
    private ComPtr<ID3D11Texture2D> _mask;
    private ComPtr<ID3D11ShaderResourceView> _maskView;
    private bool _atlasMode, _antialiasMode;
    private SkinCamera? _constantCamera;
    private Matrix4x4 _viewProjection;
    private bool SoftwarePass => IsWarp && !_atlasMode;
    private int _width, _height;
    private int _renderWidth, _renderHeight;
    private readonly float[] _data = new float[SkinShaderScene.FloatCount];
    private readonly byte[] _rgba = new byte[256 * 128 * 4];
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SkinRenderSnapshot? _companion;
    private SkinSceneSnapshot? _scene;
    private SkinSceneSnapshot? _geometryScene;
    private readonly Vector3[] _corners = new Vector3[112];

    internal SkinGpuRenderer(
        Action<nint> attach,
        Action<SkinGpuFrame> submitted,
        Action<string> error,
        Action<SkinGpuFrame, byte[], int, int>? imageReady = null)
    {
        _attach = attach;
        _submitted = submitted;
        _error = error;
        _imageReady = imageReady;
    }

    internal bool IsWarp { get; private set; }
    internal Task Completion => _completion.Task;

    internal void Submit(SkinGpuFrame frame)
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            _pending = frame;
            Schedule();
        }
    }

    internal void Stop()
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
            _pending = null;
            Schedule();
        }
    }

    /// <summary>Discards queued paint and releases this worker's GPU state before completing; a later frame resumes it.</summary>
    internal Task SuspendAsync()
    {
        lock (_gate)
        {
            if (_stopping)
                return _completion.Task;
            _pending = null;
            _suspension ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            Schedule();
            return _suspension.Task;
        }
    }

    private void Schedule()
    {
        if (_queued)
            return;
        _queued = true;
        SkinRenderThread.Post(Run);
    }

    private void Run()
    {
        SkinGpuFrame? frame;
        TaskCompletionSource? suspension;
        bool stopping;
        Exception? stopFailure = null;
        lock (_gate)
        {
            _queued = false;
            stopping = _stopping;
            frame = _pending;
            _pending = null;
            suspension = _suspension;
            _suspension = null;
        }

        try
        {
            if (stopping)
            {
                Release();
                suspension?.TrySetResult();
                return;
            }

            if (suspension is not null)
            {
                _attach(0);
                Release();
                suspension.TrySetResult();
            }

            if (frame is null)
                return;
            if (_device is null)
                Initialize(frame.Camera.Width, frame.Camera.Height);
            Draw(frame);
            _submitted(frame);
        }
        catch (Exception ex)
        {
            suspension?.TrySetException(ex);
            // Retire this generation; other views keep their leases until their own recovery or teardown.
            _deviceLease?.Retire();
            var failure = ex;
            try { _attach(0); }
            catch (Exception detachError) { failure = new AggregateException(failure, detachError); }
            finally
            {
                try { Release(); }
                catch (Exception releaseError) { failure = new AggregateException(failure, releaseError); }
            }

            if (stopping)
                stopFailure = failure;
            try { _error(failure.Message); }
            catch (Exception callbackError) { SkinRenderThread.ReportFailure(new AggregateException(failure, callbackError)); }
        }
        finally
        {
            if (stopping)
            {
                if (stopFailure is null)
                    _completion.TrySetResult();
                else
                    _completion.TrySetException(stopFailure);
            }
        }
    }

    private void Initialize(int width, int height)
    {
        _deviceLease = SkinRenderThread.Acquire();
        _device = _deviceLease.Device;
        IsWarp = _device.IsWarp;
        using ComPtr<IDXGIAdapter> adapter = default;
        _device.DxgiDevice->GetAdapter(adapter.GetAddressOf()).ThrowIfFailed();
        using ComPtr<IDXGIFactory2> factory = default;
        var iid = typeof(IDXGIFactory2).GUID;
        adapter.Get()->GetParent(&iid, (void**)factory.GetAddressOf()).ThrowIfFailed();
        var desc = new DXGI_SWAP_CHAIN_DESC1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new()
            {
                Count = 1
            },
            BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount = 2,
            Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
            SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
            AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
        };
        IDXGISwapChain1* chain = null;
        var result = factory.Get()->CreateSwapChainForComposition((IUnknown*)_device.D3DDevice, &desc, null, &chain);
        _chain.Attach(chain);
        result.ThrowIfFailed();
        var assembly = typeof(SkinGpuRenderer).Assembly;
        using var vsStream = assembly.GetManifestResourceStream("SkinShaders.VS")!;
        using var psStream = assembly.GetManifestResourceStream("SkinShaders.PS")!;
        using var vsBytes = new MemoryStream();
        using var psBytes = new MemoryStream();
        vsStream.CopyTo(vsBytes);
        psStream.CopyTo(psBytes);
        var vs = vsBytes.ToArray();
        var ps = psBytes.ToArray();
        fixed (byte* code = vs)
        {
            ID3D11VertexShader* shader = null;
            _device.D3DDevice->CreateVertexShader(code, (nuint)vs.Length, null, &shader).ThrowIfFailed();
            _vertex.Attach(shader);
        }

        fixed (byte* code = ps)
        {
            ID3D11PixelShader* shader = null;
            _device.D3DDevice->CreatePixelShader(code, (nuint)ps.Length, null, &shader).ThrowIfFailed();
            _pixel.Attach(shader);
        }

        using var resolveStream = assembly.GetManifestResourceStream("SkinShaders.Resolve")!;
        using var resolveBytes = new MemoryStream();
        resolveStream.CopyTo(resolveBytes);
        var resolve = resolveBytes.ToArray();
        fixed (byte* code = resolve)
        {
            ID3D11PixelShader* shader = null;
            _device.D3DDevice->CreatePixelShader(code, (nuint)resolve.Length, null, &shader).ThrowIfFailed();
            _resolve.Attach(shader);
        }

        var bufferDesc = new D3D11_BUFFER_DESC
        {
            ByteWidth = (uint)(_data.Length * sizeof(float)),
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER
        };
        ID3D11Buffer* buffer = null;
        _device.D3DDevice->CreateBuffer(&bufferDesc, null, &buffer).ThrowIfFailed();
        _constants.Attach(buffer);
        var rasterDesc = new D3D11_RASTERIZER_DESC
        {
            FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID,
            CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE,
            ScissorEnable = BOOL.TRUE,
            DepthClipEnable = BOOL.TRUE
        };
        ID3D11RasterizerState* raster = null;
        _device.D3DDevice->CreateRasterizerState(&rasterDesc, &raster).ThrowIfFailed();
        _rasterizer.Attach(raster);
        Resize(width, height);
        _attach((nint)_chain.Get());
    }

    private void Resize(
        int width,
        int height,
        bool atlas = false,
        bool antialias = false)
    {
        if ((_width == width) && (_height == height) && (_atlasMode == atlas) && (_antialiasMode == antialias))
            return;
        _device!.D3DContext->OMSetRenderTargets(0, null, null);
        ReleaseSceneCache();
        ID3D11ShaderResourceView* empty = null;
        _device.D3DContext->PSSetShaderResources(1, 1, &empty);
        _softwareView.Dispose();
        _softwareTarget.Dispose();
        _softwareTexture.Dispose();
        _target.Dispose();
        _chain.Get()->ResizeBuffers(
            0,
            (uint)width,
            (uint)height,
            DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
            0).ThrowIfFailed();
        using ComPtr<ID3D11Texture2D> back = default;
        var iid = typeof(ID3D11Texture2D).GUID;
        _chain.Get()->GetBuffer(0, &iid, (void**)back.GetAddressOf()).ThrowIfFailed();
        ID3D11RenderTargetView* target = null;
        _device.D3DDevice->CreateRenderTargetView((ID3D11Resource*)back.Get(), null, &target).ThrowIfFailed();
        _target.Attach(target);
        _width = width;
        _height = height;
        _atlasMode = atlas;
        _antialiasMode = antialias;
        var pixelBudget = antialias ? 1048576f : 524288f;
        var scale = IsWarp && !atlas ? Math.Min(1f, MathF.Sqrt(pixelBudget / ((float)width * height))) : 1f;
        _renderWidth = Math.Max(1, (int)MathF.Floor(width * scale));
        _renderHeight = Math.Max(1, (int)MathF.Floor(height * scale));
        if (SoftwarePass)
        {
            var softwareDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)_renderWidth,
                Height = (uint)_renderHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                SampleDesc = new()
                {
                    Count = 1
                },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = (uint)(D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE)
            };
            ID3D11Texture2D* texture = null;
            _device.D3DDevice->CreateTexture2D(&softwareDesc, null, &texture).ThrowIfFailed();
            _softwareTexture.Attach(texture);
            ID3D11RenderTargetView* softwareTarget = null;
            _device.D3DDevice->CreateRenderTargetView((ID3D11Resource*)texture, null, &softwareTarget).ThrowIfFailed();
            _softwareTarget.Attach(softwareTarget);
            ID3D11ShaderResourceView* softwareView = null;
            _device.D3DDevice->CreateShaderResourceView((ID3D11Resource*)texture, null, &softwareView).ThrowIfFailed();
            _softwareView.Attach(softwareView);
        }
    }

    private void Draw(SkinGpuFrame frame)
    {
        Resize(frame.Camera.Width, frame.Camera.Height, frame.Atlas is not null, frame.Antialias);
        var context = _device!.D3DContext;
        if (!ReferenceEquals(_uploaded, frame.Texture) || !ReferenceEquals(_companion, frame.Companion))
        {
            var scene = new SkinSceneSnapshot(frame.Texture, frame.Companion);
            if (_scene?.Width != scene.Width || _scene?.Height != scene.Height)
            {
                ID3D11ShaderResourceView* none = null;
                context->PSSetShaderResources(0, 1, &none);
                context->PSSetShaderResources(2, 1, &none);
                _maskView.Dispose();
                _mask.Dispose();
                _uploadedOverlay = null;
                _textureView.Dispose();
                _texture.Dispose();
                var desc = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)scene.Width,
                    Height = (uint)scene.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                    SampleDesc = new()
                    {
                        Count = 1
                    },
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE
                };
                ID3D11Texture2D* texture = null;
                _device.D3DDevice->CreateTexture2D(&desc, null, &texture).ThrowIfFailed();
                _texture.Attach(texture);
                ID3D11ShaderResourceView* view = null;
                _device.D3DDevice->CreateShaderResourceView((ID3D11Resource*)texture, null, &view).ThrowIfFailed();
                _textureView.Attach(view);
            }

            _scene = scene;
            var pixels = scene.Pixels.Span;
            for (var index = 0; index < pixels.Length; index++)
            {
                var rgba = pixels[index];
                var offset = index * 4;
                _rgba[offset] = (byte)(rgba >> 24);
                _rgba[offset + 1] = (byte)(rgba >> 16);
                _rgba[offset + 2] = (byte)(rgba >> 8);
                _rgba[offset + 3] = (byte)rgba;
            }

            fixed (byte* bytes = _rgba)
            {
                context->UpdateSubresource(
                    (ID3D11Resource*)_texture.Get(),
                    0,
                    null,
                    bytes,
                    (uint)scene.Width * 4,
                    0);
            }

            _uploaded = frame.Texture;
            _companion = frame.Companion;
            _uploadedOverlay = null;
        }

        if (frame.Overlay is { } overlay && !ReferenceEquals(_uploadedOverlay, overlay))
        {
            if (_mask.Get() is null)
            {
                var desc = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)_scene!.Width,
                    Height = (uint)_scene.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                    SampleDesc = new()
                    {
                        Count = 1
                    },
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE
                };
                ID3D11Texture2D* mask = null;
                _device.D3DDevice->CreateTexture2D(&desc, null, &mask).ThrowIfFailed();
                _mask.Attach(mask);
                ID3D11ShaderResourceView* view = null;
                _device.D3DDevice->CreateShaderResourceView((ID3D11Resource*)mask, null, &view).ThrowIfFailed();
                _maskView.Attach(view);
            }

            var cells = new uint[_scene!.Width * _scene.Height];
            for (var row = 0; row < frame.Texture.Height; row++)
                overlay.Cells.AsSpan(row * frame.Texture.Size, frame.Texture.Size).CopyTo(cells.AsSpan(row * _scene.Width));
            fixed (uint* pointer = cells)
            {
                context->UpdateSubresource(
                    (ID3D11Resource*)_mask.Get(),
                    0,
                    null,
                    pointer,
                    (uint)_scene.Width * 4,
                    0);
            }

            _uploadedOverlay = overlay;
        }

        FillConstants(frame, out var scissor);
        fixed (float* constants = _data)
        {
            context->UpdateSubresource(
                (ID3D11Resource*)_constants.Get(),
                0,
                null,
                constants,
                0,
                0);
        }

        var target = SoftwarePass ? _softwareTarget.Get() : _target.Get();
        ID3D11ShaderResourceView* empty = null;
        context->PSSetShaderResources(1, 1, &empty);
        var bg = frame.Background;
        var viewport = new D3D11_VIEWPORT
        {
            Width = _renderWidth,
            Height = _renderHeight,
            MaxDepth = 1
        };
        context->RSSetViewports(1, &viewport);
        context->RSSetScissorRects(1, &scissor);
        context->RSSetState(_rasterizer.Get());
        context->OMSetRenderTargets(1, &target, null);
        context->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(_vertex.Get(), null, 0);
        context->PSSetShader(_pixel.Get(), null, 0);
        var buffer = _constants.Get();
        context->PSSetConstantBuffers(0, 1, &buffer);
        var textureView = _textureView.Get();
        context->PSSetShaderResources(0, 1, &textureView);
        var maskView = frame.Overlay is null ? null : _maskView.Get();
        context->PSSetShaderResources(2, 1, &maskView);
        if (!TryDrawRetainedScene(frame, scissor))
        {
            context->ClearRenderTargetView(target, (float*)&bg);
            context->Draw(3, 0);
        }

        if (SoftwarePass)
        {
            target = _target.Get();
            context->OMSetRenderTargets(1, &target, null);
            var fullViewport = new D3D11_VIEWPORT
            {
                Width = _width,
                Height = _height,
                MaxDepth = 1
            };
            var fullClip = new RECT
            {
                right = _width,
                bottom = _height
            };
            context->RSSetViewports(1, &fullViewport);
            context->RSSetScissorRects(1, &fullClip);
            context->PSSetShader(_resolve.Get(), null, 0);
            var softwareView = _softwareView.Get();
            context->PSSetShaderResources(1, 1, &softwareView);
            context->Draw(3, 0);
            // Recompute only the hovered footprint at output resolution. WARP's bounded scene remains
            // point-resolved; contour AA must happen after that resolve to avoid magnified stair steps.
            var contour = OverlayClip(frame);
            if ((contour.right > contour.left) && (contour.bottom > contour.top))
            {
                _data[20] = _width;
                _data[21] = _height;
                _data[1496] = contour.left;
                _data[1497] = contour.top;
                _data[1498] = contour.right;
                _data[1499] = contour.bottom;
                fixed (float* constants = _data)
                {
                    context->UpdateSubresource(
                        (ID3D11Resource*)_constants.Get(),
                        0,
                        null,
                        constants,
                        0,
                        0);
                }

                context->PSSetShaderResources(1, 1, &empty);
                context->RSSetScissorRects(1, &contour);
                context->PSSetShader(_pixel.Get(), null, 0);
                context->Draw(3, 0);
            }
        }

        if (_imageReady is null)
        {
            _chain.Get()->Present(1, 0).ThrowIfFailed();
        }
        else
        {
            PublishImage(frame);
        }
    }

    // Popup surfaces are composited above the main content underlay. Bounded dialog previews read pixels

    // into that popup; animated callers await each image before submitting another frame.

    private void PublishImage(SkinGpuFrame frame)
    {
        using ComPtr<ID3D11Texture2D> back = default;
        var iid = typeof(ID3D11Texture2D).GUID;
        _chain.Get()->GetBuffer(0, &iid, (void**)back.GetAddressOf()).ThrowIfFailed();
        if (_readback.Get() is not null)
        {
            D3D11_TEXTURE2D_DESC existing;
            _readback.Get()->GetDesc(&existing);
            if ((existing.Width != _width) || (existing.Height != _height))
                _readback.Dispose();
        }

        if (_readback.Get() is null)
        {
            D3D11_TEXTURE2D_DESC description;
            back.Get()->GetDesc(&description);
            description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            description.BindFlags = 0;
            description.MiscFlags = 0;
            description.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            ID3D11Texture2D* staging = null;
            _device!.D3DDevice->CreateTexture2D(&description, null, &staging).ThrowIfFailed();
            _readback.Attach(staging);
        }

        var context = _device!.D3DContext;
        context->CopyResource((ID3D11Resource*)_readback.Get(), (ID3D11Resource*)back.Get());
        var bytes = new byte[checked(_width * _height * 4)];
        D3D11_MAPPED_SUBRESOURCE mapped;
        context->Map(
            (ID3D11Resource*)_readback.Get(),
            0,
            D3D11_MAP.D3D11_MAP_READ,
            0,
            &mapped).ThrowIfFailed();
        try
        {
            for (var row = 0; row < _height; row++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.pData + (row * mapped.RowPitch), _width * 4).CopyTo(bytes.AsSpan(row * _width * 4));
            }
        }
        finally
        {
            context->Unmap((ID3D11Resource*)_readback.Get(), 0);
        }

        _imageReady!(frame, bytes, _width, _height);
    }

    private void FillConstants(SkinGpuFrame frame, out RECT scissor)
    {
        if (_constantCamera != frame.Camera)
        {
            _constantCamera = frame.Camera;
            MemoryMarshal.Cast<float, Matrix4x4>(_data.AsSpan(0, 16))[0] = frame.Camera.Inverse;
            _viewProjection = frame.Camera.ViewProjection;
        }

        void Vector(int offset, Vector4 value) => MemoryMarshal.Cast<float, Vector4>(_data.AsSpan(offset, 4))[0] = value;
        Vector(16, new(frame.Camera.Eye, _width));
        Vector(20, new(_renderWidth, _renderHeight, frame.Texture.Height, _height));
        Vector(24, frame.Background);
        Vector(28, new(frame.Texture.Size, frame.Grid ? 1 : 0, frame.Parts, frame.Outer ? 1 : 0));
        var flat = frame.Atlas;
        Vector(1488, flat is null ? default : new(flat.Cell, flat.X, flat.Y, flat.Layer));
        Vector(1492, new(flat is null ? 0 : 1, frame.Overlay?.Marks.Length > 0 ? 1 : 0, frame.Pose is null ? 0 : 1, frame.Antialias ? 1 : 0));
        for (var part = 0; part < 7; part++)
        {
            var matrix = frame.Pose is { } pose && part < pose.Parts.Length ? pose.Parts[part] : (Matrix4x4?)null;
            Matrix4x4.Invert(matrix ?? Matrix4x4.Identity, out var inverse);
            MemoryMarshal.Cast<float, Matrix4x4>(_data.AsSpan(1500 + (part * 16), 16))[0] = inverse;
        }

        if (!ReferenceEquals(_geometryScene, _scene))
            BuildGeometry(_scene!);
        var contour = OverlayClip(frame);
        Vector(1496, new(contour.left * (float)_renderWidth / _width, contour.top * (float)_renderHeight / _height, contour.right * (float)_renderWidth / _width, contour.bottom * (float)_renderHeight / _height));
        if (flat is not null)
        {
            scissor = new()
            {
                left = Math.Clamp((int)MathF.Floor(flat.X), 0, _renderWidth),
                top = Math.Clamp((int)MathF.Floor(flat.Y), 0, _renderHeight),
                right = Math.Clamp((int)MathF.Ceiling(flat.X + (flat.Cell * frame.Texture.Size)), 0, _renderWidth),
                bottom = Math.Clamp((int)MathF.Ceiling(flat.Y + (flat.Cell * frame.Texture.Height)), 0, _renderHeight)
            };
            return;
        }

        var minScreen = new Vector2(_renderWidth, _renderHeight);
        var maxScreen = Vector2.Zero;
        var viewProjection = _viewProjection;
        for (var box = 0; box < 14; box++)
        {
            if (((frame.Parts & (1 << (box / 2))) == 0) || (((box % 2) > 0) && !frame.Outer))
                continue;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = _corners[(box * 8) + corner];
                if (_corners[box * 8] == _corners[(box * 8) + 7])
                    continue;
                if (frame.Pose is { } pose && box / 2 < pose.Parts.Length)
                    point = Vector3.Transform(point, pose.Parts[box / 2]);
                var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
                var screen = new Vector2(((clip.X / clip.W) + 1) * _renderWidth / 2, (1 - (clip.Y / clip.W)) * _renderHeight / 2);
                minScreen = Vector2.Min(minScreen, screen);
                maxScreen = Vector2.Max(maxScreen, screen);
            }
        }

        scissor = new()
        {
            left = Math.Clamp((int)MathF.Floor(minScreen.X) - 1, 0, _renderWidth),
            top = Math.Clamp((int)MathF.Floor(minScreen.Y) - 1, 0, _renderHeight),
            right = Math.Clamp((int)MathF.Ceiling(maxScreen.X) + 1, 0, _renderWidth),
            bottom = Math.Clamp((int)MathF.Ceiling(maxScreen.Y) + 1, 0, _renderHeight)
        };
    }

    private RECT OverlayClip(SkinGpuFrame frame)
    {
        if (frame.Overlay is not { Marks.Length: > 0 } overlay)
            return default;
        var minimum = new Vector2(_width, _height);
        var maximum = Vector2.Zero;
        foreach (var mark in overlay.Marks)
        {
            if (((frame.Parts & (1 << (int)mark.Surface.Part)) == 0) || ((mark.Surface.Layer == SkinLayer.Outer) && !frame.Outer))
                continue;
            var offset = 144 + ((((int)mark.Surface.Part * 12) + ((int)mark.Surface.Layer * 6) + SkinShaderScene.FaceIndex(mark.Surface)) * 16);
            var origin = new Vector3(_data[offset], _data[offset + 1], _data[offset + 2]);
            var right = new Vector3(_data[offset + 4], _data[offset + 5], _data[offset + 6]) / _data[offset + 14];
            var down = new Vector3(_data[offset + 8], _data[offset + 9], _data[offset + 10]) / _data[offset + 15];
            for (var corner = 0; corner < 4; corner++)
            {
                var point = origin + (right * (mark.X + (corner & 1))) + (down * (mark.Y + (corner >> 1)));
                var clip = Vector4.Transform(new Vector4(point, 1), _viewProjection);
                if (clip.W <= 0)
                    continue;
                var screen = new Vector2(((clip.X / clip.W) + 1) * _width / 2, (1 - (clip.Y / clip.W)) * _height / 2);
                minimum = Vector2.Min(minimum, screen);
                maximum = Vector2.Max(maximum, screen);
            }
        }

        return new()
        {
            left = Math.Clamp((int)MathF.Floor(minimum.X) - 6, 0, _width),
            top = Math.Clamp((int)MathF.Floor(minimum.Y) - 6, 0, _height),
            right = Math.Clamp((int)MathF.Ceiling(maximum.X) + 6, 0, _width),
            bottom = Math.Clamp((int)MathF.Ceiling(maximum.Y) + 6, 0, _height)
        };
    }

    private void BuildGeometry(SkinSceneSnapshot texture)
    {
        SkinShaderScene.WriteGeometry(texture, _data, _corners);
        _geometryScene = texture;
    }

    private void Release()
    {
        _readback.Dispose();
        // A hidden viewport may stop before its first frame; recovery can also release the device before final teardown.
        // The null-conditional property access does not guard the subsequent unsafe pointer dereference.
        if (_device is { } device && device.D3DContext is not null)
            device.D3DContext->ClearState();
        ReleaseSceneCache();
        _softwareView.Dispose();
        _softwareTarget.Dispose();
        _softwareTexture.Dispose();
        _resolve.Dispose();
        _maskView.Dispose();
        _mask.Dispose();
        _uploadedOverlay = null;
        _rasterizer.Dispose();
        _textureView.Dispose();
        _texture.Dispose();
        _constants.Dispose();
        _pixel.Dispose();
        _vertex.Dispose();
        _target.Dispose();
        _chain.Dispose();
        var lease = _deviceLease;
        _deviceLease = null;
        _device = null;
        lease?.Release();
        _uploaded = null;
        _companion = null;
        _scene = _geometryScene = null;
        _width = _height = 0;
    }
}