// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Tests;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects real Core TCP/pipe authentication, local edit admission and durable uncertainty. The peer substitutes Minecraft, not transport or storage.</summary>
[TestClass]
public sealed class GameBridgeTests
{
    /// <summary>Review: reverse AI ingress must not block the same socket reader needed for a nested game observation. Actual framed TCP traffic reaches both directions before either call finishes; a synchronous reader dispatcher deadlocks this case.</summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task GameOriginatedRequest_NestedGameObservation_UsesTheSameAuthenticatedTransport()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        var accepting = listener.AcceptTcpClientAsync(TestContext.CancellationToken);
        await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, TestContext.CancellationToken);
        using var accepted = await accepting;
        BridgeConnection? connection = null;
        connection = new(accepted.GetStream(), async (request, ct) =>
        {
            Assert.AreEqual("launcher.ai", request.Method);
            return await connection!.CallAsync("game.snapshot", null, ct);
        });
        await using var lifetime = connection;
        connection.Start();
        var stream = peer.GetStream();
        await FramedJsonPipe.WriteAsync(stream, new BridgeRpc { Id = "from-game", Method = "launcher.ai" }, BridgeWireJsonContext.Default.BridgeRpc, "TEST", TestContext.CancellationToken);
        var nested = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeRpc, "TEST", TestContext.CancellationToken);
        Assert.AreEqual("game.snapshot", nested.Method);
        using var observation = JsonDocument.Parse("""{"ready":true,"world":"independent-oracle"}""");
        await FramedJsonPipe.WriteAsync(stream, new BridgeRpc { Id = nested.Id, Result = observation.RootElement }, BridgeWireJsonContext.Default.BridgeRpc, "TEST", TestContext.CancellationToken);
        var result = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeRpc, "TEST", TestContext.CancellationToken);
        Assert.AreEqual("from-game", result.Id);
        Assert.AreEqual("independent-oracle", result.Result!.Value.GetProperty("world").GetString());
    }

    /// <summary>Review: local ownership alone must not authorize commands. The authenticated peer reports native cheats disabled; neither a second submission nor any edit dispatch may bypass that independent permission.</summary>
    [TestMethod]
    public async Task LocalWorld_CommandsRequireActualCheats_RejectBeforeDispatch()
    {
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken);
        var queries = 0; var edits = 0;
        using var data = JsonDocument.Parse("""{"allowCommands":false}""");
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.edit") Interlocked.Increment(ref edits);
            if (request.Method != "game.query") return fixture.Response(request);
            Interlocked.Increment(ref queries);
            var query = request.Params!.Value.Deserialize(GameBridgeJsonContext.Default.GameQueryRequest)!;
            return new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(new GameQueryResult { SessionId = fixture.Session, GameIncarnation = query.GameIncarnation, WorldEpoch = query.WorldEpoch, Dimension = query.Dimension, SampledAt = DateTimeOffset.UtcNow.ToString("O"), Kind = "world", Source = "server_authoritative", Data = data.RootElement }, GameBridgeJsonContext.Default.GameQueryResult) };
        }, TestContext.CancellationToken);
        var edit = fixture.Edit() with { Kind = "command.execute", Health = null, Command = "give @s diamond" };
        var result = await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken);
        Assert.AreEqual("rejected", result.State);
        Assert.AreEqual("GAME_CHEATS_DISABLED", result.ErrorCode);
        Assert.AreEqual(result, await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken));
        Assert.AreEqual(1, queries);
        Assert.AreEqual(0, edits);
    }

    /// <summary>Review: a remote capability must prevent dispatch even for a live authenticated peer. Persisted rejection and conflicting ID reuse distinguish admission from merely relaying a Java refusal.</summary>
    [TestMethod]
    [DataRow("player.setHealth")]
    [DataRow("command.execute")]
    [DataRow("player.control")]
    [DataRow("inventory.button")]
    public async Task RemoteWorld_WriteNeverReachesPeer_ReceiptAndFingerprintRemainStable(string kind)
    {
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken);
        var edits = 0;
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.edit") Interlocked.Increment(ref edits);
            return fixture.Response(request, "remote_read_only");
        }, TestContext.CancellationToken);
        var snapshot = await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken);
        Assert.AreEqual("remote_read_only", snapshot.Mode);
        var edit = fixture.Edit();
        if (kind == "command.execute") edit = edit with { Kind = kind, Health = null, Command = "time query daytime" };
        else if (kind == "player.control") edit = edit with { Kind = kind, Health = null, Control = new() { Keys = ["attack"], Ticks = 20 } };
        else if (kind == "inventory.button") edit = edit with { Kind = kind, Health = null, Button = new() { ContainerId = 1, StateId = 1, Button = 0 } };
        var refused = await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken);
        Assert.AreEqual("rejected", refused.State);
        Assert.AreEqual("SERVER_READ_ONLY", refused.ErrorCode);
        Assert.AreEqual(refused, await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken));
        var conflict = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => fixture.Service.EditAsync(fixture.Session, edit with { Dimension = "minecraft:the_end" }, TestContext.CancellationToken));
        Assert.AreEqual("GAME_OPERATION_CONFLICT", conflict.Code);
        Assert.AreEqual(0, edits, "Core must not send a remote write and rely solely on its peer to refuse it.");
        Assert.DoesNotContain(fixture.Secret, await File.ReadAllTextAsync(Path.Combine(fixture.Paths.SessionsDirectory, fixture.Session + ".json"), TestContext.CancellationToken));
    }

    /// <summary>Review: the peer applies then disconnects before returning its receipt. A fresh authenticated connection may query that edit, but even repeating the exact Core request must not dispatch it again.</summary>
    [TestMethod]
    public async Task AppliedThenDisconnected_ReconnectQueriesReceipt_WithoutReplayingIntent()
    {
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken);
        var edit = fixture.Edit();
        var applied = new GameOperation { OperationId = edit.OperationId, State = "applied", Health = 13, ServerThread = "synthetic-server" };
        var edits = 0;
        await using (var lost = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.edit") { Interlocked.Increment(ref edits); return null; }

            return fixture.Response(request);
        }, TestContext.CancellationToken))
        {
            var unknown = await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken);
            Assert.AreEqual("unknown", unknown.State);
        }

        await using var recovered = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.edit") Interlocked.Increment(ref edits);
            return request.Method == "game.operation.show" ? new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(applied, GameBridgeJsonContext.Default.GameOperation) } : fixture.Response(request);
        }, TestContext.CancellationToken);
        Assert.AreEqual(applied, await fixture.Service.EditAsync(fixture.Session, edit, TestContext.CancellationToken));
        Assert.AreEqual(applied, await fixture.Service.GetOperationAsync(fixture.Session, edit.OperationId, TestContext.CancellationToken));
        Assert.AreEqual(1, edits, "Receipt recovery must never repeat an uncertain mutation.");
    }

    /// <summary>Review: a malformed unauthenticated frame must release the connection slot, allowing a legitimate fresh challenge afterwards. This is independent of business guard tests.</summary>
    [TestMethod]
    [DataRow("length")]
    [DataRow("proof")]
    [DataRow("oversized")]
    [DataRow("null_challenge")]
    public async Task InvalidHandshake_DoesNotOccupyBridge_ValidPeerCanConnect(string corruption)
    {
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken);
        using (var invalid = new TcpClient())
        {
            await invalid.ConnectAsync("127.0.0.1", fixture.Port, TestContext.CancellationToken);
            var stream = invalid.GetStream();
            _ = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeHandshake, "TEST", TestContext.CancellationToken);
            if (corruption == "proof")
                await FramedJsonPipe.WriteAsync(stream, new BridgeHandshake { SessionId = fixture.Session, Challenge = new string('1', 64), Proof = new string('0', 64) }, BridgeWireJsonContext.Default.BridgeHandshake, "TEST", TestContext.CancellationToken);
            else if (corruption == "oversized") await stream.WriteAsync(new byte[] { 1, 16, 0, 0 }, TestContext.CancellationToken);
            else if (corruption == "null_challenge") await FramedJsonPipe.WriteAsync(stream, new JsonObject { ["version"] = 1, ["sessionId"] = fixture.Session, ["challenge"] = null }, "TEST", TestContext.CancellationToken);
            else await stream.WriteAsync(new byte[4], TestContext.CancellationToken);
        }

        await using var peer = await fixture.ConnectAsync(request => fixture.Response(request), TestContext.CancellationToken);
        Assert.AreEqual("local_owned", (await fixture.Service.SnapshotAsync(fixture.Session, TestContext.CancellationToken)).Mode);
    }

    /// <summary>Gets the runner cancellation context, avoiding unbounded background fixture operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Review: a delayed queued observation must not overwrite a committed terminal receipt. A file lease and peer-response barrier control the ordering; storage and the actual pipe/TCP query remain real.</summary>
    [TestMethod]
    public async Task DelayedQueuedQuery_CannotRegressDurableAppliedReceipt()
    {
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken);
        var edit = fixture.Edit();
        var queued = new GameOperation { OperationId = edit.OperationId, State = "queued" };
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method != "game.operation.show") return fixture.Response(request);
            observed.TrySetResult();
            return new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(queued, GameBridgeJsonContext.Default.GameOperation) };
        }, TestContext.CancellationToken);
        var file = GameBridgeService.JournalPath(fixture.Paths, fixture.Session, edit.OperationId);
        var entry = new BridgeJournalEntry { Fingerprint = "fixture", Request = edit, Operation = queued };
        await GameBridgeService.WriteJournalAsync(file, entry, TestContext.CancellationToken);
        var lease = await ResourceLease.AcquireAsync(file, TestContext.CancellationToken);
        Task<GameOperation> query;
        var applied = queued with { State = "applied", Health = 13 };
        try
        {
            query = fixture.Service.GetOperationAsync(fixture.Session, edit.OperationId, TestContext.CancellationToken);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
            await GameBridgeService.WriteJournalAsync(file, entry with { Operation = applied }, TestContext.CancellationToken);
        }
        finally { lease.Dispose(); }

        Assert.AreEqual(applied, await query);
        Assert.AreEqual(applied, (await GameBridgeService.ReadJournalAsync(file, TestContext.CancellationToken)).Operation);
    }
}