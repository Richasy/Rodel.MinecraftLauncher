using Rodel.Inference.Core;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Real stored weights and Core-owned Embedded inference; serialized because these cases share the machine's model/GPU resources.</summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("LiveLocal")]
[DoNotParallelize]
public sealed partial class LocalModelWorkflowTests
{
    /// <summary>
    /// Real generations must use the selected Embedded model, reuse its owner, retain a canceled stream and release it on unload.
    /// Unlike EngineStub tests this loads actual weights; cancellation begins only after durable text and active state are observed.
    /// The file variant uses the SDK-resolved source without copying it, protecting path forwarding rather than a second import implementation.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(300000, CooperativeCancellation = true)]
    public async Task LocalModel_GeneratesReusesEngine_CancelsActiveStream_AndUnloads(bool useFilePath)
    {
        var model = LiveOnlineSettings.LocalModel();
        if (!useFilePath && Path.IsPathFullyQualified(model))
            Assert.Inconclusive("The stored-reference variant requires a model reference; the direct-file variant covers the supplied path.");
        if (useFilePath && !Path.IsPathFullyQualified(model))
        {
            var configuration = await StorePaths.LoadConfigurationAsync(cancellationToken: TestContext.CancellationToken);
            var located = await ModelLocator.LocateAsync(new ModelStore(StorePaths.Resolve(configuration: configuration).Models.Path),
                model, TestContext.CancellationToken);
            Assert.IsNotNull(located, "The explicitly selected model is not present in the SDK store.");
            model = located.Value.Artifacts.ModelPath;
        }

        TestContext.WriteLine($"Real Embedded model: {model}; no daemon endpoint or downloaded test weights.");
        await using var session = await AssistantCliSession.StartAsync();
        var live = new LiveCli(session);
        var binding = await live.ConfigureLocalAsync(model, TestContext.CancellationToken);
        var first = await live.RunAsync(["ai", "request", "--model", binding, "--prompt",
            "What is 7 multiplied by 8? Answer in one short sentence.", "--temperature", "0", "--max-output-tokens", "512"], TestContext.CancellationToken);
        Assert.AreEqual("embedded", first["service"]!["hosting"]!.ToString());
        Assert.IsNull(first["service"]!["endpoint"]);
        Assert.AreEqual(model, first["model"]!.ToString());
        Assert.IsTrue(MyRegex().IsMatch(first["text"]!.ToString()), "Real local arithmetic response was not correct.");
        var resident = await live.RunAsync(["ai", "local", "status"], TestContext.CancellationToken);
        Assert.IsTrue(resident["initialized"]!.GetValue<bool>());
        Assert.AreEqual(1L, resident["engineInstancesCreated"]!.GetValue<long>());

        var second = await live.RunAsync(["ai", "request", "--model", binding, "--prompt",
            "What is 13 multiplied by 17? Answer in one short sentence.", "--temperature", "0", "--max-output-tokens", "512"], TestContext.CancellationToken);
        Assert.IsTrue(MyRegex1().IsMatch(second["text"]!.ToString()), "The second request did not actually generate the expected new answer.");
        Assert.AreEqual(1L, (await live.RunAsync(["ai", "local", "status"], TestContext.CancellationToken))["engineInstancesCreated"]!.GetValue<long>());
        var conversation = await live.CreateAsync(binding, TestContext.CancellationToken);
        var receipt = await live.SendAsync(conversation, binding,
            "Output exactly 1500 numbered lines. Each line must contain its number and the words 'local streaming cancellation evidence'. Start with line 0001 immediately. Do not summarize, explain or stop early.",
            Guid.NewGuid().ToString("N"), false, false, TestContext.CancellationToken, 8192, 1);
        var turn = receipt["id"]!.ToString();
        await live.ActiveTextAsync(conversation, turn, TestContext.CancellationToken);
        await live.RunAsync(["ai", "chat", "turn", "cancel", conversation, turn], TestContext.CancellationToken);
        var canceled = await live.FinishAsync(conversation, turn, TestContext.CancellationToken, expectedExit: 3);
        Assert.AreEqual("canceled", canceled["status"]!.ToString());
        var events = await live.EventsAsync(conversation, turn, TestContext.CancellationToken);
        Assert.Contains(item => item!["kind"]!.ToString() == "text_delta", events);
        Assert.DoesNotContain(item => item!["kind"]!.ToString() == "tool_started", events);
        var unloaded = await live.RunAsync(["ai", "local", "unload"], TestContext.CancellationToken);
        Assert.IsFalse(unloaded["initialized"]!.GetValue<bool>());
        Assert.AreEqual(1L, unloaded["engineInstancesCreated"]!.GetValue<long>());
    }

    /// <summary>Gets or sets the explicit live deadline for owned model work and cleanup.</summary>
    public TestContext TestContext { get; set; }

    [GeneratedRegex(@"\b56\b")]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"\b221\b")]
    private static partial Regex MyRegex1();
}