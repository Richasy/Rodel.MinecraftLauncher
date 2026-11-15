// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using StbImageSharp;
using StbImageWriteSharp;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects transparent layer sharing through real project transactions and CLI/MCP file boundaries.</summary>
[TestClass]
public sealed class SkinLayerPngTests
{
    // Author review: whole-skin import intentionally makes Base opaque and replaces existing layers.
    // Existing PNG/session tests cannot detect either mistake in the new additive path. The independent
    // PNG fixture has alpha 0/128/255, including Base and unused atlas pixels, and no production encoder.
    /// <summary>A frozen overlay adds one selected layer and restores the entire prior stack in one undo step.</summary>
    [TestMethod]
    [DataRow(SkinModel.Classic, 64)]
    [DataRow(SkinModel.Slim, 128)]
    public async Task ImportLayer_PreservesOriginalRgbaAndStackInOneUndoStep(SkinModel model, int size)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(model, color: "#123456", size: size, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "keep", Name = "Keep" } }, TestContext.CancellationToken);
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, PaintLayerId = "keep", Surface = new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, Color = "#FF0000" }, TestContext.CancellationToken);
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = "keep", Visible = false, Locked = true, Opacity = 0.4 } }, TestContext.CancellationToken);
        var before = service.Read(id);
        var (path, rgba) = await Fixture(sandbox.Root, size);
        var input = await SkinService.PrepareImportAsync(path, cancellationToken: TestContext.CancellationToken);
        Array.Fill(input.DecodeLayer().Pixels, 0xFFFFFFFFu);
        File.Delete(path);

        await session.ImportLayerAsync(input, "Shawl", TestContext.CancellationToken);
        var imported = service.Read(id);
        var layer = imported.Layers[^1];
        Assert.AreEqual(before.Revision + 1, session.Revision);
        Assert.AreEqual(model, imported.Model);
        Assert.AreEqual(size, imported.Size);
        Assert.AreEqual(layer.Id, session.ActiveLayerId);
        Assert.AreEqual("Shawl", layer.Name);
        Assert.AreEqual("import-layer", imported.Transaction.Kind);
        CollectionAssert.AreEqual(before.Pixels, imported.Pixels);
        Assert.AreEqual("keep", imported.Layers[0].Id);
        Assert.IsFalse(imported.Layers[0].Visible);
        Assert.IsTrue(imported.Layers[0].Locked);
        Assert.AreEqual(0.4, imported.Layers[0].Opacity);
        CollectionAssert.AreEqual(before.Layers[0].Pixels, imported.Layers[0].Pixels);
        CollectionAssert.AreEqual(rgba, Rgba(session.LayerPixels(layer.Id)));

        await session.RestoreAsync(cancellationToken: TestContext.CancellationToken);
        Assert.HasCount(2, session.Layers);
        CollectionAssert.AreEqual(before.Pixels, service.Read(id).Pixels);
        await session.RestoreAsync(redo: true, TestContext.CancellationToken);
        Assert.HasCount(3, session.Layers);
        CollectionAssert.AreEqual(rgba, Rgba(session.LayerPixels(layer.Id)));
    }

    // Author review: export must address raw layer content, not the flattened image or visibility/opacity.
    // An independent decoder and a whole project byte snapshot distinguish those failures from a no-op.
    /// <summary>A hidden locked layer exports exact RGBA, including transparent RGB, without changing its project.</summary>
    [TestMethod]
    [DataRow(64)]
    [DataRow(128)]
    public async Task ExportHiddenLayer_WritesOnlyRawPixelsAndPreservesProject(int size)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, color: "#FF0000", size: size, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var (input, rgba) = await Fixture(sandbox.Root, size);
        await session.ImportLayerAsync(await SkinService.PrepareImportAsync(input, cancellationToken: TestContext.CancellationToken), "Shawl", TestContext.CancellationToken);
        var layerId = session.ActiveLayerId!;
        await session.ApplyAsync(new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Set, Id = layerId, Visible = false, Locked = true, Opacity = 0.25 } }, TestContext.CancellationToken);
        var project = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var before = await File.ReadAllBytesAsync(project, TestContext.CancellationToken);
        var output = Path.Combine(sandbox.Root, "shared layer.png");

        await session.ExportLayerAsync(layerId, output, cancellationToken: TestContext.CancellationToken);
        var decoded = ImageResult.FromMemory(await File.ReadAllBytesAsync(output, TestContext.CancellationToken), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual(size, decoded.Width);
        Assert.AreEqual(size, decoded.Height);
        CollectionAssert.AreEqual(rgba, decoded.Data);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(project, TestContext.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => session.ExportLayerAsync(layerId, output, cancellationToken: TestContext.CancellationToken));
        CollectionAssert.AreEqual(rgba, ImageResult.FromMemory(await File.ReadAllBytesAsync(output, TestContext.CancellationToken), StbImageSharp.ColorComponents.RedGreenBlueAlpha).Data);
    }

    // Author review: additive import introduces an explicit same-size contract and a new mutation path.
    // Rejections must preserve both the open session and durable bytes, including an external edit.
    /// <summary>Rejected input or a stale/canceled transaction cannot partially create or select a layer.</summary>
    [TestMethod]
    [DataRow("size")]
    [DataRow("legacy")]
    [DataRow("conflict")]
    [DataRow("canceled")]
    [DataRow("damaged")]
    public async Task RejectedLayerImport_PreservesSessionAndPersistedContent(string scenario)
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var session = service.OpenEditor(id);
        var (path, _) = await Fixture(sandbox.Root, scenario == "size" ? 128 : 64, scenario == "legacy" ? 32 : null);
        if (scenario == "damaged") await File.WriteAllBytesAsync(path, [0, 1, 2], TestContext.CancellationToken);
        if (scenario == "conflict")
            await service.EditAsync(id, 0, [new() { Tool = SkinTool.Layer, LayerChange = new() { Operation = SkinLayerOperation.Add, Id = "external", Name = "External" } }], TestContext.CancellationToken);
        var project = Path.Combine(sandbox.Root, "skins", id + ".rmcs");
        var before = await File.ReadAllBytesAsync(project, TestContext.CancellationToken);
        var rendered = session.Render().Pixels.ToArray();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        async Task Import() => await session.ImportLayerAsync(await SkinService.PrepareImportAsync(path, cancellationToken: TestContext.CancellationToken), "Shawl",
            scenario == "canceled" ? canceled.Token : TestContext.CancellationToken);
        if (scenario == "conflict") await Assert.ThrowsAsync<InvalidOperationException>(Import);
        else if (scenario == "canceled") await Assert.ThrowsAsync<OperationCanceledException>(Import);
        else await Assert.ThrowsAsync<InvalidDataException>(Import);
        Assert.AreEqual(0L, session.Revision);
        Assert.AreEqual("background", session.ActiveLayerId);
        Assert.HasCount(1, session.Layers);
        CollectionAssert.AreEqual(rendered, session.Render().Pixels.ToArray());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(project, TestContext.CancellationToken));
    }

    // Author review: lower-level cases do not exercise typed MCP options or actual command processes.
    // Importing via MCP and sharing via CLI detects a dropped asLayer/name/paintLayerId mapping.
    /// <summary>Public adapters share one additive import/export implementation while retaining the model and full-skin export.</summary>
    [TestMethod]
    public async Task McpImport_CliExport_SharesTransparentLayerWithoutReplacingSkin()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var id = await service.CreateAsync(SkinModel.Slim, color: "#FF0000", cancellationToken: TestContext.CancellationToken);
        var (input, rgba) = await Fixture(sandbox.Root, 64);
        await using var mcp = await McpSession.ConnectAsync(sandbox, "skins");
        var imported = await mcp.CallAsync("skin_import", new JsonObject { ["documentId"] = id, ["expectedRevision"] = 0, ["input"] = input, ["asLayer"] = true, ["name"] = "Shared shawl" });
        Assert.AreEqual("succeeded", imported["status"]?.GetValue<string>(), imported.ToJsonString());
        var document = service.Read(id);
        Assert.AreEqual(SkinModel.Slim, document.Model);
        Assert.HasCount(1, document.Layers);
        Assert.AreEqual("Shared shawl", document.Layers[0].Name);
        var output = Path.Combine(sandbox.Root, "shawl shared.png");
        (await sandbox.RunAsync("skin", "export", id, "--paint-layer", document.Layers[0].Id, "--output", output)).ReadEnvelope(0);
        CollectionAssert.AreEqual(rgba, ImageResult.FromMemory(await File.ReadAllBytesAsync(output, TestContext.CancellationToken), StbImageSharp.ColorComponents.RedGreenBlueAlpha).Data);
        (await sandbox.RunAsync("skin", "undo", id, "--revision", "1")).ReadEnvelope(0);
        Assert.IsEmpty(service.Read(id).Layers);
        Assert.AreEqual(0xFF0000FFu, service.Read(id).Pixels[(8 * 64) + 8]);
        var unnamed = await mcp.CallAsync("skin_import", new JsonObject { ["documentId"] = id, ["expectedRevision"] = 2, ["input"] = input, ["asLayer"] = true });
        Assert.AreEqual("succeeded", unnamed["status"]?.GetValue<string>(), unnamed.ToJsonString());
        Assert.AreEqual("shawl source", service.Read(id).Layers[0].Name);
        Assert.AreEqual(SkinModel.Slim, service.Read(id).Model);
    }

    private async Task<(string Path, byte[] Rgba)> Fixture(string root, int size, int? height = null)
    {
        var rgba = new byte[size * (height ?? size) * 4];
        new byte[] { 0x12, 0x34, 0x56, 0 }.CopyTo(rgba, 0);
        new byte[] { 0x33, 0x55, 0x77, 128 }.CopyTo(rgba, ((8 * size / 64 * size) + (8 * size / 64)) * 4);
        new byte[] { 0xAA, 0xBB, 0xCC, 255 }.CopyTo(rgba, ((20 * size) + 20) * 4);
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(rgba, size, height ?? size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        var path = Path.Combine(root, "shawl source.png");
        await File.WriteAllBytesAsync(path, stream.ToArray(), TestContext.CancellationToken);
        return (path, rgba);
    }

    private static byte[] Rgba(uint[] pixels) => [.. pixels.SelectMany(pixel => new[] { (byte)(pixel >> 24), (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel })];

    /// <summary>Gets or sets cooperative cancellation for owned files and operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}