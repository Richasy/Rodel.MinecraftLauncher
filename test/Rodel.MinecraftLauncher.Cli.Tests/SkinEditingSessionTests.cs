// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises editor drafts against real Core leases and isolated durable files, without a GPU or CLI child.</summary>
[TestClass]
public sealed class SkinEditingSessionTests
{
    /// <summary>A competing persisted edit must reject the draft commit while retaining visible draft pixels; cancellation publishes nothing.</summary>
    [TestMethod]
    public async Task ConflictingCommit_RetainsDraftUntilExplicitDiscard()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        session.Begin(new() { Tool = SkinTool.Stroke, Surface = front, Color = "#FF0000" });
        session.Append(new(1, 1));
        var draft = session.Render().Pixels.ToArray();
        Assert.AreEqual(0L, service.Read(id).Revision, "Pointer preview must not publish a partial transaction.");
        await service.EditAsync(id, 0, [new() { Tool = SkinTool.Pencil, Surface = front, X = 2, Y = 2, Color = "#00FF00" }], TestContext.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitAsync(TestContext.CancellationToken));
        Assert.IsTrue(session.HasStroke);
        CollectionAssert.AreEqual(draft, session.Render().Pixels.ToArray());
        Assert.AreEqual("#000000FF", SkinEditor.Sample(service.Read(id), front, 1, 1));
        session.Cancel();
        Assert.IsFalse(session.HasStroke);
        Assert.AreEqual(1L, service.Read(id).Revision);
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(service.Read(id), front, 2, 2));
    }

    // Design review: library symmetry cases do not exercise Core preview -> durable commit -> history.
    // This catches a lost mirror flag or two separate transactions by checking independently specified
    // opposing arm pixels, the untouched disk before commit, and one undo/redo restoring both sides.
    /// <summary>A mirrored gesture on either model and resolution remains one atomic history entry.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64)]
    [DataRow(SkinModel.Classic, 128)]
    [DataRow(SkinModel.Slim, 64)]
    [DataRow(SkinModel.Slim, 128)]
    public async Task MirroredStroke_CommitsAndRestoresBothArmsTogether(SkinModel model, int size)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(model, color: "#000000", size: size, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var right = new SkinSurface { Part = SkinPart.RightArm, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var left = right with { Part = SkinPart.LeftArm };
        var reflectedX = ((model == SkinModel.Slim ? 3 : 4) * (size / 64)) - 2;
        session.Begin(new() { Tool = SkinTool.Stroke, Surface = right, Color = "#FF2040", Mirror = SkinMirrorMode.Body });
        session.Append(new(1, 2));
        var marks = session.BrushFootprint(new() { Tool = SkinTool.Pencil, Surface = right, X = 1, Y = 2, Color = "#FF2040", Mirror = SkinMirrorMode.Body });
        Assert.Contains(mark => mark.Surface == left && mark.X == reflectedX && mark.Y == 2, marks, "Projected cursor must include the reflected arm.");
        Assert.AreEqual(0L, service.Read(id).Revision, "Preview must not publish one side early.");
        await session.CommitAsync(TestContext.CancellationToken);
        var committed = service.Read(id);
        Assert.AreEqual(1L, committed.Revision);
        Assert.AreEqual("#FF2040FF", SkinEditor.Sample(committed, right, 1, 2));
        Assert.AreEqual("#FF2040FF", SkinEditor.Sample(committed, left, reflectedX, 2));
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        var undone = service.Read(id);
        Assert.AreEqual("#000000FF", SkinEditor.Sample(undone, right, 1, 2));
        Assert.AreEqual("#000000FF", SkinEditor.Sample(undone, left, reflectedX, 2));
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("#FF2040FF", SkinEditor.Sample(service.Read(id), left, reflectedX, 2));
    }

    // Author review: existing geometry previews use composite pixels and cannot establish fill sampling.
    // A transparent layer with a solid divider has an independently known 3x8 connected region,
    // while a differently colored background seed would make a composite-based preview return one cell.
    // Preview must preserve revision, pixels and history; committing that same edit must affect exactly the preview.
    /// <summary>Fill preview samples the addressed layer, remains read-only and matches the durable edit.</summary>
    [TestMethod]
    public async Task FillPreview_UsesLayerConnectivityWithoutPublishing()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var edits = new List<SkinEdit>
        {
            new() { Tool = SkinTool.Pencil, Surface = front, X = 1, Y = 1, Color = "#FF0000" },
            new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "paint", Name = "Paint" } },
        };
        for (var y = 0; y < 8; y++) edits.Add(new() { Tool = SkinTool.Pencil, PaintLayerId = "paint", Surface = front, X = 3, Y = y, Color = "#00FF00" });
        await service.EditAsync(id, 0, [.. edits], TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var before = session.LayerPixels("paint");
        var (Revision, Entries) = session.History();
        var edit = new SkinEdit { Tool = SkinTool.Fill, PaintLayerId = "paint", Surface = front, X = 1, Y = 1, Color = "#0000FF" };
        var marks = session.FillFootprint(edit);
        Assert.HasCount(24, marks);
        Assert.IsTrue(marks.All(mark => mark.Surface == front && mark.X < 3 && mark.Y < 8));
        Assert.AreEqual(24, marks.Select(mark => (mark.X, mark.Y)).Distinct().Count());
        CollectionAssert.AreEqual(before, session.LayerPixels("paint"));
        Assert.AreEqual(Revision, session.Revision);
        Assert.HasCount(Entries.Length, session.History().Entries);
        Assert.AreEqual(Revision, service.Read(id).Revision);
        await session.ApplyAsync(edit, TestContext.CancellationToken);
        var after = session.LayerPixels("paint");
        var face = SkinLayout.Face(SkinModel.Classic, front, 64);
        var expected = marks.Select(mark => ((face.Y + mark.Y) * 64) + face.X + mark.X).ToHashSet();
        for (var index = 0; index < after.Length; index++)
            Assert.AreEqual(expected.Contains(index) ? 0x0000FFFFu : before[index], after[index]);
        Assert.AreEqual(Revision + 1, session.Revision);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        CollectionAssert.AreEqual(before, session.LayerPixels("paint"));
    }

    // Author review: library clipping cannot establish durable session publication or history replay.
    // This real storage case checks the frozen Begin mask, an excluded mirror and one-step undo/redo.
    /// <summary>Masked editor drawing persists only selected pixels and replays through the shared history.</summary>
    [TestMethod]
    public async Task AtlasSelection_CommitsClippedPixelsThroughSharedHistory()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        var face = SkinLayout.Face(SkinModel.Classic, front, 64);
        var index = ((face.Y + 1) * 64) + face.X + 1;
        var mask = new bool[4096]; mask[index] = true;
        var edit = new SkinEdit { Tool = SkinTool.Stroke, Surface = front, Color = "#FF2040", BrushSize = 5, Mirror = SkinMirrorMode.Body, PixelMask = mask };
        session.Begin(edit);
        Array.Fill(mask, false);
        session.Append(new(1, 1)); session.Append(new(5, 5));
        var expected = service.Read(id).Pixels.ToArray(); expected[index] = 0xFF2040FF;
        CollectionAssert.AreEqual(expected, session.Render().Pixels.ToArray());
        Assert.AreEqual(0L, service.Read(id).Revision);
        await session.CommitAsync(TestContext.CancellationToken);
        CollectionAssert.AreEqual(expected, service.Read(id).Pixels);
        Assert.HasCount(2, session.History().Entries, "The initial state and one gesture form two timeline positions.");
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("#000000FF", SkinEditor.Sample(service.Read(id), front, 1, 1));
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        CollectionAssert.AreEqual(expected, service.Read(id).Pixels);
        mask[index] = true;
        var fill = edit with { Tool = SkinTool.Fill, X = 1, Y = 1, Color = "#0000FF", Mirror = SkinMirrorMode.None };
        Assert.HasCount(1, session.FillFootprint(fill));
        await session.ApplyAsync(fill, TestContext.CancellationToken);
        expected[index] = 0x0000FFFF;
        CollectionAssert.AreEqual(expected, service.Read(id).Pixels);
    }

    /// <summary>Gets or sets cooperative test cancellation.</summary>
    public TestContext TestContext { get; set; } = null!;
}