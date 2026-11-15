using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects exact pending decisions and review drift independently of native tools or App callbacks.</summary>
[TestClass]
public sealed class ToolConfirmationTests
{
    /// <summary>
    /// The real executor must follow the selected mode even for a tool with a domain review: allow_operations
    /// must not wait for a user decision, and safe mode must combine permission and the frozen review into one prompt.
    /// Only command transport is replaced; persisted bindings, policy, journaling and decisions remain real.
    /// </summary>
    [TestMethod]
    [DataRow("allow_operations", 0)]
    [DataRow("safe", 1)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ReviewedOperation_UsesOneSafeDecision_AndNoneInAllowAll(string mode, int expectedDecisions)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        conversation["mode"] = mode;
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, mode == "allow_operations", TestContext.CancellationToken);
        turn["mode"] = mode;
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var decisions = 0;
        var dispatches = 0;
        var arguments = new JsonObject { ["arguments"] = new JsonArray("--help") };
        async Task Decide(string kind, string? id, JsonObject pending)
        {
            if (kind != "confirmation_required") return;
            decisions++;
            Assert.AreEqual(0, dispatches);
            await new AiToolApprovals(sandbox.Paths).DecideAsync(new()
            {
                ConversationId = conversation["id"]!.ToString(),
                TurnId = turn["id"]!.ToString(),
                InvocationId = id!,
                Fingerprint = pending["fingerprint"]!.ToString(),
                Approve = true
            }, TestContext.CancellationToken);
        }

        Task<JsonObject> Dispatch(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            dispatches++;
            return Task.FromResult(new JsonObject { ["status"] = "succeeded", ["data"] = new JsonObject { ["observed"] = true } });
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var result = await new AiToolExecutor(sandbox.Paths, turn, launcher, Dispatch)
            .InvokeAsync("execute", arguments, Guid.NewGuid().ToString("N"), "reviewed-command", Decide, TestContext.CancellationToken);
        Assert.AreEqual(expectedDecisions, decisions);
        Assert.AreEqual(1, dispatches);
        Assert.AreEqual("succeeded", result["status"]!.ToString());
        Assert.IsNull(sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), turn["id"]!.ToString())["pendingConfirmation"]);
    }

    /// <summary>
    /// A wrong fingerprint must not consume a decision; rejection or changed real source after approval must prevent dispatch.
    /// The callback runs only after durable awaiting_confirmation, and recovery must remove pending state without inventing execution.
    /// </summary>
    [TestMethod]
    [DataRow(false, "AI_CONFIRMATION_DECLINED")]
    [DataRow(true, "AI_CONFIRMATION_REVIEW_CHANGED")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ExactDecision_RejectsWrongFingerprint_AndDoesNotAcceptDeclinedOrChangedReview(bool changeSource, string code)
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var source = Path.Combine(sandbox.Root, "reviewed.txt");
        await File.WriteAllTextAsync(source, "original", TestContext.CancellationToken);
        var approvals = new AiToolApprovals(sandbox.Paths);
        var invocation = Guid.NewGuid().ToString("N");
        var decisions = 0;
        async Task Decide(string kind, string? id, JsonObject pending)
        {
            if (kind != "confirmation_required") return;
            decisions++;
            Assert.AreEqual("awaiting_confirmation", sandbox.Store.GetTurn(conversation["id"]!.ToString(), turn["id"]!.ToString()).Status);
            var decision = new AiToolConfirmation
            {
                ConversationId = conversation["id"]!.ToString(),
                TurnId = turn["id"]!.ToString(),
                InvocationId = invocation,
                Fingerprint = "wrong-fingerprint",
                Approve = changeSource
            };
            var wrong = await Assert.ThrowsAsync<LauncherOperationException>(async () => await approvals.DecideAsync(decision, TestContext.CancellationToken));
            Assert.AreEqual("AI_CONFIRMATION_CHANGED", wrong.Code);
            await Assert.ThrowsAsync<ArgumentException>(async () => await approvals.DecideAsync(decision with { Fingerprint = pending["fingerprint"]!.ToString(), Approve = true, Scope = "conversation" }, TestContext.CancellationToken));
            if (changeSource) await File.WriteAllTextAsync(source, "external edit", TestContext.CancellationToken);
            await approvals.DecideAsync(decision with { Fingerprint = pending["fingerprint"]!.ToString() }, TestContext.CancellationToken);
        }

        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await approvals.WaitAsync(turn, invocation, "reviewed_operation", [],
                new AiObservedToolReview(new JsonObject { ["content"] = "original" }),
                async ct => new AiObservedToolReview(new JsonObject { ["content"] = await File.ReadAllTextAsync(source, ct) }),
                ct => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }, Decide, TestContext.CancellationToken));
        Assert.AreEqual(code, failure.Code);
        Assert.AreEqual(1, decisions);
        var reopened = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), turn["id"]!.ToString(), true);
        Assert.AreEqual("running", reopened["status"]!.ToString());
        Assert.IsNull(reopened["pendingConfirmation"]);
        Assert.AreEqual(changeSource ? "external edit" : "original", await File.ReadAllTextAsync(source, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the deadline for actual pending decision persistence and source rechecks.</summary>
    public TestContext TestContext { get; set; }
}