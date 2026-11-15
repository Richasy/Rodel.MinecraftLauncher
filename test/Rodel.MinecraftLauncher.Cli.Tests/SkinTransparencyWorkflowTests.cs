// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Verifies the same reversible transparency used by editor gestures and command exports.</summary>
[TestClass]
public sealed class SkinTransparencyWorkflowTests
{
    /// <summary>
    /// A masked composite must preserve the backing artwork through draft cancellation, project persistence and history.
    /// The separate CLI process exports actual PNG pixels and clears the mask through the typed command contract.
    /// </summary>
    [TestMethod]
    public async Task MaskedStroke_PersistsWithoutChangingLayers_AndClearsBackToOriginal()
    {
        using var sandbox = new CliSandbox();
        var ct = TestContext.CancellationToken;
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#406080", cancellationToken: ct);
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var before = await service.EditAsync(id, 0,
            [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ink", Name = "Ink" } },
             new() { Tool = SkinTool.Pencil, Surface = front, PaintLayerId = "ink", X = 1, Color = "#EA5722" }], ct);
        const int baseTexel = (8 * 64) + 8;
        const int inkTexel = baseTexel + 1;
        var session = service.OpenEditor(id);
        var mask = new SkinEdit { Tool = SkinTool.Stroke, Surface = front, TransparencyMask = true };

        session.Begin(mask);
        session.Append(new(0, 0));
        var draft = session.Render();
        Assert.IsTrue(draft.IsMasked(baseTexel));
        Assert.AreEqual(0u, draft.Pixels.Span[baseTexel]);
        Assert.AreEqual(0xEA5722FFu, draft.Pixels.Span[inkTexel]);
        session.Cancel();
        Assert.AreEqual(0x406080FFu, session.Render().Pixels.Span[baseTexel]);

        session.Begin(mask);
        session.Append(new(0, 0));
        session.Append(new(1, 0));
        await session.CommitAsync(ct);
        var output = Path.Combine(sandbox.Root, "mask.png");
        (await sandbox.RunAsync("skin", "export", id, "--output", output)).ReadEnvelope(0);
        var exported = SkinPng.DecodeLayer(await File.ReadAllBytesAsync(output, ct));
        Assert.AreEqual(0u, exported.Pixels[baseTexel]);
        Assert.AreEqual(0u, exported.Pixels[inkTexel]);
        Assert.AreEqual(0x406080FFu, exported.Pixels[inkTexel + 1]);

        (await sandbox.RunAsync("skin", "undo", id, "--revision", "2")).ReadEnvelope(0);
        Assert.AreEqual("#EA5722FF", SkinEditor.Sample(service.Read(id), front, 1, 0));
        (await sandbox.RunAsync("skin", "redo", id, "--revision", "3")).ReadEnvelope(0);
        Assert.AreEqual("#00000000", SkinEditor.Sample(service.Read(id), front, 1, 0));
        (await sandbox.RunAsync("skin", "edit", id, "--revision", "4", "--edits",
            """[{"tool":"Eraser","transparencyMask":true,"surface":{"part":"Head","layer":"Base","face":"Front"},"points":[{"x":0,"y":0},{"x":1,"y":0}]}]""")).ReadEnvelope(0);

        var restored = service.Read(id);
        Assert.AreEqual("#406080FF", SkinEditor.Sample(restored, front, 0, 0));
        Assert.AreEqual("#EA5722FF", SkinEditor.Sample(restored, front, 1, 0));
        CollectionAssert.AreEqual(before.Pixels, restored.Pixels);
        CollectionAssert.AreEqual(before.Layers[0].Pixels, restored.Layers[0].Pixels);
    }

    /// <summary>Gets the deadline for isolated project and PNG operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}