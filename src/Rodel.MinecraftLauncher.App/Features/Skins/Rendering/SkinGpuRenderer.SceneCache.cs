// Copyright (c) Richasy. All rights reserved.

using Sprout.Interop;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

internal sealed unsafe partial class SkinGpuRenderer
{
    private const long MaxSceneCacheBytes = 64L * 1024 * 1024;
    private ComPtr<ID3D11Texture2D> _sceneCache;
    private ComPtr<ID3D11RenderTargetView> _sceneCacheTarget;
    private SkinGpuFrame? _sceneCacheFrame;

    // Hover changes the mask, not the model's shaded pixels. Retain those pixels on the same device and
    // re-run the unchanged depth/alpha/AA shader only over the current contour after restoring the base.
    private bool TryDrawRetainedScene(SkinGpuFrame frame, RECT modelClip)
    {
        if (SoftwarePass || frame.Atlas is not null || frame.Pose is not null || frame.Overlay is null
            || (long)_width * _height * 4 > MaxSceneCacheBytes)
        {
            ReleaseSceneCache();
            return false;
        }

        var context = _device!.D3DContext;
        if (_sceneCache.Get() is null)
        {
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)_width,
                Height = (uint)_height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new() { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET,
            };
            ID3D11Texture2D* texture = null;
            var result = _device.D3DDevice->CreateTexture2D(&description, null, &texture);
            _sceneCache.Attach(texture);
            if (result.Value < 0)
            {
                ReleaseSceneCache();
                return false;
            }

            ID3D11RenderTargetView* view = null;
            result = _device.D3DDevice->CreateRenderTargetView((ID3D11Resource*)texture, null, &view);
            _sceneCacheTarget.Attach(view);
            if (result.Value < 0)
            {
                ReleaseSceneCache();
                return false;
            }
        }

        var baseFrame = frame with { Overlay = null };
        var overlayEnabled = _data[1493];
        if (_sceneCacheFrame != baseFrame)
        {
            _data[1493] = 0;
            UploadSceneConstants();
            var cachedTarget = _sceneCacheTarget.Get();
            context->OMSetRenderTargets(1, &cachedTarget, null);
            var background = frame.Background;
            context->ClearRenderTargetView(cachedTarget, (float*)&background);
            context->RSSetScissorRects(1, &modelClip);
            context->Draw(3, 0);
            _sceneCacheFrame = baseFrame;
            _data[1493] = overlayEnabled;
            UploadSceneConstants();
        }

        // Flip buffers do not constitute a retained base. Restore every output pixel from our own texture,
        // including the previous contour, before drawing the new contour. This also handles hover leaving.
        context->OMSetRenderTargets(0, null, null);
        using ComPtr<ID3D11Resource> output = default;
        _target.Get()->GetResource(output.GetAddressOf());
        context->CopyResource(output.Get(), (ID3D11Resource*)_sceneCache.Get());
        var target = _target.Get();
        context->OMSetRenderTargets(1, &target, null);
        var contour = OverlayClip(frame);
        if (contour.right > contour.left && contour.bottom > contour.top)
        {
            contour.left = Math.Max(0, contour.left - 1);
            contour.top = Math.Max(0, contour.top - 1);
            contour.right = Math.Min(_width, contour.right + 1);
            contour.bottom = Math.Min(_height, contour.bottom + 1);
            context->RSSetScissorRects(1, &contour);
            context->Draw(3, 0);
        }

        return true;
    }

    private void UploadSceneConstants()
    {
        fixed (float* constants = _data)
            _device!.D3DContext->UpdateSubresource((ID3D11Resource*)_constants.Get(), 0, null, constants, 0, 0);
    }

    private void ReleaseSceneCache()
    {
        _sceneCacheFrame = null;
        _sceneCacheTarget.Dispose();
        _sceneCache.Dispose();
    }
}