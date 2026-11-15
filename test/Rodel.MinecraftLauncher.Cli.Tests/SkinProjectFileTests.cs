// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects file-picker project boundaries using real project files and persistence.</summary>
[TestClass]
public sealed class SkinProjectFileTests
{
    // Author review: existing session tests do not distinguish managed paths from same-ID external files.
    // Real storage, preserved undo/redo and source bytes detect accidental overwrite, flattening or receipt migration.
    /// <summary>Opening managed paths reuses identity; external copies preserve content history but not execution receipts.</summary>
    [TestMethod]
    public async Task OpenFile_PreservesHistoryAndSourceWithIndependentIdentity()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, template: "alex", name: "Keep name", size: 128, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ink", Name = "Ink" } }, TestContext.CancellationToken);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "ink", Name = "Renamed" } }, TestContext.CancellationToken);
        await session.RestoreAsync(false, TestContext.CancellationToken);
        var managed = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var (Session, Name) = await service.OpenProjectFileAsync(Path.Combine(sandbox.Root, "skins", "..", "skins", id + ".rmcs"), TestContext.CancellationToken);
        Assert.AreEqual(id, Session.Id);
        // Extension boundary review: stale development JSON and unrelated data must not be discovered
        // as projects or rewritten. This extends the existing real managed-file/listing workflow.
        var unrelated = Path.Combine(sandbox.Root, "skins", "unrelated.json");
        await File.WriteAllTextAsync(unrelated, "keep unrelated data", TestContext.CancellationToken);
        Assert.HasCount(1, service.List());
        Assert.AreEqual("keep unrelated data", await File.ReadAllTextAsync(unrelated, TestContext.CancellationToken));

        // A same-stem backup is a distinct source, not authority to edit its .rmcs sibling.
        var backup = Path.ChangeExtension(managed, ".bak");
        File.Copy(managed, backup);
        var backupCopy = await service.OpenProjectFileAsync(backup, TestContext.CancellationToken);
        Assert.AreNotEqual(id, backupCopy.Session.Id);

