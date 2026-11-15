// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Tests editor selection and mixed layer/drawing transactions through real Core persistence; library raster and CLI serialization have separate coverage.</summary>
[TestClass]
public sealed class SkinLayerSessionTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    // Design review: existing library/CLI layer tests do not exercise the editor session's selection publication,
    // its mixed gesture/property history, or no-op edits after undo. Known red/blue overlap distinguishes stack order.
    /// <summary>One shared history restores deleted layer identity, pixels and metadata, then ordinary drawing; no-op layer commands retain redo.</summary>
    [TestMethod]
    public async Task LayerAndStrokeHistory_RestoresDeletedContentAndPreservesRedoForNoOps()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await Layer(session, new() { Operation = SkinLayerOperation.Add, Id = "under", Name = "Under" });
        await Draw(session, "#FF0000");
        await Layer(session, new() { Operation = SkinLayerOperation.Add, Id = "top", Name = "Top" });
        Assert.AreEqual("top", session.ActiveLayerId, "New layer must become the painting destination.");
        await Draw(session, "#0000FF");
        await Layer(session, new() { Operation = SkinLayerOperation.Set, Id = "top", Name = "Details" });
        await Layer(session, new() { Operation = SkinLayerOperation.Set, Id = "top", Visible = false });
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        await Layer(session, new() { Operation = SkinLayerOperation.Reorder, Id = "top", Index = 0 });
        await Layer(session, new() { Operation = SkinLayerOperation.Remove, Id = "top" });
        Assert.AreEqual("under", session.ActiveLayerId);

        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        var restored = service.Read(id);
        Assert.AreEqual("top", restored.Layers[0].Id);
        Assert.AreEqual("Details", restored.Layers[0].Name);
        Assert.IsFalse(restored.Layers[0].Visible);
        var texel = SkinLayout.Face(SkinModel.Classic, Front, 64);
        Assert.AreEqual(0x0000FFFFu, restored.Layers[0].Pixels[(texel.Y * 64) + texel.X]);
        Assert.AreEqual("under", session.ActiveLayerId, "Restoring a deleted layer must not steal a still-valid selection.");
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken); // reorder
        Assert.AreEqual("top", service.Read(id).Layers[1].Id);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken); // visibility
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken); // rename
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken); // blue stroke
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        var revision = session.Revision;
        await Layer(session, new() { Operation = SkinLayerOperation.Set, Id = "top", Name = "Top", Visible = true });
        await Layer(session, new() { Operation = SkinLayerOperation.Reorder, Id = "top", Index = 1 });
        Assert.AreEqual(revision, session.Revision);
        Assert.IsTrue(session.CanRedo, "No-op metadata must not discard drawing redo.");
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
    }

    // Design review: null is a real background destination. This protects against silent selection fallback or
    // a rejected drawing operation partially writing another layer; it is not a test of the metadata getters alone.
    /// <summary>Unpaintable selected layers stay selected and reject both gestures and fill without writing the background.</summary>
    [TestMethod]
    [DataRow(false, false, 1d)]
    [DataRow(true, true, 1d)]
    [DataRow(true, false, 0d)]
    public async Task UnpaintableSelection_RejectsDrawingWithoutRedirect(bool visible, bool locked, double opacity)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await Layer(session, new() { Operation = SkinLayerOperation.Add, Id = "ink" });
        await Layer(session, new() { Operation = SkinLayerOperation.Set, Id = "ink", Visible = visible, Locked = locked, Opacity = opacity });
        session.SelectLayer("ink");
        var background = session.LayerPixels(SkinDocument.BackgroundLayerId);
        var revision = session.Revision;
        var draw = new SkinEdit { Tool = SkinTool.Stroke, Surface = Front, PaintLayerId = session.ActiveLayerId, Color = "#FF0000" };
        Assert.IsFalse(session.CanPaint);
        Assert.AreEqual("ink", session.ActiveLayerId);
        Assert.Throws<InvalidOperationException>(() => session.Begin(draw));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync(draw with { Tool = SkinTool.Fill }, TestContext.CancellationToken));
        Assert.IsFalse(session.HasStroke);
        Assert.AreEqual(revision, service.Read(id).Revision);
        CollectionAssert.AreEqual(background, session.LayerPixels(SkinDocument.BackgroundLayerId));
        Assert.IsTrue(session.LayerPixels("ink").All(pixel => pixel == 0));
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("ink", session.ActiveLayerId);
        Assert.IsTrue(session.CanPaint);
    }

    // Design review: await a real external write before the editor command to deterministically cross the revision
    // boundary. It adds session metadata/selection preservation evidence absent from the existing stroke-conflict case.
    /// <summary>A conflicting rename, reorder or delete cannot publish optimistic metadata or change the selected destination.</summary>
    [TestMethod]
    [DataRow(SkinLayerOperation.Set)]
    [DataRow(SkinLayerOperation.Reorder)]
    [DataRow(SkinLayerOperation.Remove)]
    public async Task ConflictingLayerChange_PreservesSessionAndExternalWrite(SkinLayerOperation operation)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, color: "#000000", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await Layer(session, new() { Operation = SkinLayerOperation.Add, Id = "a", Name = "A" });
        await Layer(session, new() { Operation = SkinLayerOperation.Add, Id = "b", Name = "B" });
        session.SelectLayer("a");
        var original = session.Render().Pixels.ToArray();
        var revision = session.Revision;
        await service.EditAsync(id, revision, [new() { Tool = SkinTool.Pencil, Surface = Front, Color = "#00FF00" }], TestContext.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Layer(session, new() { Operation = operation, Id = "a", Name = "Changed", Index = 1 }));
        Assert.AreEqual(revision, session.Revision);
        Assert.AreEqual("a", session.ActiveLayerId);
        CollectionAssert.AreEqual(original, session.Render().Pixels.ToArray());
        CollectionAssert.AreEqual(expected, session.Layers.Select(layer => layer.Id).ToArray());
        Assert.AreEqual("A", session.Layers[1].Name);
        var persisted = service.Read(id);
        Assert.AreEqual(revision + 1, persisted.Revision);
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(persisted, Front, 0, 0));
        Assert.AreEqual("A", persisted.Layers[0].Name);
    }

    private Task Layer(SkinEditingSession session, SkinLayerChange change) =>
        session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = change }, TestContext.CancellationToken);
    private async Task Draw(SkinEditingSession session, string color)
    {
        session.Begin(new() { Tool = SkinTool.Stroke, Surface = Front, PaintLayerId = session.ActiveLayerId, Color = color });
        session.Append(new(0, 0));
        await session.CommitAsync(TestContext.CancellationToken);
    }
    /// <summary>Gets or sets cooperative test cancellation.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] expected = ["background", "a", "b"];
}