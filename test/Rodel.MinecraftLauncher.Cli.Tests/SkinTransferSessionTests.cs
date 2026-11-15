// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises transfer drafts, actual storage failures and the common mixed-content history.</summary>
[TestClass]
public sealed class SkinTransferSessionTests
{
    // Author review: a real exclusive project-file handle forces the durable read boundary to fail after preview.
    // Releasing that same handle permits retry, proving draft preservation without mock success or timing guesses.
    /// <summary>A failed transfer commit retains its frozen draft and retries exactly once after the file is released.</summary>
    [TestMethod]
    public async Task FailedTransferCommit_RetainsPreviewAndRetriesOneHistoryStep()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, Surface = OuterFront, X = 1, Y = 1, Color = "#FF0000" }, TestContext.CancellationToken);
        var baseline = session.Render().Pixels.ToArray(); var revision = session.Revision;
        var mask = new bool[4096]; mask[(9 * 64) + 41] = true;
        session.BeginTransfer(new() { Tool = SkinTool.Move, AtlasTransfer = new() { SourceMask = mask, X = 41, Y = 9, Layer = SkinLayer.Outer } });
        Array.Fill(mask, false); session.UpdateTransfer(42, 9);
        var preview = session.Render(); var pixels = preview.Pixels.ToArray();
        Assert.IsTrue(session.HasDraft); Assert.IsFalse(session.HasStroke);
        Assert.AreSame(preview, session.Render(), "All views consume the same published immutable preview.");
        CollectionAssert.AreEqual(baseline, service.Read(id).Pixels);
        using (var held = new FileStream(Path.Combine(sandbox.Root, "skins", id + SkinService.ProjectFileExtension), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => session.CommitAsync(TestContext.CancellationToken));
            Assert.IsTrue(session.HasDraft); Assert.AreEqual(revision, session.Revision);
            CollectionAssert.AreEqual(pixels, session.Render().Pixels.ToArray());
        }

        await session.CommitAsync(TestContext.CancellationToken);
        Assert.IsFalse(session.HasDraft); Assert.AreEqual(revision + 1, session.Revision);
        CollectionAssert.AreEqual(pixels, service.Read(id).Pixels);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        CollectionAssert.AreEqual(baseline, session.Render().Pixels.ToArray());
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        CollectionAssert.AreEqual(pixels, service.Read(id).Pixels);
    }

    // Author review: clipboard library tests cannot establish that transfer, brush and layer commands share one
    // session timeline. This compact journey retains each actual pixel checkpoint and walks the same history both ways.
    /// <summary>Layer, drawing, move, delete and paste restore through one durable undo/redo sequence.</summary>
    [TestMethod]
    public async Task MixedTransfers_UseTheExistingContentTimeline()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, size: 128, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ink", Name = "Ink" } }, TestContext.CancellationToken);
        var checkpoints = new List<uint[]> { session.LayerPixels("ink") };
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, PaintLayerId = "ink", Surface = OuterFront, X = 1, Y = 1, Color = "#FF0000" }, TestContext.CancellationToken);
        checkpoints.Add(session.LayerPixels("ink"));
        var mask = new bool[128 * 128]; mask[(17 * 128) + 81] = true;
        var copied = session.CaptureAtlas(mask); var observed = session.Revision;
        Assert.AreEqual(observed, service.Read(id).Revision);
        session.BeginTransfer(new() { Tool = SkinTool.Move, PaintLayerId = "ink", AtlasTransfer = new() { SourceMask = mask, X = 81, Y = 17, Layer = SkinLayer.Outer } });
        session.UpdateTransfer(82, 17); session.Cancel();
        CollectionAssert.AreEqual(checkpoints[^1], session.LayerPixels("ink"));
        Assert.AreEqual(observed, session.Revision);
        session.BeginTransfer(new() { Tool = SkinTool.Move, PaintLayerId = "ink", AtlasTransfer = new() { SourceMask = mask, X = 82, Y = 17, Layer = SkinLayer.Outer } });
        await session.CommitAsync(TestContext.CancellationToken); checkpoints.Add(session.LayerPixels("ink"));
        var moved = new bool[128 * 128]; moved[(17 * 128) + 82] = true;
        await session.ApplyAsync(new() { Tool = SkinTool.Cut, PaintLayerId = "ink", AtlasTransfer = new() { SourceMask = moved } }, TestContext.CancellationToken);
        checkpoints.Add(session.LayerPixels("ink"));
        session.BeginTransfer(new() { Tool = SkinTool.Paste, PaintLayerId = "ink", AtlasTransfer = new() { X = 83, Y = 17, Layer = SkinLayer.Outer, Block = copied.Block } });
        copied.Block.Pixels[0] = 0; // An externally held clipboard cannot change the frozen draft.
        await session.CommitAsync(TestContext.CancellationToken); checkpoints.Add(session.LayerPixels("ink"));
        Assert.AreEqual(0xFF0000FFu, checkpoints[^1][(17 * 128) + 83]);
        for (var index = checkpoints.Count - 2; index >= 0; index--)
        {
            await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
            CollectionAssert.AreEqual(checkpoints[index], session.LayerPixels("ink"));
        }

        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.DoesNotContain(layer => layer.Id == "ink", session.Layers, "The same timeline must also remove the original added layer.");
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(layer => layer.Id == "ink", session.Layers);
        CollectionAssert.AreEqual(checkpoints[0], session.LayerPixels("ink"));
        for (var index = 1; index < checkpoints.Count; index++)
        {
            await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
            CollectionAssert.AreEqual(checkpoints[index], session.LayerPixels("ink"));
        }

        CollectionAssert.AreEqual(session.Render().Pixels.ToArray(), new SkinRenderSnapshot(service.Read(id)).Pixels.ToArray());
    }

    // Author review: face-copy tests cannot establish one durable transaction across both shells
    // or that the session's actual selected layer is used. Reopening and a single undo/redo prove both.
    /// <summary>A whole visible limb is saved to the selected layer as one recoverable project operation.</summary>
    [TestMethod]
    public async Task CopyLimbToOpposite_PersistsBothShellsWithOneUndoStep()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, color: "#0000FF", size: 128, cancellationToken: TestContext.CancellationToken);
        var front = new SkinSurface { Part = SkinPart.LeftArm, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var outer = front with { Layer = SkinLayer.Outer, Face = SkinFace.Left };
        await service.EditAsync(id, 0,
        [
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "paint", Name = "Selected" } },
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "source", Name = "Visible source" } },
            new() { Tool = SkinTool.Pencil, PaintLayerId = "source", Surface = front, Color = "#FF0000" },
            new() { Tool = SkinTool.Pencil, PaintLayerId = "source", Surface = outer, Color = "#00FF0080" },
        ], TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        session.SelectLayer("paint");
        var before = session.LayerPixels("paint");
        var revision = session.Revision;
        var history = session.History().Entries.Length;

        await session.CopyPartToOppositeAsync(SkinPart.LeftArm, cancellationToken: TestContext.CancellationToken);

        var stored = service.Read(id);
        Assert.AreEqual(revision + 1, stored.Revision);
        Assert.HasCount(history + 1, session.History().Entries);
        Assert.AreEqual("paint", session.ActiveLayerId);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(stored, front with { Part = SkinPart.RightArm }, 5, 0, "paint"));
        Assert.AreEqual("#00FF0080", SkinEditor.Sample(stored, outer with { Part = SkinPart.RightArm, Face = SkinFace.Right }, 7, 0, "paint"));
        var copied = session.LayerPixels("paint");
        CollectionAssert.AreEqual(copied, service.OpenEditor(id).LayerPixels("paint"));
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        CollectionAssert.AreEqual(before, session.LayerPixels("paint"));
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        CollectionAssert.AreEqual(copied, service.Read(id).Layers.Single(layer => layer.Id == "paint").Pixels);
    }

    private static SkinSurface OuterFront => new() { Part = SkinPart.Head, Layer = SkinLayer.Outer, Face = SkinFace.Front };
    /// <summary>Gets or sets cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; } = null!;
}