        var node = LegacyProject(service.Read(id), "Keep name");
        node["receipts"] = new JsonArray(new JsonObject
        {
            ["operationId"] = Guid.NewGuid().ToString("N"),
            ["documentId"] = id,
            ["baseRevision"] = 0,
            ["revision"] = 1,
            ["changed"] = true,
            ["committedAt"] = DateTimeOffset.UtcNow,
        });
        var outside = Path.Combine(sandbox.Root, "outside"); Directory.CreateDirectory(outside);
        var source = Path.Combine(outside, id + ".rmcs");
        await File.WriteAllTextAsync(source, node.ToJsonString(), TestContext.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
        var copy = await service.OpenProjectFileAsync(source, TestContext.CancellationToken);
        Assert.AreNotEqual(id, copy.Session.Id);
        Assert.AreEqual("Keep name", copy.Name);
        Assert.AreEqual(SkinModel.Slim, copy.Session.Render().Model);
        Assert.AreEqual(128, copy.Session.Render().Size);
        var stored = Manifest(Path.Combine(sandbox.Root, "skins", copy.Session.Id + ".rmcs"));
        Assert.IsTrue(JsonNode.DeepEquals(node["document"],
            JsonSerializer.SerializeToNode(service.Read(copy.Session.Id), SkinJsonContext.Default.SkinDocument)));
        Assert.HasCount(0, stored["receipts"]!.AsArray());
        Assert.IsTrue(copy.Session.CanUndo); Assert.IsTrue(copy.Session.CanRedo);
        await copy.Session.RestoreAsync(true, TestContext.CancellationToken);
        Assert.AreEqual("Renamed", service.Read(copy.Session.Id).Layers.Single().Name);
        await copy.Session.RestoreAsync(false, TestContext.CancellationToken);
        await copy.Session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "ink", Visible = false } }, TestContext.CancellationToken);
        Assert.IsFalse(service.Read(copy.Session.Id).Layers.Single().Visible);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        Assert.AreEqual("Ink", service.Read(id).Layers.Single().Name);
    }

    // Author review: this boundary must not publish invalid content or return a session after real disk failure.
    // A malformed history and an actual directory collision distinguish validation and atomic-publication failures.
    /// <summary>Invalid content and publication failures preserve the source and permit a clean retry.</summary>
    [TestMethod]
    public async Task OpenFile_RejectsInvalidHistoryAndRecoversFromPublicationFailure()
    {
        using var sandbox = new CliSandbox();
        var sourceService = new SkinService(new LauncherPaths(Path.Combine(sandbox.Root, "source")));
        var id = await sourceService.CreateAsync(SkinModel.Classic, template: "classic", cancellationToken: TestContext.CancellationToken);
        var source = Path.Combine(sandbox.Root, "source", "skins", id + ".rmcs");
        var bytes = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
        var destination = Path.Combine(sandbox.Root, "target"); Directory.CreateDirectory(destination);
        var service = new SkinService(new LauncherPaths(destination));
        var invalid = Path.Combine(sandbox.Root, "invalid.rmcs");
        var node = LegacyProject(sourceService.Read(id), "Untitled");
        var history = node["document"]!.DeepClone().AsObject();
        foreach (var field in new[] { "schemaVersion", "revision", "undo", "redo" }) history.Remove(field);
        history["size"] = 7; node["document"]!["undo"] = new JsonArray(history);
        await File.WriteAllTextAsync(invalid, node.ToJsonString(), TestContext.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenProjectFileAsync(invalid, TestContext.CancellationToken));
        Assert.HasCount(0, service.List());
        var collision = Path.Combine(destination, "skins");
        await File.WriteAllTextAsync(collision, "keep", TestContext.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => service.OpenProjectFileAsync(source, TestContext.CancellationToken));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(collision, TestContext.CancellationToken));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        File.Delete(collision);
        var (Session, Name) = await service.OpenProjectFileAsync(source, TestContext.CancellationToken);
        Assert.HasCount(1, service.List());
        Assert.AreNotEqual(id, Session.Id);
    }

    // Author review: the existing open-file cases do not exercise the save-as CLI/session or an independent
    // retained redo branch. Reopening the actual file and restoring its hidden layer detects flattening and lost history.
    /// <summary>Save-as preserves editable content and history without modifying the source or importing execution receipts.</summary>
    [TestMethod]
    public async Task SaveAs_ThroughCli_PreservesLayersHistoryAndIndependentSource()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, size: 128, name: "Video example", cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "details", Name = "Details" } }, TestContext.CancellationToken);
        var front = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front };
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, Surface = front, PaintLayerId = "details", Color = "#CC3377", X = 1, Y = 2 }, TestContext.CancellationToken);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "details", Visible = false } }, TestContext.CancellationToken);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        var source = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var original = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
        var output = Path.Combine(sandbox.Root, "video example.rmcs");
        var saved = (await sandbox.RunAsync("skin", "save-as", id, "--revision", session.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), "--output", output)).ReadEnvelope(0);
        Assert.AreEqual(Path.GetFullPath(output), saved["data"]!["path"]!.ToString());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        var (Session, Name) = await service.OpenProjectFileAsync(output, TestContext.CancellationToken);
        Assert.AreNotEqual(id, Session.Id);
        Assert.AreEqual("Video example", Name);
        Assert.AreEqual(SkinModel.Slim, Session.Render().Model);
        Assert.AreEqual(128, Session.Render().Size);
        Assert.IsTrue(Session.CanUndo && Session.CanRedo);
        Assert.AreEqual("#CC3377FF", SkinEditor.Sample(service.Read(Session.Id), front, 1, 2));
        await Session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(Session.Layers.Single(layer => layer.Id == "details").Visible);
        Assert.IsTrue(session.Layers.Single(layer => layer.Id == "details").Visible);
        Assert.HasCount(0, Manifest(output)["receipts"]!.AsArray());
    }

    // Author review: real failed publication/cancellation/stale revisions must not replace an existing archive,
    // alter the original, or strand the session in saving state. These are save-as boundaries, not pixel tests.
    /// <summary>A failed or canceled save-as preserves existing files and allows an explicitly authorized retry.</summary>
    [TestMethod]
    public async Task SaveAs_RejectionsPreserveFilesAndAllowRetry()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var source = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var original = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
        var output = Path.Combine(sandbox.Root, "copy.rmcs");
        await File.WriteAllTextAsync(output, "keep destination", TestContext.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => session.SaveProjectAsAsync(output, cancellationToken: TestContext.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SaveProjectAsAsync(source, overwrite: true, TestContext.CancellationToken));
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.SaveProjectAsAsync(output, overwrite: true, canceled.Token));
        }

        Assert.AreEqual("keep destination", await File.ReadAllTextAsync(output, TestContext.CancellationToken));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        await session.SaveProjectAsAsync(output, overwrite: true, TestContext.CancellationToken);
        Assert.AreEqual("Untitled", (await service.OpenProjectFileAsync(output, TestContext.CancellationToken)).Name);
        await service.EditAsync(id, session.Revision, [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "external" } }], TestContext.CancellationToken);
        var preserved = await File.ReadAllBytesAsync(output, TestContext.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveProjectAsAsync(output, overwrite: true, TestContext.CancellationToken));
        CollectionAssert.AreEqual(preserved, await File.ReadAllBytesAsync(output, TestContext.CancellationToken));
    }

    // Author review: full-snapshot JSON duplicated unchanged textures into every history state. This checks
    // actual archive reuse, independent mutable decoded arrays, redo and corruption detection, not a timing threshold.
    /// <summary>Identical texture history shares compressed blobs without aliasing current pixels or accepting corrupted content.</summary>
    [TestMethod]
    public async Task SharedTextureHistory_RetainsIndependentStatesAndRejectsCorruptBlob()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, size: 128, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "ink" } }, TestContext.CancellationToken);
        for (var index = 0; index < 6; index++)
            await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "ink", Visible = index % 2 != 0 } }, TestContext.CancellationToken);
        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        var path = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var document = service.Read(id);
        var legacy = LegacyProject(document, "Untitled").ToJsonString();
        Assert.IsLessThan(legacy.Length / 10, new FileInfo(path).Length, "Unchanged full-size textures must not be repeated in every serialized history state.");
        using (var archive = ZipFile.OpenRead(path))
        {
            Assert.HasCount(2, archive.Entries.Where(entry => entry.FullName.StartsWith("textures/", StringComparison.Ordinal)).ToArray(),
                "Only the opaque background and empty layer are distinct textures, despite multiple visibility/history states.");
        }

        document.Layers[0].Pixels[0] = 0x12345678;
        Assert.AreEqual(0u, document.Undo[^1].Layers[0].Pixels[0], "Decoded shared blobs must not alias mutable current and historical arrays.");
        await session.RestoreAsync(redo: true, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(service.Read(id).Layers[0].Visible);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var blob = archive.Entries.First(entry => entry.FullName.StartsWith("textures/", StringComparison.Ordinal));
            byte[] bytes;
            using (var stream = blob.Open())
            {
                using var copy = new MemoryStream(); stream.CopyTo(copy); bytes = copy.ToArray();
            }

            bytes[0] ^= 1;
            using var changed = blob.Open(); changed.Position = 0; changed.Write(bytes);
        }

        Assert.Throws<InvalidDataException>(() => service.Read(id));
    }

    private static JsonObject LegacyProject(SkinDocument document, string name) => new()
    {
        ["document"] = JsonSerializer.SerializeToNode(document, SkinJsonContext.Default.SkinDocument),
        ["name"] = name,
        ["modifiedAt"] = DateTimeOffset.UtcNow,
        ["receipts"] = new JsonArray()
    };

    private static JsonObject Manifest(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var stream = archive.GetEntry("project.json")!.Open();
        return JsonNode.Parse(stream)!.AsObject();
    }

    /// <summary>Gets or sets cancellation and diagnostics supplied by the test host.</summary>
    public TestContext TestContext { get; set; }
}