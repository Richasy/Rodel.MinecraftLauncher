using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Verifies stdio transport, typed tool mapping, and host boundaries against actual CLI/Core effects.
/// CLI-only validation and configuration lifecycle cases belong to their own suites.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class McpContractTests
{
    /// <summary>
    /// Design review: a real compact connection must omit domain schemas while retaining typed
    /// validation and actual persistence. One write/read plus an unknown target distinguishes a
    /// working discovery adapter from a cosmetic tools/list filter or unrestricted name fallback.
    /// </summary>
    [TestMethod]
    public async Task DiscoveryProfile_LoadsOneSchema_AndExecutesTypedCoreWithoutPublishingDomainTools()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli, "discovery");
        CollectionAssert.AreEquivalent(expected, await mcp.ToolNamesAsync());
        var schema = await mcp.CallAsync("capabilities", new() { ["toolName"] = "config_set" });
        Assert.IsNotNull(schema["data"]?["inputSchema"]?["properties"]?["value"]);
        var written = await mcp.CallAsync("tool_call", new() { ["name"] = "config_set", ["arguments"] = new JsonObject { ["key"] = "discovered", ["value"] = false } });
        Assert.AreEqual("succeeded", written["status"]?.ToString());
        var read = (await cli.RunAsync("config", "get", "discovered")).ReadEnvelope(0);
        Assert.IsFalse(read["data"]?["value"]?.GetValue<bool>());
        var missing = await mcp.CallAsync("tool_call", new() { ["name"] = "config_set", ["arguments"] = new JsonObject { ["key"] = "discovered" } });
        Assert.AreEqual("failed", missing["status"]?.ToString());
        var unknown = await mcp.CallAsync("tool_call", new() { ["name"] = "execute", ["arguments"] = new JsonObject { ["arguments"] = new JsonArray("config", "set", "discovered", "changed") } });
        Assert.AreEqual("UNKNOWN_TOOL", unknown["error"]?["code"]?.ToString());
        Assert.IsFalse((await cli.RunAsync("config", "get", "discovered")).ReadEnvelope(0)["data"]?["value"]?.GetValue<bool>());
    }

    /// <summary>
    /// Author review: direct Core dispatch must retain the external CLI's independent task host.
    /// A real source-directory lease prevents copying until after the MCP process exits; success
    /// after release therefore proves lifecycle ownership rather than merely fast completion.
    /// </summary>
    [TestMethod]
    public async Task DiscoveryTask_AfterMcpExit_CompletesInDetachedCliHost()
    {
        using var cli = new CliSandbox();
        var source = (await cli.RunAsync("instance", "create", "Source", "--version", "26.3")).ReadEnvelope(0)["data"]!;
        var game = source["gameDirectory"]!.ToString();
        await File.WriteAllTextAsync(Path.Combine(game, "marker.txt"), "preserved", TestContext.CancellationToken);
        using var barrier = await ResourceLease.AcquireAsync(game, TestContext.CancellationToken);
        string jobId;
        await using (var mcp = await McpSession.ConnectAsync(cli, "discovery"))
        {
            var submitted = await mcp.CallAsync("tool_call", new() { ["name"] = "instance_copy", ["arguments"] = new JsonObject { ["instanceId"] = source["id"]!.ToString(), ["name"] = "Copy" } });
            Assert.AreEqual("succeeded", submitted["status"]?.ToString(), submitted.ToJsonString());
            jobId = submitted["data"]!["id"]!.ToString();
        }

        var state = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cli.Root, "jobs", jobId + ".json"), TestContext.CancellationToken))!;
        Assert.IsTrue(state["status"]?.ToString() is "queued" or "running", state.ToJsonString());
        var identity = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cli.Root, "jobs", jobId + ".json.owner"), TestContext.CancellationToken))!;
        using var owner = Process.GetProcessById(identity["processId"]!.GetValue<int>());
        Assert.AreEqual(DateTimeOffset.Parse(identity["processStartUtc"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, owner.StartTime.ToUniversalTime());
        await using var job = new JobHost(cli, jobId, owner);
        barrier.Dispose();
        var terminal = await job.WaitForTerminalAsync();
        Assert.AreEqual("succeeded", terminal["status"]?.ToString(), terminal.ToJsonString());
        var copy = terminal["result"]!["gameDirectory"]!.ToString();
        Assert.AreEqual("preserved", await File.ReadAllTextAsync(Path.Combine(copy, "marker.txt"), TestContext.CancellationToken));
    }

    /// <summary>
    /// Values must cross the typed tool and CLI token boundaries without coercion, loss, or double serialization.
    /// Rows distinguish explicit null, empty text, false, numeric precision, and nested JSON.
    /// </summary>
    [TestMethod]
    [DataRow("null")]
    [DataRow("\"\"")]
    [DataRow("false")]
    [DataRow("9007199254740993")]
    [DataRow("{\"nested\":[null,false,\"--background\",9007199254740993]}")]
    public async Task ConfigSet_TypedJsonValue_PersistsExactValue(string json)
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "payload", "old-value")).ReadEnvelope(0);
        await using var mcp = await McpSession.ConnectAsync(cli);
        var expected = JsonNode.Parse(json);

        var result = await mcp.CallAsync("config_set", new JsonObject { ["key"] = "payload", ["value"] = expected?.DeepClone() });
        var read = (await cli.RunAsync("config", "get", "payload")).ReadEnvelope(0);

        Assert.AreEqual("succeeded", result["status"]?.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(expected, read["data"]?["value"]));
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken))!.AsObject();
        Assert.IsTrue(persisted.ContainsKey("payload"), "Explicit null must not be treated as a missing property.");
        Assert.IsTrue(JsonNode.DeepEquals(expected, persisted["payload"]));
    }

    /// <summary>
    /// A missing required value must be rejected, not interpreted as explicit null and written over existing data.
    /// </summary>
    [TestMethod]
    public async Task ConfigSet_MissingValue_RejectsWithoutOverwritingExistingData()
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "payload", "keep-me")).ReadEnvelope(0);
        var file = Path.Combine(cli.Root, "settings.json");
        var original = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);

        var failure = await mcp.CallAsync("config_set", new JsonObject { ["key"] = "payload" });

        Assert.AreEqual("failed", failure["status"]?.GetValue<string>());
        Assert.AreEqual("INVALID_ARGUMENT", failure["error"]?["code"]?.GetValue<string>());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>
    /// execute may accept a CLI data-dir token, but the already configured MCP host must remain the write owner.
    /// </summary>
    [TestMethod]
    public async Task Execute_DataDirectoryOverride_WritesOnlyToHostRoot()
    {
        using var cli = new CliSandbox();
        using var outside = new CliSandbox();
        (await outside.RunAsync("config", "set", "sentinel", "untouched")).ReadEnvelope(0);
        var outsideFile = Path.Combine(outside.Root, "settings.json");
        var original = await File.ReadAllBytesAsync(outsideFile, TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);

        var result = await mcp.CallAsync("execute", new JsonObject
        {
            ["arguments"] = new JsonArray("--data-dir", outside.Root, "config", "set", "--", "payload", "from-execute")
        });
        var read = (await cli.RunAsync("config", "get", "payload")).ReadEnvelope(0);

        Assert.AreEqual("succeeded", result["status"]?.GetValue<string>());
        Assert.AreEqual("from-execute", read["data"]?["value"]?.GetValue<string>());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(outsideFile, TestContext.CancellationToken));
    }

    /// <summary>
    /// Named tools must retain the CLI confirmation guard before queueing a destructive background request.
    /// </summary>
    [TestMethod]
    public async Task ApplyReview_WithoutConfirmation_RejectsBeforeQueueing()
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "sentinel", "untouched")).ReadEnvelope(0);
        await using var mcp = await McpSession.ConnectAsync(cli);
        var original = cli.SnapshotFiles();

        var failure = await mcp.CallAsync("content_apply_review", new JsonObject
        {
            ["instanceId"] = "unused-instance",
            ["reviewId"] = "unused-review",
            ["background"] = true
        });

        Assert.AreEqual("failed", failure["status"]?.GetValue<string>());
        Assert.AreEqual("INVALID_ARGUMENT", failure["error"]?["code"]?.GetValue<string>());
        var after = cli.SnapshotFiles();
        CollectionAssert.AreEquivalent(original.Keys.ToArray(), after.Keys.ToArray());
        foreach (var path in original.Keys)
            Assert.AreEqual(original[path], after[path], $"Unconfirmed tool changed {path}.");
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["capabilities", "tool_call", "execute", "result_read", "result_image_read"];
}