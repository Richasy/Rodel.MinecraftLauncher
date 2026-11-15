// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Crosses actual CLI and stdio boundaries for the new editor contract; Core rejection internals have separate focused coverage.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class SkinEditorMcpTests
{
    /// <summary>A bound AI must not gain direct commit/file paths or another document; only the host can accept its previewed candidate.</summary>
    [TestMethod]
    public async Task BoundSkinMcp_OnlyStagesOwnCandidates_AndHostAcceptanceIsObservable()
    {
        using var cli = new CliSandbox();
        await using (var catalog = await McpSession.ConnectAsync(cli, "skins"))
        {
            var schema = await catalog.ToolSchemaAsync("skin_create");
            CollectionAssert.AreEqual(ExpectedSizes, schema.GetProperty("properties").GetProperty("size").GetProperty("enum").EnumerateArray().Select(value => value.GetInt32()).ToArray());
            var projects = await catalog.CallAsync("skin_list", []);
            Assert.AreEqual("succeeded", projects["status"]!.GetValue<string>(), projects.ToJsonString());
            Assert.IsEmpty(projects["data"]!.AsArray());
            var invalid = await catalog.CallAsync("skin_create", new JsonObject { ["size"] = 96 });
            Assert.AreEqual("failed", invalid["status"]!.GetValue<string>());
        }

        var created = (await cli.RunAsync("skin", "create", "--template", "alex", "--model", "Slim", "--size", "128", "--name", "Alex project")).ReadEnvelope(0);
        var id = created["data"]!["documentId"]!.GetValue<string>();
        Assert.AreEqual(128, created["data"]!["width"]!.GetValue<int>());
        var other = (await cli.RunAsync("skin", "create")).ReadEnvelope(0)["data"]!["documentId"]!.GetValue<string>();
        var scope = (await cli.RunAsync("skin", "scope", "create", id, "--request", "{}")).ReadEnvelope(0)["data"]!["scopeId"]!.GetValue<string>();
        await using var mcp = await McpSession.ConnectAsync(cli, "skins", scope);
        var templates = await mcp.CallAsync("skin_templates", []);
        Assert.AreEqual("succeeded", templates["status"]!.GetValue<string>(), templates.ToJsonString());
        Assert.ContainsSingle(item => item?["templateId"]?.GetValue<string>() == "cape" && item?["suggestedModel"]?.GetValue<string>() == "Cape", templates["data"]!.AsArray());
        var names = await mcp.ToolNamesAsync();
        Assert.IsTrue(names.Contains("skin_propose"));
        Assert.IsFalse(names.Contains("skin_edit"));
        Assert.IsFalse(names.Contains("skin_proposal_accept"));
        Assert.IsFalse(names.Contains("skin_export"));
        Assert.IsFalse(names.Contains("skin_outfit"));
        var escaped = await mcp.CallAsync("skin_inspect", new JsonObject { ["documentId"] = other });
        Assert.AreEqual("failed", escaped["status"]!.GetValue<string>());
        await Assert.ThrowsExactlyAsync<ModelContextProtocol.McpProtocolException>(() => mcp.CallAsync("skin_edit",
            new JsonObject { ["documentId"] = id, ["expectedRevision"] = 0, ["edits"] = new JsonArray() }));
        var proposed = await mcp.CallAsync("skin_propose", new JsonObject
        {
            ["scopeId"] = scope,
            ["expectedRevision"] = 0,
            ["edits"] = JsonNode.Parse("""[{"tool":"Pencil","surface":{"part":"Head","layer":"Base","face":"Front"},"x":1,"y":1,"color":"#FF0000"}]""")
        });
        Assert.AreEqual("succeeded", proposed["status"]!.GetValue<string>(), proposed.ToJsonString());
        var proposalId = proposed["data"]!["proposalId"]!.GetValue<string>();
        var skins = new SkinService(new LauncherPaths(cli.Root));
        Assert.AreEqual(0L, skins.Read(id).Revision);
        var image = await mcp.PreviewAsync(new JsonObject { ["proposalId"] = proposalId }, "skin_proposal_preview");
        Assert.IsGreaterThan(100, image.Length);
        Assert.AreEqual(0L, skins.Read(id).Revision);
        var accepted = (await cli.RunAsync("skin", "proposal", "accept", proposalId, "--revision", "0")).ReadEnvelope(0);
        Assert.AreEqual(1L, accepted["data"]!["revision"]!.GetValue<long>());
        var observed = await mcp.CallAsync("skin_proposal_show", new JsonObject { ["proposalId"] = proposalId });
        Assert.AreEqual("accepted", observed["data"]!["state"]!.GetValue<string>());
        Assert.AreEqual(1L, observed["data"]!["receipt"]!["revision"]!.GetValue<long>());
        Assert.AreEqual("#FF0000FF", SkinEditor.Sample(skins.Read(id), new() { Part = SkinPart.Head, Layer = SkinLayer.Base, Face = SkinFace.Front }, 1, 1));
        var output = Path.Combine(cli.Root, "hd.png");
        (await cli.RunAsync("skin", "export", id, "--output", output)).ReadEnvelope(0);
        Assert.AreEqual(128, SkinPng.Decode(await File.ReadAllBytesAsync(output, TestContext.CancellationToken), SkinModel.Slim).Size);
    }

    /// <summary>CLI import and direct history jumps must restore model/size, while clone/name/list remain durable project operations.</summary>
    [TestMethod]
    public async Task CliProjectLifecycle_ImportAndHistoryRestoreCompleteConfiguration()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("skin", "create", "--model", "Slim", "--template", "alex", "--size", "128")).ReadEnvelope(0);
        var id = created["data"]!["documentId"]!.GetValue<string>();
        var input = Path.Combine(cli.Root, "classic.png");
        await File.WriteAllBytesAsync(input, SkinPng.Encode(SkinTemplates.Create("classic", SkinModel.Classic)), TestContext.CancellationToken);
        var imported = (await cli.RunAsync("skin", "import", id, "--input", input, "--model", "Classic", "--revision", "0")).ReadEnvelope(0);
        Assert.AreEqual(64, imported["data"]!["width"]!.GetValue<int>());
        var history = (await cli.RunAsync("skin", "history", id)).ReadEnvelope(0);
        Assert.AreEqual("import", history["data"]!["entries"]![1]!["transaction"]!["kind"]!.GetValue<string>());
        var restored = (await cli.RunAsync("skin", "restore", id, "--index", "0", "--revision", "1")).ReadEnvelope(0);
        Assert.AreEqual("Slim", restored["data"]!["model"]!.GetValue<string>());
        Assert.AreEqual(128, restored["data"]!["width"]!.GetValue<int>());
        Assert.IsTrue(restored["data"]!["canRedo"]!.GetValue<bool>());
        var clone = (await cli.RunAsync("skin", "clone", id, "--name", "Working copy")).ReadEnvelope(0);
        var cloneId = clone["data"]!["documentId"]!.GetValue<string>();
        Assert.AreNotEqual(id, cloneId);
        Assert.IsFalse(clone["data"]!["canUndo"]!.GetValue<bool>());
        (await cli.RunAsync("skin", "rename", cloneId, "--name", "Renamed")).ReadEnvelope(0);
        var listed = (await cli.RunAsync("skin", "list")).ReadEnvelope(0)["data"]!.AsArray();
        Assert.ContainsSingle(item => item?["documentId"]?.GetValue<string>() == cloneId && item?["name"]?.GetValue<string>() == "Renamed", listed);
        var templates = (await cli.RunAsync("skin", "templates")).ReadEnvelope(0)["data"]!.AsArray();
        Assert.ContainsSingle(item => item?["templateId"]?.GetValue<string>() == "alex" && item?["suggestedModel"]?.GetValue<string>() == "Slim", templates);
    }

    /// <summary>Gets or sets cancellation for owned files.</summary>
    public TestContext TestContext { get; set; }

    private static readonly int[] ExpectedSizes = [64, 128];
}