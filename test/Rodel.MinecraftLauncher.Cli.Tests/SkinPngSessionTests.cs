// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises frozen PNG input/output at the real editor-session and persistence boundary; codec/layout algorithms have separate library coverage.</summary>
[TestClass]
public sealed class SkinPngSessionTests
{
    private static readonly SkinSurface Front = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };

    // Author review: existing CLI import/history tests do not cover an already-open session, frozen bytes,
    // mutable preview isolation or preserving complete paint layers across a model/size replacement.
    // A known blue input, deleted source and independent layer metadata distinguish reload/flatten/two-step bugs.
    /// <summary>Import uses the reviewed bytes once and restores complete prior content through the shared undo/redo chain.</summary>
    [TestMethod]
    [DataRow(SkinModel.Slim, 64, SkinModel.Classic, 128)]
    [DataRow(SkinModel.Classic, 128, SkinModel.Slim, 64)]
    public async Task ImportFrozenPng_UndoRestoresModelSizeLayersAndDrawing(SkinModel oldModel, int oldSize, SkinModel newModel, int newSize)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(oldModel, color: "#000000", size: oldSize, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await Layer(session, new() { Operation = SkinLayerOperation.Add, Id = "ink", Name = "Keep this layer" });
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, Surface = Front, X = 0, Y = 0, Color = "#FF0000", PaintLayerId = "ink" }, TestContext.CancellationToken);
        await Layer(session, new() { Operation = SkinLayerOperation.Set, Id = "ink", Visible = false, Locked = true, Opacity = 0.4 });
        session.SelectLayer("ink");
        var original = service.Read(id);
        var path = Path.Combine(sandbox.Root, "input.png");
        await File.WriteAllBytesAsync(path, SkinPng.Encode(SkinEditor.Create(newModel, "#0000FF", newSize)), TestContext.CancellationToken);
        var input = await SkinService.PrepareImportAsync(path, cancellationToken: TestContext.CancellationToken);
        Array.Fill(input.Decode(newModel).Pixels, 0xFF0000FFu);
        await File.WriteAllBytesAsync(path, [0, 1, 2], TestContext.CancellationToken);
        File.Delete(path);

        await session.ImportAsync(input, newModel, TestContext.CancellationToken);
        Assert.AreEqual(original.Revision + 1, session.Revision);
        Assert.AreEqual(newModel, session.Render().Model);
        Assert.AreEqual(newSize, session.Render().Size);
        Assert.HasCount(1, session.Layers); // PNG replaces additional layers, retaining only its background.
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        Assert.AreEqual("import", service.Read(id).Transaction.Kind);

        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        var restored = service.Read(id);
        Assert.AreEqual(oldModel, restored.Model);
        Assert.AreEqual(oldSize, restored.Size);
        CollectionAssert.AreEqual(original.Pixels, restored.Pixels);
        var layer = restored.Layers.Single();
        Assert.AreEqual("ink", layer.Id);
        Assert.AreEqual("Keep this layer", layer.Name);
        Assert.IsFalse(layer.Visible);
        Assert.IsTrue(layer.Locked);
        Assert.AreEqual(0.4, layer.Opacity);
        CollectionAssert.AreEqual(original.Layers.Single().Pixels, layer.Pixels);
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(newSize, session.Render().Size);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(service.Read(id).Layers.Single().Visible); // The preceding metadata transaction is still in the same history.
    }

    // Author review: codec legacy conversion is already tested. This protects the new empty-editor ownership:
    // invalid model must publish nothing, while successful creation has one import history step and no extra drawing layer.
    /// <summary>Legacy input creates one project atomically and undo returns to its explicit same-size blank baseline.</summary>
    [TestMethod]
    public async Task CreateImportedLegacy_RejectsSlimWithoutProjectAndKeepsOneUndoStep()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        Directory.CreateDirectory(sandbox.Root);
        var path = Path.Combine(sandbox.Root, "legacy.png");
        using (var output = new MemoryStream())
        {
            var rgba = new byte[64 * 32 * 4];
            for (var i = 0; i < rgba.Length; i += 4) { rgba[i + 2] = 255; rgba[i + 3] = 255; }

            new StbImageWriteSharp.ImageWriter().WritePng(rgba, 64, 32, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
            await File.WriteAllBytesAsync(path, output.ToArray(), TestContext.CancellationToken);
        }

        var input = await SkinService.PrepareImportAsync(path, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(input.IsLegacy);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.CreateImportedAsync(input, SkinModel.Slim, "Invalid", TestContext.CancellationToken));
        Assert.IsEmpty(service.List());
        var session = await service.CreateImportedAsync(input, SkinModel.Classic, "Legacy project", TestContext.CancellationToken);
        Assert.HasCount(1, service.List());
        Assert.AreEqual(1L, session.Revision);
        Assert.HasCount(1, service.Read(session.Id).Undo);
        Assert.IsEmpty(service.Read(session.Id).Layers);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("#808080FF", SkinEditor.Sample(service.Read(session.Id), Front, 0, 0));
        Assert.IsFalse(session.CanUndo);
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(service.Read(session.Id), Front, 0, 0));
    }

    // Author review: a real exclusive project handle reaches Core's read failure after obtaining the resource lease.
    // An unchanged session and later retry after deleting the source distinguish lost drafts and path-based retry bugs.
    /// <summary>Import read failure preserves the session and the same frozen input can be retried after releasing the actual file.</summary>
    [TestMethod]
    public async Task ImportLockedProject_RetryUsesFrozenInputWithoutChangingHistoryOnFailure()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var input = await Prepare(sandbox.Root);
        File.Delete(Path.Combine(sandbox.Root, "input.png"));
        using (var blocked = new FileStream(Path.Combine(sandbox.Root, "skins", id + ".rmcs"), FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => session.ImportAsync(input, SkinModel.Slim, TestContext.CancellationToken));
        Assert.AreEqual(0L, session.Revision);
        Assert.AreEqual(64, session.Render().Size);
        Assert.IsFalse(session.CanUndo);
        await session.ImportAsync(input, SkinModel.Slim, TestContext.CancellationToken);
        Assert.AreEqual(1L, session.Revision);
        Assert.AreEqual(128, session.Render().Size);
        Assert.AreEqual(SkinModel.Slim, session.Render().Model);
    }

    // Author review: import shares the service lease but must not reopen/adopt an external revision before retry.
    // Known external green pixels and the old gray session make an accidental rebase or partial publication observable.
    /// <summary>Repeated import conflicts preserve both the local snapshot and a real external writer's content.</summary>
    [TestMethod]
    public async Task ImportConflict_RetryDoesNotRebaseOrOverwriteExternalContent()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var input = await Prepare(sandbox.Root);
        await service.EditAsync(id, 0, [new() { Tool = SkinTool.Pencil, Surface = Front, X = 0, Y = 0, Color = "#00FF00" }], TestContext.CancellationToken);
        for (var attempt = 0; attempt < 2; attempt++)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.ImportAsync(input, SkinModel.Slim, TestContext.CancellationToken));
        Assert.AreEqual(0L, session.Revision);
        Assert.AreEqual(64, session.Render().Size);
        Assert.IsFalse(session.CanUndo);
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        Assert.AreEqual(1L, service.Read(id).Revision);
    }

    // Author review: existing CLI export rereads disk and does not cover a stale, intentionally retained session.
    // A locked existing output reaches File.Replace failure; its independent old bytes must survive, with no history effects.
    /// <summary>Session export uses its committed pixels, preserves an existing target on replacement failure, and never changes history.</summary>
    [TestMethod]
    public async Task ExportSessionSnapshot_ExternalWriteAndOutputFailurePreserveExpectedContent()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, color: "#0000FF", size: 128, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await service.EditAsync(id, 0, [new() { Tool = SkinTool.Pencil, Surface = Front, X = 0, Y = 0, Color = "#00FF00" }], TestContext.CancellationToken);
        var output = Path.Combine(sandbox.Root, "output.png");
        var old = SkinPng.Encode(SkinEditor.Create(SkinModel.Classic, "#FF0000"));
        await File.WriteAllBytesAsync(output, old, TestContext.CancellationToken);
        using (var blocked = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => session.ExportAsync(output, overwrite: true, TestContext.CancellationToken));
        CollectionAssert.AreEqual(old, await File.ReadAllBytesAsync(output, TestContext.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => session.ExportAsync(output, cancellationToken: TestContext.CancellationToken));
        await session.ExportAsync(output, overwrite: true, TestContext.CancellationToken);
        var exported = SkinPng.Decode(await File.ReadAllBytesAsync(output, TestContext.CancellationToken), SkinModel.Slim);
        Assert.AreEqual(128, exported.Size);
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(exported, Front, 0, 0));
        Assert.AreEqual("#00FF00FF", SkinEditor.Sample(service.Read(id), Front, 0, 0));
        Assert.AreEqual(0L, session.Revision);
        Assert.IsFalse(session.CanUndo);
        Assert.IsFalse(session.CanRedo);
    }

    /// <summary>Gets or sets the test runner's cancellation and diagnostics context.</summary>
    public TestContext TestContext { get; set; }

    private Task Layer(SkinEditingSession session, SkinLayerChange change) => session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = change }, TestContext.CancellationToken);
    private async Task<SkinPngImport> Prepare(string root)
    {
        var path = Path.Combine(root, "input.png");
        await File.WriteAllBytesAsync(path, SkinPng.Encode(SkinEditor.Create(SkinModel.Slim, "#0000FF", 128)), TestContext.CancellationToken);
        return await SkinService.PrepareImportAsync(path, cancellationToken: TestContext.CancellationToken);
    }
}