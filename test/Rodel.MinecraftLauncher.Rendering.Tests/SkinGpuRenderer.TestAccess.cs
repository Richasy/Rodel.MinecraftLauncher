// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

// Formal source-linked regression access only. This file is never an App build/publish input.
internal sealed unsafe partial class SkinGpuRenderer
{
    internal nint DeviceIdentityForTests => _device is { } device ? (nint)device.D3DDevice : 0;
    // Read after the image-ready barrier, while no next frame has been submitted.
    internal bool HasRetainedSceneForTests => _sceneCache.Get() is not null;

    // Read after the suspension barrier, with no subsequent frame in flight.
    internal bool HasGpuResourcesForTests => _device is not null || _chain.Get() is not null
        || _target.Get() is not null || _texture.Get() is not null || _sceneCache.Get() is not null;
}