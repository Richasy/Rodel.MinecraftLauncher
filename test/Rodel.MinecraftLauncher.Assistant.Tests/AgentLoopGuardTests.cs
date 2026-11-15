using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises official MEAI dispatch with real Core approval/storage boundaries, not a replacement agent loop.</summary>
[TestClass]
public sealed class AgentLoopGuardTests
{
    /// <summary>
    /// A real readonly turn-file replacement failure after the first effect must escape MEAI and prevent the second
    /// same-batch invocation and next model request. Counting dispatches detects swallowed failures that a throw-only
    /// assertion would miss; all files belong to the isolated fixture.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task FirstPersistenceFailure_StopsRemainingBatchAndNextModel()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var file = Path.Combine(sandbox.Root, "ai", "conversations", conversation["id"]!.ToString(), "turns", turn["id"] + ".json");
        var models = 0;
        var writes = 0;
        using var driver = new AiTurnDriver(Context(), (_, _, _) =>
        {
            models++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("one", "guarded_write", new Dictionary<string, object?>()),
                new FunctionCallContent("two", "guarded_write", new Dictionary<string, object?>())
            ]))
            { FinishReason = ChatFinishReason.ToolCalls });
        });
        async Task<FunctionResultContent> Apply(FunctionCallContent call, CancellationToken ct)
        {
            Assert.IsNotNull(FunctionInvokingChatClient.CurrentContext);
            writes++;
            await File.WriteAllTextAsync(Path.Combine(sandbox.Root, "effect.txt"), call.CallId, ct);
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            await sandbox.Store.SaveTurnAsync(turn, ct);
            throw new AssertFailedException("The real readonly persistence boundary must fail.");
        }

        try
        {
            Exception? failure = null;
            try { await driver.RunAsync([new(ChatRole.User, "Apply both.")], [Tool()], Apply, TestContext.CancellationToken); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = error; }

            Assert.IsNotNull(failure);
            Assert.AreEqual(1, writes);
            Assert.AreEqual(1, models);
            Assert.AreEqual("one", await File.ReadAllTextAsync(Path.Combine(sandbox.Root, "effect.txt"), TestContext.CancellationToken));
        }
        finally { File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); }
    }

    /// <summary>
    /// A persisted exact confirmation must suspend the official loop without losing its invocation; only approval
    /// resumes once, while cancellation or wrong fingerprint never applies the effect. The real Core approval store
    /// and wait/recheck run inside the guard, with only model responses and the final simple file effect controlled.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ExactApproval_WaitsInsideOfficialLoop_ThenResumesOrCancels(bool cancel)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var approvals = new AiToolApprovals(sandbox.Paths);
        var waiting = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = Guid.NewGuid().ToString("N");
        var review = new JsonObject { ["target"] = "synthetic-setting", ["revision"] = 1 };
        var models = 0;
        var writes = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var driver = new AiTurnDriver(Context(), (history, _, _) =>
        {
            models++;
            if (models == 1)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("one", "guarded_write", new Dictionary<string, object?>())]))
                { FinishReason = ChatFinishReason.ToolCalls });
            }

            Assert.AreEqual("one", history.Last().Contents.OfType<FunctionResultContent>().Single().CallId);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")) { FinishReason = ChatFinishReason.Stop });
        });
        async Task<FunctionResultContent> Apply(FunctionCallContent call, CancellationToken ct)
        {
            Assert.IsNotNull(FunctionInvokingChatClient.CurrentContext);
            await approvals.WaitAsync(turn, invocation, call.Name, [], new AiObservedToolReview(review),
                _ => Task.FromResult<AiToolReview>(new AiObservedToolReview(review.DeepClone().AsObject())), _ => Task.CompletedTask,
                (kind, _, data) => { if (kind == "confirmation_required") waiting.TrySetResult(data); return Task.CompletedTask; }, ct);
            writes++;
            await File.WriteAllTextAsync(Path.Combine(sandbox.Root, "approved.txt"), "applied", ct);
            return new(call.CallId, "succeeded");
        }

        var run = driver.RunAsync([new(ChatRole.User, "Apply with confirmation.")], [Tool()], Apply, cancellation.Token);
        var pending = await waiting.Task.WaitAsync(TestContext.CancellationToken);
        Assert.AreEqual(1, models);
        Assert.AreEqual(0, writes);
        var decision = new AiToolConfirmation
        {
            ConversationId = conversation["id"]!.ToString(),
            TurnId = turn["id"]!.ToString(),
            InvocationId = invocation,
            Fingerprint = pending["fingerprint"]!.ToString(),
            Approve = true
        };
        await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await approvals.DecideAsync(decision with { Fingerprint = "wrong" }, TestContext.CancellationToken));
        if (cancel)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await run);
            Assert.AreEqual(0, writes);
            Assert.AreEqual(1, models);
        }
        else
        {
            await approvals.DecideAsync(decision, TestContext.CancellationToken);
            Assert.AreEqual("Done.", (await run).Messages.Last().Text);
            Assert.AreEqual(1, writes);
            Assert.AreEqual(2, models);
        }
    }

    /// <summary>An ambient agent span must not turn on MEAI tool descriptions/arguments/error-text collection.</summary>
    [TestMethod]
    public async Task AmbientAgentActivity_DoesNotEnableUnsafeToolTelemetry()
    {
        using var source = new ActivitySource("rodel-test-agent-" + Guid.NewGuid().ToString("N"));
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = value => value.Name == source.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        using (var ambient = source.StartActivity("invoke_agent"))
        {
            ambient!.SetTag("gen_ai.operation.name", "invoke_agent");
            var models = 0;
            using var driver = new AiTurnDriver(Context(), (_, _, _) => Task.FromResult(++models == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("one", "guarded_write", new Dictionary<string, object?> { ["private"] = "PRIVATE-ARGUMENT" })])) { FinishReason = ChatFinishReason.ToolCalls }
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")) { FinishReason = ChatFinishReason.Stop }));
            await Assert.ThrowsAsync<IOException>(async () => await driver.RunAsync([], [Tool()],
                (_, _) => throw new IOException("PRIVATE-STORAGE-PATH"), TestContext.CancellationToken));
        }

        Assert.HasCount(1, spans, "Only the test's parent span should exist; official tool instrumentation is intentionally isolated.");
    }

    private static AiProviderContext Context() => new("openai-chat", 1, "binding", "fixture", null, null, null);
    private static AIFunctionDeclaration Tool() => AiToolDeclarations.Create("guarded_write", "PRIVATE-TOOL-DESCRIPTION", new() { ["type"] = "object" });

    /// <summary>Gets or sets the owned operation deadline.</summary>
    public TestContext TestContext { get; set; }
}