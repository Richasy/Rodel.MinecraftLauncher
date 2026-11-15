namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Real cross-provider continuation; no fixed HTTP reply or fake local engine can establish this history projection.</summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("LiveMixed")]
[DoNotParallelize]
public sealed class MixedModelWorkflowTests
{
    /// <summary>
    /// Switching local→online-with-tools→local in one conversation must preserve earlier user context and newly observed evidence.
    /// Separate local/online tests cannot detect cross-provider native-history projection or accidental engine reload.
    /// </summary>
    [TestMethod]
    [Timeout(420000, CooperativeCancellation = true)]
    public async Task LocalOnlineLocal_ContinuesActualHistoryAndToolEvidence_WithoutReloadingEngine()
    {
        var model = LiveOnlineSettings.LocalModel();
        var online = LiveOnlineSettings.Online();
        TestContext.WriteLine($"Real mixed providers: Embedded {model} -> {online.Protocol} {online.Model} -> Embedded.");
        await using var session = await AssistantCliSession.StartAsync();
        var live = new LiveCli(session, online.CredentialEnvironment);
        var localBinding = await live.ConfigureLocalAsync(model, TestContext.CancellationToken);
        var onlineBinding = await live.ConfigureOnlineAsync(online, TestContext.CancellationToken);
        var memory = "MEMORY-" + Guid.NewGuid().ToString("N")[..12];
        var observed = "OBSERVED-" + Guid.NewGuid().ToString("N")[..12];
        await live.RunAsync(["config", "set", "liveAiObserved", observed], TestContext.CancellationToken);
        var conversation = await live.CreateAsync(localBinding, TestContext.CancellationToken);
        var first = await live.SendAsync(conversation, localBinding,
            $"Remember the exact code {memory}. Reply only with that exact code, without explanation.",
            Guid.NewGuid().ToString("N"), false, false, TestContext.CancellationToken);
        var remembered = await live.FinishAsync(conversation, first["id"]!.ToString(), TestContext.CancellationToken);
        Assert.Contains(memory, remembered["answer"]!.ToString());
        var second = await live.SendAsync(conversation, onlineBinding,
            "Use the actual config_get launcher tool with key liveAiObserved. Report its value and the exact MEMORY code supplied in the earlier user message. Do not modify anything.",
            Guid.NewGuid().ToString("N"), true, false, TestContext.CancellationToken);
        var answered = await live.FinishAsync(conversation, second["id"]!.ToString(), TestContext.CancellationToken);
        Assert.Contains(memory, answered["answer"]!.ToString());
        Assert.Contains(observed, answered["answer"]!.ToString());
        var events = await live.EventsAsync(conversation, second["id"]!.ToString(), TestContext.CancellationToken);
        Assert.Contains(item => item!["kind"]!.ToString() == "tool_finished"
            && item["data"]!["name"]!.ToString() == "config_get" && item["data"]!["result"]!["status"]!.ToString() == "succeeded"
            && item["data"]!["result"]!["data"]?["value"]?.ToString() == observed, events);
        var third = await live.SendAsync(conversation, localBinding,
            "Reply with the exact MEMORY code from the original user message and the exact OBSERVED value returned by the real launcher tool. Do not invent new codes. No explanation.",
            Guid.NewGuid().ToString("N"), false, false, TestContext.CancellationToken);
        var final = await live.FinishAsync(conversation, third["id"]!.ToString(), TestContext.CancellationToken);
        var persisted = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(live.Root, "ai", "conversations", conversation, "turns", third["id"]!.ToString() + ".json"), TestContext.CancellationToken))!;
        var history = persisted["wireHistory"]!.ToJsonString();
        Assert.Contains(memory, history, "The exact original user identifier was lost from persisted wire history.");
        Assert.Contains(observed, history, "The exact real tool result was lost from persisted wire history.");
        Assert.Contains(memory, final["answer"]!.ToString());
        Assert.Contains(observed, final["answer"]!.ToString());
        Assert.AreEqual("rodel-inference", final["service"]!["protocol"]!.ToString());
        Assert.AreEqual(1L, (await live.RunAsync(["ai", "local", "status"], TestContext.CancellationToken))["engineInstancesCreated"]!.GetValue<long>());
        Assert.IsFalse((await live.RunAsync(["ai", "local", "unload"], TestContext.CancellationToken))["initialized"]!.GetValue<bool>());
    }

    /// <summary>Gets or sets the bounded deadline for actual cross-provider generation and owned-resource cleanup.</summary>
    public TestContext TestContext { get; set; }
}