// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects AI permission and durable edit uncertainty using the real Core bridge. CLI/MEAI routing has separate integration coverage.</summary>
[TestClass]
public sealed class GameBridgeOperationTests
{
    /// <summary>Review: instance-only and observed-world checks cannot distinguish two running games of the same instance. A game-originated turn must reject another session even for a read, before the backend can establish a new world binding.</summary>
    [TestMethod]
    public async Task GameOriginatedTurn_CannotRetargetAnotherRunningSession()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var own = Id();
        turn["gameSessionId"] = own;
        var policy = new AiToolPolicy(sandbox.Paths, turn);
        var refused = Assert.ThrowsExactly<LauncherOperationException>(() => policy.Check("game_snapshot", new() { ["sessionId"] = Id() }, TestContext.CancellationToken));
        Assert.AreEqual("AI_GAME_SESSION_SCOPE", refused.Code);
        policy.Check("game_snapshot", new() { ["sessionId"] = own }, TestContext.CancellationToken);
    }

    /// <summary>Review: read-only AI mode must stop before the backend; allow-operations may reach Core but must not bypass a remote world's independent guard. This distinguishes AI permission from world authority.</summary>
    [TestMethod]
    public async Task OperationPermission_StillCannotWriteServerWorld()
    {
        using var sandbox = new AiSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root);
        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request, "remote_read_only"), TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var readonlyTurn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var launcher = new AiCoreTools(sandbox.Paths);
        var calls = 0;
        async Task<JsonObject> Backend(string name, JsonObject input, CancellationToken ct, string? requestId)
        {
            if (name == "game_snapshot") return new() { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(await fixture.Service.SnapshotAsync(fixture.Session, ct), GameBridgeJsonContext.Default.GameSnapshot) };
            calls++;
            var edit = input["request"]!.Deserialize(GameBridgeJsonContext.Default.GameEditRequest)!;
            var result = await fixture.Service.EditAsync(fixture.Session, edit, ct);
            return new() { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(result, GameBridgeJsonContext.Default.GameOperation) };
        }

        var request = new JsonObject { ["sessionId"] = fixture.Session, ["request"] = JsonSerializer.SerializeToNode(fixture.Edit(), GameBridgeJsonContext.Default.GameEditRequest) };
        var denied = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => new AiToolExecutor(sandbox.Paths, readonlyTurn, launcher, Backend)
            .InvokeAsync("game_edit", request.DeepClone().AsObject(), Id(), "readonly", Emit, TestContext.CancellationToken));
        Assert.AreEqual("AI_TOOL_READ_ONLY", denied.Code);
        Assert.AreEqual(0, calls);
        var allowedTurn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var allowed = new AiToolExecutor(sandbox.Paths, allowedTurn, launcher, Backend);
        _ = await allowed.InvokeAsync("game_snapshot", new() { ["sessionId"] = fixture.Session }, Id(), "observe", Emit, TestContext.CancellationToken);
        var refused = await allowed
            .InvokeAsync("game_edit", request, Id(), "allowed", Emit, TestContext.CancellationToken);
        Assert.AreEqual("SERVER_READ_ONLY", refused["data"]?["errorCode"]?.ToString());
        Assert.AreEqual(1, calls);
        Assert.IsTrue(allowedTurn["intents"]![0]!["verification"]!["effectResolved"]!.GetValue<bool>());
    }

    /// <summary>Review: a queued game result is not final success. A distinct overlapping edit must remain blocked until the exact persisted receipt is observed terminal; ordinary returned-result reuse would miss this pending effect.</summary>
    [TestMethod]
    public async Task QueuedEdit_BlocksOverlappingMutation_UntilExactReceiptResolves()
    {
        using var sandbox = new AiSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, sandbox.Root);
        var edit = fixture.Edit() with { Kind = "player.control", Health = null, Control = new() { Keys = ["forward"], Ticks = 2 } };
        await using var peer = await fixture.ConnectAsync(request => request.Method == "game.edit"
            ? new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(new GameOperation { OperationId = edit.OperationId, State = "queued" }, GameBridgeJsonContext.Default.GameOperation) }
            : fixture.Response(request), TestContext.CancellationToken);
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var journal = new AiToolJournal(sandbox.Paths);
        var args = new JsonObject { ["sessionId"] = fixture.Session, ["request"] = JsonSerializer.SerializeToNode(edit, GameBridgeJsonContext.Default.GameEditRequest) };
        var intent = await journal.PrepareAsync(turn, Id(), "pending", "game_edit", args, TestContext.CancellationToken);
        await journal.DispatchingAsync(turn, intent, TestContext.CancellationToken);
        var queued = await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken);
        await journal.CompleteAsync(turn, intent, new() { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(queued, GameBridgeJsonContext.Default.GameOperation) }, TestContext.CancellationToken);
        Assert.AreEqual("unknown_effect", intent["state"]?.ToString());
        // A stopped model turn may still have a queued native action; this is silent waiting, not a warning or success.
        turn["status"] = "completed";
        await sandbox.Store.SaveTurnAsync(turn, TestContext.CancellationToken);
        var pending = await journal.CheckAsync(conversation["id"]!.ToString(), turn["id"]!.ToString(), TestContext.CancellationToken);
        Assert.IsTrue(AiTurnObservation.HasPendingEffects(pending));
        Assert.IsFalse(AiTurnObservation.HasEffectProblem(pending));
        var next = args.DeepClone().AsObject(); next["request"]!["operationId"] = Id();
        var blocked = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => journal.PrepareAsync(turn, Id(), "next", "game_edit", next, TestContext.CancellationToken));
        Assert.AreEqual("AI_EFFECT_REVIEW_REQUIRED", blocked.Code);
        var file = GameBridgeService.JournalPath(sandbox.Paths, fixture.Session, edit.OperationId);
        var stored = await GameBridgeService.ReadJournalAsync(file, TestContext.CancellationToken);
        // This is the controlled terminal-receipt boundary; real Java application/readback has separate game evidence.
        await GameBridgeService.WriteJournalAsync(file, stored with { Operation = queued with { State = "applied" } }, TestContext.CancellationToken);
        _ = await journal.PrepareAsync(turn, Id(), "after-observation", "game_edit", next, TestContext.CancellationToken);
        Assert.AreEqual("effect_outcome_observed", intent["state"]?.ToString());
        Assert.IsFalse(intent["verification"]!["desiredStateObserved"]!.GetValue<bool>());
    }

    private static string Id() => Guid.NewGuid().ToString("N");

    /// <summary>Review: a model must not retarget an authorized turn after a world switch, even using a later snapshot. The first observed world is frozen independently of model-supplied local-mode claims.</summary>
    [TestMethod]
    public async Task WorldReplacement_InSameTurn_DoesNotRetargetItsEditAuthority()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var session = Id(); var incarnation = Id();
        var policy = new AiToolPolicy(sandbox.Paths, turn);
        var observed = new GameSnapshot { SessionId = session, GameIncarnation = incarnation, Sequence = "1", SampledAt = DateTimeOffset.UtcNow.ToString("O"), Ready = true, Mode = "local_owned", WorldEpoch = "1", Dimension = "minecraft:overworld" };
        var arguments = new JsonObject { ["sessionId"] = session };
        Assert.IsTrue(policy.ObserveGameSnapshot(arguments, new() { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(observed, GameBridgeJsonContext.Default.GameSnapshot) }));
        Assert.IsFalse(policy.ObserveGameSnapshot(arguments, new() { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(observed with { WorldEpoch = "2", Sequence = "2" }, GameBridgeJsonContext.Default.GameSnapshot) }));
        arguments["request"] = JsonSerializer.SerializeToNode(new GameEditRequest { OperationId = Id(), GameIncarnation = incarnation, WorldEpoch = "2", Dimension = "minecraft:overworld", Kind = "player.setHealth", Health = 13 }, GameBridgeJsonContext.Default.GameEditRequest);
        var refusal = Assert.ThrowsExactly<LauncherOperationException>(() => policy.Check("game_edit", arguments, TestContext.CancellationToken));
        Assert.AreEqual("AI_GAME_WORLD_CHANGED", refusal.Code);
    }
    private static Task Emit(string kind, string? invocation, JsonObject detail) => Task.CompletedTask;

    /// <summary>Review: dimension travel and respawn replace client objects but retain the same owned integrated world. A new local server must still be unable to retarget the turn; legacy snapshots without a root keep the stricter existing behavior.</summary>
    [TestMethod]
    public async Task SameLocalWorld_TravelUpdatesObservation_NewLocalServerCannotRetarget()
    {
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, true, TestContext.CancellationToken);
        var session = Id(); var incarnation = Id();
        var policy = new AiToolPolicy(sandbox.Paths, turn);
        var observed = new GameSnapshot { SessionId = session, GameIncarnation = incarnation, Sequence = "1", SampledAt = DateTimeOffset.UtcNow.ToString("O"), Ready = true, Mode = "local_owned", WorldEpoch = "1", LocalWorldEpoch = "1", Dimension = "minecraft:overworld" };
        var arguments = new JsonObject { ["sessionId"] = session };
        bool Observe(GameSnapshot value) => policy.ObserveGameSnapshot(arguments, new() { ["status"] = "succeeded", ["data"] = JsonSerializer.SerializeToNode(value, GameBridgeJsonContext.Default.GameSnapshot) });
        Assert.IsTrue(Observe(observed));
        Assert.IsTrue(Observe(observed with { WorldEpoch = "2", Dimension = "minecraft:the_nether" }));
        Assert.AreEqual("2", turn["gameWorldBindings"]![session]!["worldEpoch"]!.ToString());
        Assert.IsFalse(Observe(observed with { WorldEpoch = "3", LocalWorldEpoch = "2" }));
        arguments["request"] = JsonSerializer.SerializeToNode(new GameEditRequest { OperationId = Id(), GameIncarnation = incarnation, WorldEpoch = "3", Dimension = "minecraft:overworld", Kind = "player.control", Control = new() { Keys = ["forward"] } }, GameBridgeJsonContext.Default.GameEditRequest);
        Assert.AreEqual("AI_GAME_WORLD_CHANGED", Assert.ThrowsExactly<LauncherOperationException>(() => policy.Check("game_edit", arguments, TestContext.CancellationToken)).Code);
    }
    /// <summary>Gets the test runner cancellation context.</summary>
    public TestContext TestContext { get; set; } = null!;
}