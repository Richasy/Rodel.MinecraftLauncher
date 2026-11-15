using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Tests real CLI/MCP routing and identity against a fake App callback; packaged App/visible state is verified separately.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AppControlWorkflowTests
{
    /// <summary>
    /// CLI and MCP must mutate the same selected App through the same symbols, preserving target identity and exact state checks.
    /// The fake replaces only the UI callback and writes a real temporary state file; it is not App UI acceptance.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task CliAndMcp_UseSameSelectedHost_AndStaleMutationPreservesState()
    {
        using var cli = new CliSandbox();
        Directory.CreateDirectory(cli.Root);
        var file = Path.Combine(cli.Root, "fake-app-state.json");
        var theme = "System";
        var revision = 0;
        JsonObject State() => new() { ["preferences"] = new JsonObject { ["theme"] = theme }, ["fingerprint"] = "state-" + revision };
        await using var host = await AppControlHost.StartAsync(new LauncherPaths(cli.Root), async (request, ct) =>
        {
            if (request.Action == AppControlAction.State) return State();
            if (request.ExpectedFingerprint != "state-" + revision)
                throw new LauncherOperationException("APP_STATE_CHANGED", "Synthetic App observed a stale request.");
            theme = request.Preferences!.Theme!;
            revision++;
            await File.WriteAllTextAsync(file, State().ToJsonString(), ct);
            return State();
        }, ct: TestContext.CancellationToken);
        var listed = (await cli.RunAsync("app", "list")).ReadEnvelope(0)["data"]!["items"]!.AsArray();
        Assert.HasCount(1, listed);
        var id = listed[0]!["id"]!.ToString();
        var shown = (await cli.RunAsync("app", "show", id)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("System", shown["preferences"]!["theme"]!.ToString());
        (await cli.RunAsync("app", "preferences", "update", id, "--changes", """{"theme":"Dark"}""", "--expected-fingerprint", "state-0")).ReadEnvelope(0);
        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var stale = (await cli.RunAsync("app", "preferences", "update", id, "--changes", """{"theme":"Light"}""", "--expected-fingerprint", "state-0")).ReadEnvelope(1);
        Assert.AreEqual("APP_STATE_CHANGED", stale["error"]!["code"]!.ToString());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        await using var mcp = await McpSession.ConnectAsync(cli);
        var changed = await mcp.CallAsync("app_control", new JsonObject
        {
            ["appId"] = id,
            ["request"] = new JsonObject
            {
                ["action"] = "PreferencesUpdate",
                ["expectedFingerprint"] = "state-1",
                ["preferences"] = new JsonObject { ["theme"] = "Light" }
            }
        });
        Assert.AreEqual("succeeded", changed["status"]!.ToString());
        Assert.AreEqual("Light", JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!["preferences"]!["theme"]!.ToString());
        var committed = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var readThroughMutation = await mcp.CallAsync("app_control", new JsonObject
        {
            ["appId"] = id,
            ["request"] = new JsonObject { ["action"] = 0, ["expectedFingerprint"] = "state-2" }
        });
        Assert.AreEqual("INVALID_ARGUMENT", readThroughMutation["error"]!["code"]!.ToString());
        CollectionAssert.AreEqual(committed, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the deadline for actual child processes and current-user App pipes.</summary>
    public TestContext TestContext { get; set; }
}