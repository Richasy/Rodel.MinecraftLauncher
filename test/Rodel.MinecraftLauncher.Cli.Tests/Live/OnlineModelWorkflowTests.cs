using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Real online model decisions and actual launcher MCP effects; serialized to bound live-provider traffic, not hide a fixture race.</summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("LiveOnline")]
[DoNotParallelize]
public sealed class OnlineModelWorkflowTests
{
    /// <summary>
    /// A real model must mutate only isolated settings through actual launcher tools, read back the effect and continue from durable history.
    /// An answer echo is insufficient: independent bytes and ordered successful tool results establish write/readback, while duplicate input must not replay it.
    /// </summary>
    [TestMethod]
    [Timeout(300000, CooperativeCancellation = true)]
    public async Task OnlineModel_PerformsActualMutationAndReadback_ThenContinuesWithoutReplaying()
    {
        var settings = LiveOnlineSettings.Online();
        TestContext.WriteLine($"Real online protocol: {settings.Protocol}; model: {settings.Model}. Credential uses an environment reference.");
        await using var session = await AssistantCliSession.StartAsync();
        var live = new LiveCli(session, settings.CredentialEnvironment);
        var binding = await live.ConfigureOnlineAsync(settings, TestContext.CancellationToken);
        await live.RunAsync(["config", "set", "untouchedLiveMetadata", """{"preserved":true}"""], TestContext.CancellationToken);
        var conversation = await live.CreateAsync(binding, TestContext.CancellationToken);
        var marker = "LIVE-" + Guid.NewGuid().ToString("N");
        var key = Guid.NewGuid().ToString("N");
        var prompt = $"In this isolated launcher test, use config_patch or config_set to set the GLOBAL launcher setting liveAiEvidence to the exact string {marker}. Preserve all other fields. Then use config_get with key liveAiEvidence to read it back. Report the value from that successful read. Use only these scoped launcher tools; do not use execute, App, file, network or third-party tools.";
        var receipt = await live.SendAsync(conversation, binding, prompt, key, true, true, TestContext.CancellationToken);
        var turn = receipt["id"]!.ToString();
        var completed = await live.FinishAsync(conversation, turn, TestContext.CancellationToken);
        Assert.AreEqual("completed", completed["status"]!.ToString());
        var events = await live.EventsAsync(conversation, turn, TestContext.CancellationToken);
        var write = events.FirstOrDefault(item => item!["kind"]!.ToString() == "tool_finished"
            && item["data"]!["name"]!.ToString() is "config_patch" or "config_set"
            && item["data"]!["result"]!["status"]!.ToString() == "succeeded");
        Assert.IsNotNull(write, "The actual online model did not successfully use a launcher mutation tool.");
        var read = events.FirstOrDefault(item => item!["kind"]!.ToString() == "tool_finished"
            && item["sequence"]!.GetValue<long>() > write["sequence"]!.GetValue<long>()
            && item["data"]!["name"]!.ToString() == "config_get"
            && item["data"]!["result"]!["status"]!.ToString() == "succeeded"
            && item["data"]!["result"]!["data"]?["value"]?.ToString() == marker);
        Assert.IsNotNull(read, "No successful actual config_get after the mutation returned the independently selected value.");
        var file = Path.Combine(live.Root, "settings.json");
        var stored = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))!;
        Assert.AreEqual(marker, stored["liveAiEvidence"]!.ToString());
        Assert.IsTrue(stored["untouchedLiveMetadata"]!["preserved"]!.GetValue<bool>());
        Assert.Contains(marker, completed["answer"]!.ToString());

        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var duplicate = await live.SendAsync(conversation, binding, prompt, key, true, true, TestContext.CancellationToken);
        Assert.AreEqual(turn, duplicate["id"]!.ToString());
        Assert.AreEqual(events.Count, (await live.EventsAsync(conversation, turn, TestContext.CancellationToken)).Count);
        var next = await live.SendAsync(conversation, binding,
            "Use config_get with key liveAiEvidence to report its actual current value. This turn is read-only; do not change anything.",
            Guid.NewGuid().ToString("N"), true, false, TestContext.CancellationToken);
        var answer = await live.FinishAsync(conversation, next["id"]!.ToString(), TestContext.CancellationToken);
        Assert.Contains(marker, answer["answer"]!.ToString());
        var followingEvents = await live.EventsAsync(conversation, next["id"]!.ToString(), TestContext.CancellationToken);
        Assert.Contains(item => item!["kind"]!.ToString() == "tool_finished"
            && item["data"]!["name"]!.ToString() == "config_get" && item["data"]!["result"]!["status"]!.ToString() == "succeeded"
            && item["data"]!["result"]!["data"]?["value"]?.ToString() == marker, followingEvents);
        Assert.DoesNotContain(item => item!["kind"]!.ToString() == "tool_started"
            && item["data"]!["name"]!.ToString() is "config_set" or "config_patch" or "execute", followingEvents);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>
    /// A real open provider stream must cancel only after durable partial output, preserve that evidence and release the conversation for follow-up.
    /// Local-engine cancellation and fixed HTTP replies cannot prove this remote boundary.
    /// </summary>
    [TestMethod]
    [Timeout(300000, CooperativeCancellation = true)]
    public async Task OnlineStream_CancelsAfterActualText_AndContinuesSameConversation()
    {
        var settings = LiveOnlineSettings.Online();
        await using var session = await AssistantCliSession.StartAsync();
        var live = new LiveCli(session, settings.CredentialEnvironment);
        var binding = await live.ConfigureOnlineAsync(settings, TestContext.CancellationToken);
        var conversation = await live.CreateAsync(binding, TestContext.CancellationToken);
        var receipt = await live.SendAsync(conversation, binding,
            "Write exactly 1500 numbered lines about Minecraft launcher settings. Each line must contain its number and one complete sentence. Start immediately with 0001. Do not summarize or end early. Do not call any tools.",
            Guid.NewGuid().ToString("N"), false, false, TestContext.CancellationToken, 4096, 1);
        var id = receipt["id"]!.ToString();
        await live.ActiveTextAsync(conversation, id, TestContext.CancellationToken);
        await live.RunAsync(["ai", "chat", "turn", "cancel", conversation, id], TestContext.CancellationToken);
        var canceled = await live.FinishAsync(conversation, id, TestContext.CancellationToken, expectedExit: 3);
        Assert.AreEqual("canceled", canceled["status"]!.ToString());
        var events = await live.EventsAsync(conversation, id, TestContext.CancellationToken);
        Assert.Contains(item => item!["kind"]!.ToString() == "text_delta", events);
        Assert.HasCount(1, events.Where(item => item!["kind"]!.ToString() == "model_started").ToArray());
        Assert.HasCount(1, events.Where(item => item!["kind"]!.ToString() == "turn_finished").ToArray());
        Assert.DoesNotContain(item => item!["kind"]!.ToString() == "tool_started", events);
        var following = await live.SendAsync(conversation, binding,
            "Do not continue the previous long output. Reply only RECOVERED, without tools or explanation.",
            Guid.NewGuid().ToString("N"), false, false, TestContext.CancellationToken, 256, 1);
        var recovered = await live.FinishAsync(conversation, following["id"]!.ToString(), TestContext.CancellationToken);
        Assert.AreEqual("completed", recovered["status"]!.ToString());
        Assert.Contains("RECOVERED", recovered["answer"]!.ToString());
    }

    /// <summary>Gets or sets the deadline for explicitly selected real-provider operations.</summary>
    public TestContext TestContext { get; set; }
}