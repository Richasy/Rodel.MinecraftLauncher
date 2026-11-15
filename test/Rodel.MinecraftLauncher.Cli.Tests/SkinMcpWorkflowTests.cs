// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using StbImageSharp;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises the isolated skin MCP profile through actual CLI processes, durable drawings and exported 3D artifacts.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class SkinMcpWorkflowTests
{
    /// <summary>Skin MCP must not expose launcher operations; drawings persist across clients and are returned as real 3D images.</summary>
    [TestMethod]
    public async Task SkinProfile_DrawingExportAndPreview_UseSharedPersistentEditor()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli, "skins");
        var tools = await mcp.ToolNamesAsync();
        Assert.IsFalse(tools.Contains("execute"));
        Assert.IsFalse(tools.Contains("launch"));
        var previewSchema = await mcp.ToolSchemaAsync("skin_preview");
        Assert.IsFalse(previewSchema.GetProperty("properties").TryGetProperty("viewer", out _),
            "The preview tool must not advertise a web-client export.");
        var created = await mcp.CallAsync("skin_create", new JsonObject { ["model"] = "Slim", ["color"] = "#000000" });
        Assert.AreEqual("succeeded", created["status"]?.GetValue<string>());
        Assert.AreEqual("Slim", created["data"]!["model"]!.GetValue<string>());
        Assert.IsFalse(created["data"]!.AsObject().ContainsKey("pixels"), "Tool summaries must not expose texture or history snapshots.");
        Assert.IsFalse(created["data"]!.AsObject().ContainsKey("undo"));
        var id = created["data"]!["documentId"]!.GetValue<string>();
        var edited = await mcp.CallAsync("skin_edit", new JsonObject
        {
            ["documentId"] = id,
            ["expectedRevision"] = 0,
            ["edits"] = JsonNode.Parse("""[{"tool":"Fill","surface":{"part":"Head","layer":"Base","face":"Front"},"color":"#FF0000"}]""")
        });
        Assert.AreEqual("succeeded", edited["status"]?.GetValue<string>(), edited.ToJsonString());
        Assert.AreEqual(1L, edited["data"]!["revision"]!.GetValue<long>());
        var exported = Path.Combine(cli.Root, "result.png");
        (await cli.RunAsync("skin", "export", id, "--output", exported)).ReadEnvelope(0);
        var imported = SkinPng.Decode(await File.ReadAllBytesAsync(exported, TestContext.CancellationToken), SkinModel.Slim);
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(imported, new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, 0, 0));
        var previewPath = Path.Combine(cli.Root, "preview.png");
        var imageBytes = await mcp.PreviewAsync(new JsonObject
        {
            ["documentId"] = id,
            ["output"] = previewPath,
            ["view"] = new JsonObject { ["yaw"] = 0, ["pitch"] = 0 }
        });
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(previewPath, TestContext.CancellationToken), imageBytes);
        var image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
        // A frontal camera sees the face at the top center, not an arbitrary thumbnail or flat atlas.
        var offset = ((51 * image.Width) + (image.Width / 2)) * 4;
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, image.Data.AsSpan(offset, 4).ToArray());
        var viewerPath = Path.Combine(cli.Root, "viewer.html");
        var rejected = (await cli.RunAsync("skin", "preview", id, "--viewer", "--output", viewerPath)).ReadEnvelope(2);
        Assert.AreEqual("INVALID_ARGUMENT", rejected["error"]?["code"]?.GetValue<string>());
        Assert.IsFalse(File.Exists(viewerPath), "The removed option must fail before publishing a file.");
    }

    /// <summary>A stale second client or invalid batch must not overwrite a successful drawing, including after undo and restart.</summary>
    [TestMethod]
    public async Task StaleAndInvalidTransactions_LeaveDurableDrawingUnchanged()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("skin", "create")).ReadEnvelope(0);
        var id = created["data"]!["documentId"]!.GetValue<string>();
        const string draw = """[{"tool":"Pencil","surface":{"part":"Head","layer":"Base","face":"Front"},"color":"#123456"}]""";
        (await cli.RunAsync("skin", "edit", id, "--revision", "0", "--edits", draw)).ReadEnvelope(0);
        var path = Path.Combine(cli.Root, "skins", id + ".rmcs");
        var before = await File.ReadAllBytesAsync(path, TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli, "skins");
        var stale = await mcp.CallAsync("skin_edit", new JsonObject { ["documentId"] = id, ["expectedRevision"] = 0, ["edits"] = JsonNode.Parse(draw) });
        Assert.AreEqual("failed", stale["status"]?.GetValue<string>());
        var invalid = await mcp.CallAsync("skin_edit", new JsonObject
        {
            ["documentId"] = id,
            ["expectedRevision"] = 1,
            ["edits"] = JsonNode.Parse("""[{"tool":"Pencil","surface":{"part":"Head","layer":"Base","face":"Front"},"color":"#FFFFFF"},{"tool":"Eraser","surface":{"part":"Head","layer":"Base","face":"Front"}}]""")
        });
        Assert.AreEqual("failed", invalid["status"]?.GetValue<string>());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        (await cli.RunAsync("skin", "undo", id, "--revision", "1")).ReadEnvelope(0);
        var restored = (await cli.RunAsync("skin", "read-face", id, "--part", "Head", "--layer", "Base", "--face", "Front")).ReadEnvelope(0);
        Assert.AreEqual("#808080FF", restored["data"]!["rows"]![0]![0]!.GetValue<string>());
        Assert.AreEqual(2L, restored["data"]!["revision"]!.GetValue<long>());
    }

    /// <summary>Layers, computed masks and clipboard data must survive actual MCP/CLI serialization and remain undoable across clients.</summary>
    [TestMethod]
    public async Task LayeredDrawing_SelectionClipboardRemainsEditableAcrossProcesses()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli, "skins");
        var created = await mcp.CallAsync("skin_create", new JsonObject { ["model"] = "Slim", ["color"] = "#0000FF" });
        Assert.AreEqual("succeeded", created["status"]?.GetValue<string>());
        var id = created["data"]!["documentId"]!.GetValue<string>();
        var edited = await mcp.CallAsync("skin_edit", new JsonObject
        {
            ["documentId"] = id,
            ["expectedRevision"] = 0,
            ["edits"] = JsonNode.Parse("""
                [
                  {"tool":"Layer","layerChange":{"operation":"Add","id":"details"}},
                  {"tool":"Gradient","surface":{"part":"Head","layer":"Base","face":"Front"},"paintLayerId":"details",
                   "x":0,"y":0,"x2":7,"y2":0,"color":"#FF0000","secondaryColor":"#00FF00"}
                ]
                """)
        });
        Assert.AreEqual("succeeded", edited["status"]?.GetValue<string>(), edited.ToJsonString());
        var selected = await mcp.CallAsync("skin_select", new JsonObject
        {
            ["documentId"] = id,
            ["request"] = JsonNode.Parse("""{"surface":{"part":"Head","layer":"Base","face":"Front"},"kind":"Connected","x":0,"y":0}""")
        });
        Assert.AreEqual("succeeded", selected["status"]?.GetValue<string>());
        var captured = await mcp.CallAsync("skin_capture", new JsonObject
        {
            ["documentId"] = id,
            ["request"] = new JsonObject
            {
                ["surface"] = JsonNode.Parse("""{"part":"Head","layer":"Base","face":"Front"}"""),
                ["paintLayerId"] = "details",
                ["selection"] = selected["data"]!["selection"]!.DeepClone()
            }
        });
        Assert.AreEqual("succeeded", captured["status"]?.GetValue<string>());
        var pasted = await mcp.CallAsync("skin_edit", new JsonObject
        {
            ["documentId"] = id,
            ["expectedRevision"] = 1,
            ["edits"] = new JsonArray(new JsonObject
            {
                ["tool"] = "Paste",
                ["surface"] = JsonNode.Parse("""{"part":"Head","layer":"Base","face":"Back"}"""),
                ["paintLayerId"] = "details",
                ["clipboard"] = captured["data"]!["clipboard"]!.DeepClone()
            })
        });
        Assert.AreEqual("succeeded", pasted["status"]?.GetValue<string>(), pasted.ToJsonString());
        var projectPath = Path.Combine(cli.Root, "skins", id + ".rmcs");
        var beforeUnsafeMove = await File.ReadAllBytesAsync(projectPath, TestContext.CancellationToken);
        var unsafeMove = await mcp.CallAsync("skin_edit", new JsonObject
        {
            ["documentId"] = id,
            ["expectedRevision"] = 2,
            ["edits"] = JsonNode.Parse("""
                [{"tool":"Move","surface":{"part":"Head","layer":"Base","face":"Back"},"paintLayerId":"details",
                  "sourceSelection":{"x":0,"y":0,"width":1,"height":1},"x":1,"y":0,"opacity":0}]
                """)
        });
        Assert.AreEqual("failed", unsafeMove["status"]?.GetValue<string>());
        Assert.AreEqual("INVALID_ARGUMENT", unsafeMove["error"]?["code"]?.GetValue<string>());
        CollectionAssert.AreEqual(beforeUnsafeMove, await File.ReadAllBytesAsync(projectPath, TestContext.CancellationToken));
        var exportPath = Path.Combine(cli.Root, "layered.png");
        (await cli.RunAsync("skin", "export", id, "--output", exportPath)).ReadEnvelope(0);
        var exported = SkinPng.Decode(await File.ReadAllBytesAsync(exportPath, TestContext.CancellationToken), SkinModel.Slim);
        var back = new SkinSurface { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Back };
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(exported, back, 0, 0));
        Assert.AreEqual("#0000FFFF", SkinEditor.Sample(exported, back, 1, 0), "The copied connected mask must not include other gradient columns.");
        (await cli.RunAsync("skin", "undo", id, "--revision", "2")).ReadEnvelope(0);
        var restored = (await cli.RunAsync("skin", "read-face", id, "--part", "Head", "--layer", "Base", "--face", "Back", "--paint-layer", "details")).ReadEnvelope(0);
        Assert.AreEqual("#00000000", restored["data"]!["rows"]![0]![0]!.GetValue<string>());
    }

    /// <summary>Picking empty editable shells and hiding preview layers require explicit false values to cross the typed JSON/token boundary unchanged.</summary>
    [TestMethod]
    public async Task ModelQueriesAndPreview_KeepExplicitFalseOptions()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli, "skins");
        var created = await mcp.CallAsync("skin_create", new JsonObject { ["model"] = "Slim", ["color"] = "#0000FF" });
        Assert.AreEqual("succeeded", created["status"]?.GetValue<string>());
        var id = created["data"]!["documentId"]!.GetValue<string>();
        var ray = await mcp.CallAsync("skin_pick", new JsonObject
        {
            ["documentId"] = id,
            ["request"] = JsonNode.Parse("""{"origin":{"x":0,"y":28,"z":20},"direction":{"x":0,"y":0,"z":-1},"layer":"Outer","skipTransparent":false}""")
        });
        Assert.AreEqual("succeeded", ray["status"]?.GetValue<string>());
        Assert.AreEqual("Outer", ray["data"]!["hit"]!["surface"]!["layer"]!.GetValue<string>());
        Assert.AreEqual("Front", ray["data"]!["hit"]!["surface"]!["face"]!.GetValue<string>());
        var bytes = await mcp.PreviewAsync(new JsonObject
        {
            ["documentId"] = id,
            ["output"] = Path.Combine(cli.Root, "hidden.png"),
            ["view"] = new JsonObject { ["baseVisible"] = false, ["outerVisible"] = false, ["size"] = 64 }
        });
        var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        Assert.IsTrue(Enumerable.Range(0, image.Width * image.Height).All(i =>
            image.Data[i * 4] == 32 && image.Data[(i * 4) + 1] == 36 && image.Data[(i * 4) + 2] == 43));
    }

    /// <summary>Gets or sets the test context supplying cooperative cancellation.</summary>
    public TestContext TestContext { get; set; }
}