using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>
/// Protects authorization and durable write recovery across the real executor, settings and journal.
/// Only MCP transport/CLI dispatch is substituted; CLI token mapping and stdio contracts have their own CLI tests.
/// </summary>
[TestClass]
public sealed class ToolOperationTests
{
    /// <summary>
    /// Safe mode must pause real settings writes before dispatch. Unlike exact-review tests, this exercises the
    /// permission gate across different calls and turns: once cannot leak, named-tool approval cannot authorize
    /// another command, conversation approval can persist,
    /// and changing mode revokes both old in-flight authority and the standing grant. Only CLI transport is substituted.
    /// </summary>
    [TestMethod]
    [DataRow("once", 2)]
    [DataRow("tool", 1)]
    [DataRow("conversation", 1)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task SafeWrites_RespectApprovalScope_AndModeChangeRevokesIt(string scope, int expectedDecisions)
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        await client.Settings.UpdateAsync(new JsonObject { ["maxMemoryMb"] = 2048 }, [], cancellationToken: TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        conversation["mode"] = "safe";
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        var cid = conversation["id"]!.ToString();
        var decisions = 0;
        var writes = 0;
        var approve = true;
        var currentMemory = 2048;
        async Task Decide(string kind, string? invocation, JsonObject detail)
        {
            if (kind != "confirmation_required") return;
            decisions++;
            Assert.IsTrue(detail["operationPermission"]!.GetValue<bool>());
            Assert.AreEqual(currentMemory, client.Settings.GetOverrides()["maxMemoryMb"]!.GetValue<int>(), "No settings may change before the exact user decision.");
            var waiting = sandbox.Store.TurnDocuments(cid).Single(turn => turn?["pendingConfirmation"]?["id"]?.ToString() == invocation)!;
            await new AiToolApprovals(sandbox.Paths).DecideAsync(new()
            {
                ConversationId = cid,
                TurnId = waiting["id"]!.ToString(),
                InvocationId = invocation!,
                Fingerprint = detail["fingerprint"]!.ToString(),
                Approve = approve,
                Scope = approve ? scope : "once"
            }, TestContext.CancellationToken);
        }

        async Task<JsonObject> Apply(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            writes++;
            var result = await PatchAsync(client, input, ct);
            currentMemory = input["changes"]!["maxMemoryMb"]!.GetValue<int>();
            return result;
        }

        async Task<JsonObject> SafeTurn()
        {
            var turn = await sandbox.TurnAsync(sandbox.Store.GetDocument(cid), false, TestContext.CancellationToken);
            turn["mode"] = "safe";
            await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
            return turn;
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var first = await SafeTurn();
        await new AiToolExecutor(sandbox.Paths, first, launcher, Apply).InvokeAsync("config_patch", Patch(4096), Id(), "first", Decide, TestContext.CancellationToken);
        first["status"] = "completed";
        await sandbox.Store.SaveTurnAsync(first, TestContext.CancellationToken);
        var second = await SafeTurn();
        var executor = new AiToolExecutor(sandbox.Paths, second, launcher, Apply);
        await executor.InvokeAsync("config_patch", Patch(6144), Id(), "second", Decide, TestContext.CancellationToken);
        Assert.AreEqual(expectedDecisions, decisions);
        Assert.AreEqual(2, writes);
        if (scope == "tool")
        {
            approve = false;
            var otherTool = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
                await executor.InvokeAsync("config_set", new JsonObject { ["key"] = "maxMemoryMb", ["value"] = 8192 }, Id(), "other-tool", Decide, TestContext.CancellationToken));
            Assert.AreEqual("AI_CONFIRMATION_DECLINED", otherTool.Code);
            Assert.AreEqual(expectedDecisions + 1, decisions, "A grant for config_patch must still request a decision for config_set.");
            Assert.AreEqual(2, writes);
            approve = true;
        }

        var decisionsBeforeRevocation = decisions;
        var current = sandbox.Store.GetDocument(cid);
        await new AiConversationHistory(sandbox.Paths).UpdateAsync(new() { ConversationId = cid, ExpectedRevision = current["revision"]!.GetValue<long>(), Mode = "safe" }, TestContext.CancellationToken);
        var revoked = await Assert.ThrowsAsync<LauncherOperationException>(async () => await executor.InvokeAsync("config_patch", Patch(8192), Id(), "revoked", Decide, TestContext.CancellationToken));
        Assert.AreEqual("AI_OPERATION_AUTHORIZATION_REVOKED", revoked.Code);
        Assert.AreEqual(decisionsBeforeRevocation, decisions);
        approve = false;
        var next = await SafeTurn();
        var rejected = await Assert.ThrowsAsync<LauncherOperationException>(async () => await new AiToolExecutor(sandbox.Paths, next, launcher, Apply)
            .InvokeAsync("config_patch", Patch(8192), Id(), "declined", Decide, TestContext.CancellationToken));
        Assert.AreEqual("AI_CONFIRMATION_DECLINED", rejected.Code);
        Assert.AreEqual(decisionsBeforeRevocation + 1, decisions);
        Assert.AreEqual(2, writes);
        Assert.AreEqual(6144, client.Settings.GetOverrides()["maxMemoryMb"]!.GetValue<int>());
    }

    /// <summary>
    /// A one-turn grant may patch selected settings, but retry must reuse the durable result and a later read-only
    /// turn must not inherit permission. Real persisted unknown fields distinguish a patch from whole-object replacement.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ExplicitTurnPatch_RetryReusesEffect_AndNextTurnStaysReadOnly()
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        await client.Settings.UpdateAsync(new JsonObject { ["extension"] = new JsonObject { ["keep"] = "original" } }, [], cancellationToken: TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var dispatches = 0;
        Task<JsonObject> Apply(string _, JsonObject input, CancellationToken ct, string? requestId)
        {
            Interlocked.Increment(ref dispatches);
            return PatchAsync(client, input, ct);
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, Apply);
        var arguments = Patch(4096);

        var first = await executor.InvokeAsync("config_patch", arguments.DeepClone().AsObject(), Id(), "first", Emit, TestContext.CancellationToken);
        Assert.IsTrue(first["verification"]!["desiredStateObserved"]!.GetValue<bool>());
        var retried = await executor.InvokeAsync("config_patch", arguments.DeepClone().AsObject(), Id(), "retry", Emit, TestContext.CancellationToken);
        Assert.IsFalse(retried["mutationReplayed"]!.GetValue<bool>());
        Assert.AreEqual(1, dispatches, "A repeated tool call in the same goal must not execute the write again.");
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(sandbox.Paths.SettingsFile, TestContext.CancellationToken))!;
        Assert.AreEqual(4096, persisted["maxMemoryMb"]!.GetValue<int>());
        Assert.AreEqual("original", persisted["extension"]!["keep"]!.GetValue<string>());

        var next = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var readOnly = new AiToolExecutor(sandbox.Paths, next, launcher, Apply);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await readOnly.InvokeAsync("config_patch", Patch(6144), Id(), "no-grant", Emit, TestContext.CancellationToken));
        Assert.AreEqual("AI_TOOL_READ_ONLY", failure.Code);
        Assert.AreEqual(1, dispatches);
        Assert.AreEqual(4096, JsonNode.Parse(await File.ReadAllTextAsync(sandbox.Paths.SettingsFile, TestContext.CancellationToken))!["maxMemoryMb"]!.GetValue<int>());
    }

    /// <summary>
    /// Revocation at the persisted prepared checkpoint must prevent transport dispatch and leave settings unchanged.
    /// The emit barrier reaches the actual executor boundary, unlike racing tasks or sleeping before changing permission.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task RevokeAtPreparedCheckpoint_PreventsDispatchAndPreservesExistingSettings()
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        await client.Settings.UpdateAsync(new JsonObject { ["maxMemoryMb"] = 2048 }, [], cancellationToken: TestContext.CancellationToken);
        var original = await File.ReadAllBytesAsync(sandbox.Paths.SettingsFile, TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var prepared = false;
        var dispatches = 0;
        Task<JsonObject> Backend(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            dispatches++;
            return PatchAsync(client, input, ct);
        }

        async Task Checkpoint(string kind, string? step, JsonObject detail)
        {
            if (kind != "intent_prepared") return;
            prepared = true;
            conversation["operationRevision"] = 1L;
            await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await new AiToolExecutor(sandbox.Paths, turn, launcher, Backend)
                .InvokeAsync("config_patch", Patch(4096), Id(), "pending", Checkpoint, TestContext.CancellationToken));
        Assert.IsTrue(prepared, "The operation must reach the persisted preparation checkpoint before permission changes.");
        Assert.AreEqual("AI_OPERATION_AUTHORIZATION_REVOKED", failure.Code);
        Assert.AreEqual(0, dispatches);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(sandbox.Paths.SettingsFile, TestContext.CancellationToken));
        Assert.AreEqual("not_dispatched", turn["intents"]![0]!["state"]!.GetValue<string>());
    }

    /// <summary>
    /// A transport loss after a real settings write must persist unknown attribution and block replay.
    /// Reopening the journal and reading actual settings may observe the desired state, never fabricate the missing response.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task LostResponseAfterWrite_BlocksReplay_AndReadbackKeepsAttributionUnknown()
    {
        using var sandbox = new AiSandbox();
        var client = new LauncherClient(sandbox.Paths);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var writes = 0;
        async Task<JsonObject> LoseResponse(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            writes++;
            await PatchAsync(client, input, ct);
            throw new IOException("Controlled transport loss after the settings commit.");
        }

        var launcher = new AiCoreTools(sandbox.Paths);
        var executor = new AiToolExecutor(sandbox.Paths, turn, launcher, LoseResponse);
        var failure = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await executor.InvokeAsync("config_patch", Patch(4096), Id(), "lost", Emit, TestContext.CancellationToken));
        Assert.AreEqual("AI_TOOL_EFFECT_UNKNOWN", failure.Code);
        Assert.AreEqual("unknown_effect", turn["intents"]![0]!["state"]!.GetValue<string>());
        var blocked = await Assert.ThrowsAsync<LauncherOperationException>(async () =>
            await executor.InvokeAsync("config_patch", Patch(4096), Id(), "repeat", Emit, TestContext.CancellationToken));
        Assert.AreEqual("AI_EFFECT_REVIEW_REQUIRED", blocked.Code);
        Assert.AreEqual(1, writes);

        turn["status"] = "failed";
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var observed = await new AiToolJournal(sandbox.Paths).CheckAsync(conversation["id"]!.ToString(), turn["id"]!.ToString(), TestContext.CancellationToken);
        var intent = observed.Intents![0]!;
        Assert.IsTrue(intent["verification"]!["desiredStateObserved"]!.GetValue<bool>());
        Assert.AreEqual("unknown", intent["executionAttribution"]!.GetValue<string>());
        Assert.IsNull(intent["result"], "Readback cannot reconstruct a transport response that was never recorded.");
    }

    private static JsonObject Patch(int memory) => new() { ["changes"] = new JsonObject { ["maxMemoryMb"] = memory } };
    private static string Id() => Guid.NewGuid().ToString("N");
    private static Task Emit(string kind, string? step, JsonObject detail) => Task.CompletedTask;
    private static async Task<JsonObject> PatchAsync(LauncherClient client, JsonObject input, CancellationToken ct)
    {
        await client.Settings.UpdateAsync(input["changes"]!.DeepClone().AsObject(), [], cancellationToken: ct);
        return new JsonObject
        {
            ["status"] = "succeeded",
            ["data"] = JsonSerializer.SerializeToNode(client.Settings.GetEffective(), ConfigurationJsonContext.Default.EffectiveSettings)
        };
    }

    /// <summary>Gets or sets MSTest's cancellation and timeout context.</summary>
    public TestContext TestContext { get; set; }
}