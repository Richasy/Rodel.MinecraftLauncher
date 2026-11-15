// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

namespace Rodel.MinecraftLauncher.Rendering.Tests;

/// <summary>Owns dual-texture GPU correctness; persistence and editing are tested at their own boundaries.</summary>
[TestClass]
public sealed class CapeRenderingTests
{
    // Author review: packing tests cannot detect a wrong GPU pitch, physical cape face slot, or stale
    // geometry after changing the active work. Independent red/yellow witnesses and swapping primary
    // reject those failures. Both 128 textures exercise the popup constant buffer's maximum footprint.
    /// <summary>Actual D3D renders both saved textures and changing the editable work preserves their appearance.</summary>
    [TestMethod]
    public async Task PairedHdScene_RendersBothTexturesWhenPrimaryChanges()
    {
        var skin = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Classic, "#FF0000", 128));
        var cape = new SkinRenderSnapshot(SkinEditor.Create(SkinModel.Cape, "#FFFF00", 128));
        var ready = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new SkinGpuRenderer(_ => { }, _ => { }, error => ready.TrySetException(new InvalidOperationException(error)),
            (_, bytes, _, _) => ready.TrySetResult(bytes));
        async Task<byte[]> Draw(SkinRenderSnapshot primary, SkinRenderSnapshot? reference)
        {
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            renderer.Submit(new(primary, new(256, 256, MathF.PI + .4f, -.14f, 70), 127, true, false, new(0, 0, 0, 1), Companion: reference));
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken);
        }

        try
        {
            var paired = await Draw(cape, skin);
            var red = 0; var yellow = 0;
            for (var offset = 0; offset < paired.Length; offset += 4)
            {
                if (paired[offset + 2] > 100 && paired[offset + 1] < 10 && paired[offset] < 10) red++;
                if (paired[offset + 2] > 100 && paired[offset + 1] > 100 && paired[offset] < 10) yellow++;
            }

            Assert.IsGreaterThan(100, red, "Character reference must be visible beside the cape.");
            Assert.IsGreaterThan(100, yellow, "Cape outside must sample its own texture.");
            CollectionAssert.AreEqual(paired, await Draw(skin, cape), "Changing the active document must not change either texture's UVs.");
            var solo = await Draw(cape, null);
            Assert.IsFalse(paired.AsSpan().SequenceEqual(solo), "Solo must remove the reference geometry.");
            using var effect = new SkinPreviewEffect(cape, skin);
            var constants = effect.Create(new(cape, new(256, 256, .4f, -.14f, 70), 127, true, false, new(0, 0, 0, 1), Companion: skin)).ConstantBuffer;
            Assert.IsLessThanOrEqualTo(65536, constants.Length, "D2D's constant buffer limit applies to the combined HD scene.");
        }
        finally { renderer.Stop(); await renderer.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken); }
    }

    /// <summary>Gets or sets cancellation for the owned native worker.</summary>
    public TestContext TestContext { get; set; } = null!;
}