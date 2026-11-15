// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects timeline navigation at the live session and durable optimistic-concurrency boundary.</summary>
[TestClass]
public sealed class SkinHistorySessionTests
{
    // Author review: library RestoreTo tests do not protect session publication/selection or accidentally reading
    // a newer disk history. These checks use a real mixed layer/paint timeline and compare disk plus live pixels.
    /// <summary>Jumping across layer and pixel edits preserves future states, selection and the shared undo chain.</summary>
    [TestMethod]
    public async Task JumpAcrossTransactions_PublishesOncePreservesFutureAndBranches()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var (Revision, Entries) = session.History();
        Assert.HasCount(1, Entries);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ink", Name = "Ink" } }, TestContext.CancellationToken);
        var layerState = session.History();
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, PaintLayerId = "ink", Surface = Front, X = 2, Y = 3, Color = "#FF0000" }, TestContext.CancellationToken);
        var painted = session.Render().Pixels.ToArray();
        var latest = session.History();
        await session.RestoreToAsync(0, latest.Revision, TestContext.CancellationToken);
        Assert.AreEqual(latest.Revision + 1, session.Revision);
        Assert.HasCount(1, session.Layers);
        Assert.AreEqual(SkinDocument.BackgroundLayerId, session.ActiveLayerId);
        Assert.HasCount(3, session.History().Entries);
        Assert.IsTrue(session.CanRedo);
        await session.RestoreToAsync(2, session.History().Revision, TestContext.CancellationToken);
        CollectionAssert.AreEqual(painted, session.Render().Pixels.ToArray());
        Assert.HasCount(2, session.Layers);
        Assert.AreEqual(session.Revision, service.Read(id).Revision);
        var revision = session.Revision;
        await session.RestoreToAsync(2, revision, TestContext.CancellationToken);
        Assert.AreEqual(revision, session.Revision); // Clicking current position is a no-op.
        await session.RestoreToAsync(layerState.Entries.Single(entry => entry.Current).Index, revision, TestContext.CancellationToken);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "ink", Name = "Branch" } }, TestContext.CancellationToken);
        Assert.IsFalse(session.CanRedo);
        Assert.AreEqual("Branch", session.Layers.Single(layer => layer.Id == "ink").Name);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("Ink", session.Layers.Single(layer => layer.Id == "ink").Name);
    }

    // Author review: an old UI row index may still exist after another writer or a local restore. Rejecting the
    // observed revision is essential; checking only range would restore a different state. Failure must not publish.
    /// <summary>External and locally stale history requests retain the current session and do not overwrite another writer.</summary>
    [TestMethod]
    public async Task StaleTimelineAndActiveDraft_AreRejectedWithoutPublication()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, Surface = Front, X = 0, Y = 0, Color = "#FF0000" }, TestContext.CancellationToken);
        var (Revision, Entries) = session.History(); var pixels = session.Render().Pixels.ToArray();
        await service.EditAsync(id, session.Revision, [new() { Tool = SkinTool.Pencil, Surface = Front, X = 1, Y = 0, Color = "#00FF00" }], TestContext.CancellationToken);
        var foreign = service.Read(id);
        Assert.AreEqual(Revision, session.History().Revision); // History is session-owned, not a disk reread.
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreToAsync(0, Revision, TestContext.CancellationToken));
        Assert.AreEqual(Revision, session.Revision);
        CollectionAssert.AreEqual(pixels, session.Render().Pixels.ToArray());
        Assert.AreEqual(foreign.Revision, service.Read(id).Revision);
        session = service.OpenEditor(id);
        var old = session.History();
        await session.RestoreToAsync(0, old.Revision, TestContext.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreToAsync(1, old.Revision, TestContext.CancellationToken));
        var current = session.History();
        session.Begin(new() { Tool = SkinTool.Pencil, Surface = Front, X = 4, Y = 4, Color = "#0000FF" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreToAsync(1, current.Revision, TestContext.CancellationToken));
        Assert.IsTrue(session.HasStroke);
        session.Cancel();
        await session.RestoreToAsync(2, current.Revision, TestContext.CancellationToken); // Rejection released the saving gate.
        CollectionAssert.AreEqual(foreign.Pixels, service.Read(id).Pixels);
    }

    /// <summary>Gets or sets the runner cancellation and diagnostics context.</summary>
    public TestContext TestContext { get; set; }

    private static SkinSurface Front => new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
}