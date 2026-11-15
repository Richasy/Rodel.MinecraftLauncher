using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects prompt-derived history metadata and future-turn context without rewriting active turn scopes.</summary>
[TestClass]
public sealed class ConversationMetadataTests
{
    /// <summary>A title request waits at HTTP admission while two actual main turns finish.
    /// Review: extends the existing metadata race boundary to detect hidden generation admission and loading delays;
    /// a concurrent manual rename must win, credential rotation must retain the auxiliary request’s own lease, and reasoning exhaustion must keep the answer.</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task CloudTitle_RunsOutsideTurnAdmission_AndPreservesRenameOrFallback(bool rename, bool exhausted)
    {
        using var sandbox = new AiSandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var titleCalls = 0;
        var mainCalls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var input = await ModelEndpoint.ReadRequestAsync(context);
            if (input.ToJsonString().Contains("conversation title", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref titleCalls);
                Assert.AreEqual("Bearer synthetic-title-original", context.Request.Headers.Authorization.ToString());
                Assert.IsTrue(input["tools"] is null || input["tools"]!.AsArray().Count == 0);
                Assert.IsTrue(input["max_completion_tokens"]!.GetValue<int>() is > 0 and <= 512);
                Assert.AreEqual(2, input["messages"]!.AsArray().Count, "Title generation must not receive tool history.");
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
                await ModelEndpoint.SseAsync(context, [exhausted
                    ? """{"choices":[{"index":0,"delta":{"reasoning_content":"推理耗尽"},"finish_reason":"length"}]}"""
                    : """{"choices":[{"index":0,"delta":{"content":"日志诊断"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16}}""", "[DONE]"]);
                return;
            }

            var mainCall = Interlocked.Increment(ref mainCalls);
            Assert.AreEqual(mainCall == 1 ? "Bearer synthetic-title-original" : "Bearer synthetic-title-replacement", context.Request.Headers.Authorization.ToString());
            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"已检查日志。"},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint.Endpoint, "synthetic-title-original");
        var services = new AiServiceStore(sandbox.Paths);
        var service = (await services.GetAsync(TestContext.CancellationToken)).Services.Single();
        var oldBlob = Directory.GetFiles(Path.Combine(sandbox.Root, "ai", "secrets"), "*.bin").Single();
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", new() { ["generateTitle"] = true }, TestContext.CancellationToken);
        var id = created["id"]!.ToString();
        var prompt = new string('诊', 23) + "🧱" + "断不能进入备用标题\n第二行";
        var receipt = await session.SendAsync(Request(id, 1) with { Prompt = prompt }, TestContext.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.CancellationToken);
            var finished = await session.FinishAsync(receipt, TestContext.CancellationToken);
            Assert.AreEqual("completed", finished["status"]!.ToString());
            Assert.AreEqual("已检查日志。", finished["answer"]!.ToString());
            await services.SaveServiceAsync(new() { Id = service.Id, ExpectedRevision = service.Revision },
                secret: "synthetic-title-replacement", ct: TestContext.CancellationToken);
            await services.CleanupCredentialsAsync(TestContext.CancellationToken);
            Assert.IsTrue(File.Exists(oldBlob), "Only the held title request should still pin the retired credential after turn completion.");
            if (rename) await session.InvokeAsync("chat.update", new() { ["conversationId"] = id, ["expectedRevision"] = 2, ["title"] = "我的标题" }, TestContext.CancellationToken);
            var next = await session.SendAsync(Request(id, rename ? 3 : 2), TestContext.CancellationToken);
            Assert.AreEqual("completed", (await session.FinishAsync(next, TestContext.CancellationToken))["status"]!.ToString(),
                "A held auxiliary title must not reserve the chat generation queue.");
            Assert.AreEqual(2, mainCalls);
            Assert.AreEqual(1, titleCalls, "A later turn must not duplicate the pending title request.");
        }
        finally { release.TrySetResult(); }

        await session.FinishMetadataAsync(TestContext.CancellationToken);
        await services.CleanupCredentialsAsync(TestContext.CancellationToken);
        Assert.IsFalse(File.Exists(oldBlob), "The auxiliary request must release its retired credential at completion.");
        var saved = sandbox.Store.GetDocument(id);
        Assert.AreEqual(rename ? "我的标题" : exhausted ? new string('诊', 23) + "🧱" : "日志诊断", saved["title"]?.ToString());
        Assert.AreEqual(rename ? "manual" : exhausted ? "prompt" : "model", saved["titleSource"]!.ToString());
        Assert.AreEqual(exhausted ? "incomplete" : "completed", saved["titleGeneration"]!["status"]!.ToString());
        if (!exhausted) Assert.AreEqual(4L, saved["titleGeneration"]!["usage"]!["outputTokens"]!.GetValue<long>());
    }

    /// <summary>Switch at an admitted live stream, then start another turn. Old and new stored bindings must differ;
    /// the persisted divider must survive reload and a stale update must not alter it.</summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ContextSwitch_DuringActiveTurn_AppliesOnlyToFutureTurns_AndPersistsDivider()
    {
        using var sandbox = new AiSandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await release.Task.WaitAsync(context.RequestAborted); }

            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"done"},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await ConfigureAsync(sandbox, endpoint.Endpoint);
        var client = new LauncherClient(sandbox.Paths);
        var first = await client.Instances.CreateAsync("first", "synthetic", TestContext.CancellationToken);
        var second = await client.Instances.CreateAsync("second", "synthetic", TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var created = await session.InvokeAsync("chat.create", new() { ["instanceId"] = first.Id }, TestContext.CancellationToken);
        var id = created["id"]!.ToString();
        var receipt = await session.SendAsync(Request(id, 1), TestContext.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.CancellationToken);
            await session.InvokeAsync("chat.update", new() { ["conversationId"] = id, ["expectedRevision"] = 2, ["instanceId"] = second.Id }, TestContext.CancellationToken);
        }
        finally { release.TrySetResult(); }

        await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual(first.Id, sandbox.Store.GetTurnDocument(id, receipt["id"]!.ToString())["instanceId"]!.ToString());
        var next = await session.SendAsync(Request(id, 3), TestContext.CancellationToken);
        await session.FinishAsync(next, TestContext.CancellationToken);
        Assert.AreEqual(second.Id, sandbox.Store.GetTurnDocument(id, next["id"]!.ToString())["instanceId"]!.ToString());
        var saved = sandbox.Store.GetDocument(id);
        Assert.AreEqual(receipt["id"]!.ToString(), saved["contextChanges"]![0]!["afterTurnId"]!.ToString());
        Assert.AreEqual(second.Id, saved["contextChanges"]![0]!["value"]!.ToString());
        await Assert.ThrowsAsync<LauncherOperationException>(() => session.InvokeAsync("chat.update", new() { ["conversationId"] = id, ["expectedRevision"] = 2, ["clearInstance"] = true }, TestContext.CancellationToken));
        Assert.AreEqual(second.Id, sandbox.Store.GetDocument(id)["instanceId"]!.ToString());
    }

    private async Task ConfigureAsync(AiSandbox sandbox, Uri endpoint, string? secret = null) => await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new()
    {
        Protocol = AiProtocols.OpenAiChat,
        BaseUrl = endpoint.AbsoluteUri,
        Models = [new() { Model = "fixture" }],
    }, secret: secret, ct: TestContext.CancellationToken);
    private static AiChatSend Request(string id, long revision) => new()
    {
        ConversationId = id,
        ExpectedRevision = revision,
        ClientKey = Guid.NewGuid().ToString("N"),
        Prompt = "请检查日志中的错误",
        UseTools = false,
    };
    /// <summary>Gets or sets the deadline for owned model and storage operations.</summary>
    public TestContext TestContext { get; set; }
}