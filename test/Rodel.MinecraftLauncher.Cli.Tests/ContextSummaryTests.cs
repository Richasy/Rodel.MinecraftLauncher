using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Verifies partial-result isolation and instance-scoped aggregation across real command handlers.
/// Individual configuration and job lifecycle behavior belongs to their dedicated suites.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ContextSummaryTests
{
    /// <summary>
    /// Malformed instance metadata must remain named section failures while independent activity sections survive.
    /// </summary>
    [TestMethod]
    public async Task InstanceContext_MetadataUnreadable_PreservesActivitySectionsAndOriginalFile()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var metadataFile = Path.Combine(cli.Root, "instances", fixture.InstanceId, "instance.json");
        const string malformed = "{\"incomplete\":";
        await File.WriteAllTextAsync(metadataFile, malformed, TestContext.CancellationToken);
        var original = await File.ReadAllBytesAsync(metadataFile, TestContext.CancellationToken);

        var response = (await cli.RunAsync("context", "--instance", fixture.InstanceId)).ReadEnvelope(0);
        var context = response["data"]!;

        // Design review: reuse the corrupt fixture across both entry points to catch a Core-tool
        // extraction losing section failure envelopes, without duplicating storage recovery cases.
        await using var mcp = await McpSession.ConnectAsync(cli);
        var tool = await mcp.CallAsync("context", new JsonObject { ["instanceId"] = fixture.InstanceId });

        Assert.AreEqual("succeeded", response["status"]?.GetValue<string>());
        foreach (var observed in new[] { context, tool["data"]! })
        {
            Assert.AreEqual("failed", observed["instance"]?["status"]?.GetValue<string>());
            Assert.AreEqual("failed", observed["settings"]?["status"]?.GetValue<string>());
            Assert.IsNotNull(observed["settings"]?["error"]?["code"]);
            Assert.AreEqual("failed", observed["mods"]?["status"]?.GetValue<string>());
            Assert.IsNotNull(observed["jobs"]?["items"]);
            Assert.IsNotNull(observed["sessions"]?["items"]);
        }

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(metadataFile, TestContext.CancellationToken));
    }

    /// <summary>
    /// Instance summaries must exclude other instances' jobs and retain the documented unfiltered continuation origin.
    /// </summary>
    [TestMethod]
    public async Task InstanceContext_MixedJobs_FiltersSummaryWithoutMisleadingContinuation()
    {
        using var cli = new CliSandbox();
        var first = (await cli.RunAsync("instance", "create", "first", "--version", "synthetic-version")).ReadEnvelope(0)["data"]!["id"]!.GetValue<string>();
        var other = (await cli.RunAsync("instance", "create", "other", "--version", "synthetic-version")).ReadEnvelope(0)["data"]!["id"]!.GetValue<string>();
        await using var firstJob = await JobHost.StartAsync(cli, "config", "set", "marker", "first", "--instance", first);
        Assert.AreEqual("succeeded", (await firstJob.WaitForTerminalAsync())["status"]?.GetValue<string>());
        await using var otherJob = await JobHost.StartAsync(cli, "config", "set", "marker", "other", "--instance", other);
        Assert.AreEqual("succeeded", (await otherJob.WaitForTerminalAsync())["status"]?.GetValue<string>());
        await using var latestJob = await JobHost.StartAsync(cli, "config", "set", "marker", "latest", "--instance", first);
        Assert.AreEqual("succeeded", (await latestJob.WaitForTerminalAsync())["status"]?.GetValue<string>());

        var selected = (await cli.RunAsync("context", "--instance", first, "--limit", "1")).ReadEnvelope(0)["data"]!;
        var all = (await cli.RunAsync("context", "--limit", "1")).ReadEnvelope(0)["data"]!;

        // The same mixed activity fixture must protect Core/MCP aggregation after removing its
        // former CLI-command adapter; checking totals and continuation catches scope loss.
        await using var mcp = await McpSession.ConnectAsync(cli);
        var tool = await mcp.CallAsync("context", new JsonObject { ["instanceId"] = first, ["limit"] = 1 });

        foreach (var observed in new[] { selected, tool["data"]! })
        {
            Assert.AreEqual("latest", observed["settings"]?["values"]?["marker"]?.GetValue<string>());
            Assert.AreEqual(2, observed["jobs"]?["total"]?.GetValue<int>());
            Assert.HasCount(1, observed["jobs"]!["items"]!.AsArray());
            Assert.AreEqual(latestJob.Id, observed["jobs"]!["items"]![0]?["id"]?.GetValue<string>());
            Assert.AreEqual("job_list", observed["jobs"]?["continuation"]?["tool"]?.GetValue<string>());
            Assert.AreEqual(0, observed["jobs"]?["continuation"]?["arguments"]?["offset"]?.GetValue<int>());
        }

        Assert.AreEqual(3, all["jobs"]?["total"]?.GetValue<int>());
        Assert.AreEqual(2, all["instances"]?["total"]?.GetValue<int>());
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}