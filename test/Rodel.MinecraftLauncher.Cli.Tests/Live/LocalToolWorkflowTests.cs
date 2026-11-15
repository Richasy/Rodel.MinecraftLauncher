using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Real local-model tool decisions across the SDK translator and actual launcher MCP, not another generation-only check.</summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("LiveLocalTools")]
[DoNotParallelize]
public sealed class LocalToolWorkflowTests
{
    /// <summary>
    /// Existing GGUF weights must choose a real mutation tool, consume its result, read back the independent value and continue read-only.
    /// Persisted settings and ordered successful tool observations distinguish SDK/native tool-call interoperability from a plausible answer.
    /// The saved context override stays absent: a test-only load budget must not hide an unsafe product default.
    /// </summary>
    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task LocalModel_MutatesThroughActualMcp_ReadsBack_AndContinuesReadOnly()
    {
        var model = LiveOnlineSettings.LocalModel();
        TestContext.WriteLine($"Actual Embedded tool model: {model}; no model endpoint, proxy or substituted engine.");
        await using var session = await AssistantCliSession.StartAsync();
        var live = new LiveCli(session);
        var binding = await live.ConfigureLocalAsync(model, TestContext.CancellationToken);
        var configuration = await live.RunAsync(["ai", "service", "list"], TestContext.CancellationToken);
        Assert.IsNull(configuration["services"]![0]!["models"]![0]!["contextLength"],
            "This case must exercise the real product default, not inject a test-only context workaround.");
        await live.RunAsync(["config", "set", "unrelatedLocalMetadata", """{"preserve":"original"}"""], TestContext.CancellationToken);
        var conversation = await live.CreateAsync(binding, TestContext.CancellationToken);
        var marker = "LOCAL-" + Guid.NewGuid().ToString("N");
        var receipt = await live.SendAsync(conversation, binding,
            $"This is an isolated launcher test. Use config_set or config_patch to set the GLOBAL setting liveLocalToolEvidence to the exact string {marker}. Preserve all other settings. After that successful write, call config_get with key liveLocalToolEvidence and report the actual returned value. Do not use execute, App, file, network or third-party tools. Do not merely describe the calls.",
            Guid.NewGuid().ToString("N"), true, true, TestContext.CancellationToken, 4096, 8, 300);
        var turn = receipt["id"]!.ToString();
        var completed = await FinishAsync(live, conversation, turn);
        Assert.AreEqual("completed", completed["status"]!.ToString());
        Assert.AreEqual(16384, completed["service"]!["contextLength"]!.GetValue<int>());
        var events = await live.EventsAsync(conversation, turn, TestContext.CancellationToken);
        var write = events.FirstOrDefault(item => item!["kind"]!.ToString() == "tool_finished"
            && item["data"]!["name"]!.ToString() is "config_set" or "config_patch"
            && item["data"]!["result"]!["status"]!.ToString() == "succeeded");
        Assert.IsNotNull(write, "The real local model did not successfully dispatch an actual launcher mutation.");
        Assert.Contains(item => item!["kind"]!.ToString() == "tool_finished"
            && item["sequence"]!.GetValue<long>() > write["sequence"]!.GetValue<long>()
            && item["data"]!["name"]!.ToString() == "config_get"
            && item["data"]!["result"]!["status"]!.ToString() == "succeeded"
            && item["data"]!["result"]!["data"]?["value"]?.ToString() == marker, events);
        var file = Path.Combine(live.Root, "settings.json");
        var bytes = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var persisted = JsonNode.Parse(bytes)!;
        Assert.AreEqual(marker, persisted["liveLocalToolEvidence"]!.ToString());
        Assert.AreEqual("original", persisted["unrelatedLocalMetadata"]!["preserve"]!.ToString());
        Assert.Contains(marker, completed["answer"]!.ToString());
        var following = await live.SendAsync(conversation, binding,
            "Use config_get with key liveLocalToolEvidence to report its current actual value. This turn is read-only; do not modify any setting.",
            Guid.NewGuid().ToString("N"), true, false, TestContext.CancellationToken, 4096, 4, 300);
        var continued = await FinishAsync(live, conversation, following["id"]!.ToString());
        Assert.AreEqual("completed", continued["status"]!.ToString());
        var later = await live.EventsAsync(conversation, following["id"]!.ToString(), TestContext.CancellationToken);
        Assert.Contains(item => item!["kind"]!.ToString() == "tool_finished"
            && item["data"]!["name"]!.ToString() == "config_get"
            && item["data"]!["result"]!["data"]?["value"]?.ToString() == marker, later);
        Assert.DoesNotContain(item => item!["kind"]!.ToString() == "tool_started"
            && item["data"]!["name"]!.ToString() is "config_set" or "config_patch" or "execute", later);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        var unloaded = await live.RunAsync(["ai", "local", "unload"], TestContext.CancellationToken);
        Assert.IsFalse(unloaded["initialized"]!.GetValue<bool>());
        Assert.AreEqual(1L, unloaded["engineInstancesCreated"]!.GetValue<long>());
    }

    private async Task<JsonObject> FinishAsync(LiveCli live, string conversation, string turn)
    {
        try
        {
            return await live.FinishAsync(conversation, turn, TestContext.CancellationToken, timeoutSeconds: 330);
        }
        catch (AssertFailedException)
        {
            TestContext.WriteLine("Local turn failure: " + (await live.RunAsync(["ai", "chat", "turn", "show", conversation, turn],
                TestContext.CancellationToken)).ToJsonString());
            TestContext.WriteLine("Local turn events: " + (await live.EventsAsync(conversation, turn, TestContext.CancellationToken)).ToJsonString());
            throw;
        }
    }

    /// <summary>Gets or sets the deadline for explicitly selected local-model tool work and owned cleanup.</summary>
    public TestContext TestContext { get; set; }
}