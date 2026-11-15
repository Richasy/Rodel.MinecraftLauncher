// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Tests;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Runs recovery through the real room owner and bundled component, with controlled game endpoints.</summary>
[TestClass]
[DoNotParallelize]
public sealed class MultiplayerRecoveryTests
{
    /// <summary>Review: direct ping success alone misses the room's GamePort admission gate. A legacy-only listener owned by the selected synthetic session must reach the actual component's hosted state with automation disabled.</summary>
    [TestMethod]
    [TestCategory("Live")]
    [DataRow("Old world§1§8")]
    [DataRow("§1\0" + "78\0" + "1.6.4\0Old world\0" + "1\0" + "8")]
    public async Task ManualLegacyLan_ReachesHostedRoom(string legacy)
    {
        RequireLive();
        await ReportStorage.RunAsync(async paths =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            // The outer lifetime includes the component's own 60-second discovery and bounded teardown.
            stop.CancelAfter(TimeSpan.FromSeconds(150));
            var ct = stop.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Legacy LAN", "1.6.4", ct);
            await using var bridge = await BridgeFixture.CreateAsync(ct, paths.Root, instance.Id);
            await using var game = new MinecraftStatusFixture(legacy: legacy);
            var run = client.Multiplayer.RunAsync(new() { RoomId = Guid.NewGuid().ToString("N"), Role = "host", InstanceId = instance.Id, SessionId = bridge.Session, AutoOpen = false }, ct);
            try
            {
                var room = await WaitAsync(client, run, room => room.State == "connected", ct);
                Assert.AreEqual(game.Port, room.GamePort);
                Assert.IsNotNull(room.Invitation);
                await client.Multiplayer.ControlAsync(room.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
            }
            finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } }
        });
    }

    /// <summary>Review: only a known rejection permits a new publication. Start the listener after retry so early LAN discovery cannot hide the stalled state; unknown effects must resolve using the original operation instead.</summary>
    [TestMethod]
    [TestCategory("Live")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublicationRetry_DistinguishesRejectedAndUnknown(bool uncertain)
    {
        RequireLive();
        await ReportStorage.RunAsync(async paths =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            stop.CancelAfter(TimeSpan.FromSeconds(60));
            var ct = stop.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Publication recovery", "1.21.1", ct);
            await using var bridge = await BridgeFixture.CreateAsync(ct, paths.Root, instance.Id);
            var operations = new List<string>();
            var resolve = false;
            MinecraftStatusFixture? game = null;
            string? operationId = null;
            await using var peer = await bridge.ConnectAsync(request =>
            {
                JsonElement result;
                if (request.Method == "bridge.capabilities")
                {
                    result = JsonSerializer.SerializeToElement(new BridgeCapabilities { Capabilities = ["game.snapshot", "game.query", "world.publish"] }, BridgeWireJsonContext.Default.BridgeCapabilities);
                }
                else if (request.Method == "game.snapshot")
                {
                    result = JsonSerializer.SerializeToElement(new GameSnapshot { SessionId = bridge.Session, GameIncarnation = bridge.Incarnation, WorldEpoch = "1", LocalWorldEpoch = "1", Dimension = "minecraft:overworld", Ready = true, Mode = "local_owned", Sequence = "1", SampledAt = DateTimeOffset.UtcNow.ToString("O") }, GameBridgeJsonContext.Default.GameSnapshot);
                }
                else if (request.Method == "game.query")
                {
                    result = JsonSerializer.SerializeToElement(new GameQueryResult { SessionId = bridge.Session, GameIncarnation = bridge.Incarnation, WorldEpoch = "1", Dimension = "minecraft:overworld", Kind = "world", Source = "synthetic_server", SampledAt = DateTimeOffset.UtcNow.ToString("O"), Data = JsonSerializer.SerializeToElement(new JsonObject { ["name"] = "Recovery world" }) }, GameBridgeJsonContext.Default.GameQueryResult);
                }
                else if (request.Method is "game.edit" or "game.operation.show")
                {
                    if (request.Method == "game.edit")
                    {
                        operationId = request.Params!.Value.GetProperty("operationId").GetString();
                        lock (operations) operations.Add(operationId!);
                    }

                    if (Volatile.Read(ref resolve)) game ??= new();
                    result = JsonSerializer.SerializeToElement(new GameOperation
                    {
                        OperationId = operationId!,
                        State = game is not null ? "applied" : uncertain ? "unknown" : "rejected",
                        ErrorCode = game is not null ? null : uncertain ? "GAME_OPERATION_OUTCOME_UNKNOWN" : "GAME_WORLD_NOT_READY",
                        Result = game is null ? null : JsonSerializer.SerializeToElement(new JsonObject { ["port"] = game.Port })
                    }, GameBridgeJsonContext.Default.GameOperation);
                }
                else
                {
                    return bridge.Response(request);
                }

                return new BridgeRpc { Id = request.Id, Result = result };
            }, ct);
            var run = client.Multiplayer.RunAsync(new() { RoomId = Guid.NewGuid().ToString("N"), Role = "host", InstanceId = instance.Id, SessionId = bridge.Session }, ct);
            try
            {
                var manual = await WaitAsync(client, run, room => room.State == "manual", ct);
                Assert.IsNull(manual.GamePort);
                Volatile.Write(ref resolve, !uncertain);
                await client.Multiplayer.ControlAsync(manual.Id, "retry", ct: ct);
                if (uncertain)
                {
                    await WaitAsync(client, run, room => room.ControlId != manual.ControlId, ct);
                    lock (operations) Assert.HasCount(1, operations, "Retry must not dispatch an uncertain publication again.");
                    Volatile.Write(ref resolve, true);
                }

                var opened = await WaitAsync(client, run, room => room.World?.State == "open" && room.GamePort is not null, ct);
                Assert.AreEqual(game!.Port, opened.GamePort);
                lock (operations)
                {
                    Assert.HasCount(uncertain ? 1 : 2, operations);
                    Assert.AreEqual(operations.Count, operations.Distinct().Count(), "Known rejection retries use a new operation identity.");
                }

                await client.Multiplayer.ControlAsync(opened.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
            }
            finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } if (game is not null) await game.DisposeAsync(); }
        });
    }

    /// <summary>Review: syntax alone cannot establish reachability. The same room observes a closed port, a real status endpoint, its disappearance and recovery while retaining its controlled guest entry.</summary>
    [TestMethod]
    [TestCategory("Live")]
    public async Task Fallback_ObservesReachabilityLossAndRecovery()
    {
        RequireLive();
        await ReportStorage.RunAsync(async paths =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            stop.CancelAfter(TimeSpan.FromSeconds(60));
            var ct = stop.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Fallback recovery", "1.21.1", ct);
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            var run = client.Multiplayer.RunAsync(new() { RoomId = Guid.NewGuid().ToString("N"), Role = "guest", InstanceId = instance.Id, Invitation = RoomCode.Create(), StartGame = false }, ct);
            MinecraftStatusFixture? game = null;
            try
            {
                var initial = await WaitAsync(client, run, room => room.ComponentPort is not null, ct);
                await client.Multiplayer.ControlAsync(initial.Id, "fallback", "127.0.0.1:" + port, ct);
                var unavailable = await WaitAsync(client, run, room => room.Path == "fallback" && room.State == "disconnected", ct);
                Assert.IsNull(unavailable.Address);
                game = new(port);
                var ready = await WaitAsync(client, run, room => room.State == "connected", ct);
                Assert.IsNotNull(ready.Address);
                await game.DisposeAsync(); game = null;
                await WaitAsync(client, run, room => room.State == "disconnected" && room.Address is null, ct);
                game = new(port);
                var recovered = await WaitAsync(client, run, room => room.State == "connected", ct);
                Assert.AreEqual(ready.Address, recovered.Address);
                await client.Multiplayer.ControlAsync(initial.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
            }
            finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } if (game is not null) await game.DisposeAsync(); }
        });
    }

    /// <summary>Review: background probes must not revoke an established game or erase its launch error. The real room owner first reaches a controlled server, then loses status and finally all new connections; accepted game bytes still pass until explicit stop. The session record substitutes a running game without launching or closing a JVM.</summary>
    [TestMethod]
    [TestCategory("Live")]
    public async Task FallbackProbeFailures_PreserveGameStreamAndLaunchErrorUntilExplicitStop()
    {
        RequireLive();
        await ReportStorage.RunAsync(async paths =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            stop.CancelAfter(TimeSpan.FromSeconds(60));
            var ct = stop.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Fallback continuity", "1.21.1", ct);
            await using var bridge = await BridgeFixture.CreateAsync(ct, paths.Root, instance.Id);
            await using var server = new FallbackServer();
            var run = client.Multiplayer.RunAsync(new()
            {
                RoomId = Guid.NewGuid().ToString("N"),
                Role = "guest",
                InstanceId = instance.Id,
                SessionId = bridge.Session,
                Invitation = RoomCode.Create()
            }, ct);
            try
            {
                var initial = await WaitAsync(client, run, room => room.ComponentPort is not null, ct);
                await client.Multiplayer.ControlAsync(initial.Id, "fallback", "127.0.0.1:" + server.Port, ct);
                var ready = await WaitAsync(client, run, room => room.ErrorCode == "MULTIPLAYER_GAME_RESTART_REQUIRED", ct);
                var address = Servers.ServerService.ParseAddress(ready.Address!);
                using var game = new TcpClient();
                await game.ConnectAsync(address.Host, address.Port, ct);
                await game.GetStream().WriteAsync(RoomGameEntryTests.HandshakeWire("modern", address.Host, address.Port), ct);
                var upstream = await server.Game.WaitAsync(ct);
                await AssertForwardedAsync(game, upstream, 0x91, ct);

                server.AnswerStatus = false;
                await client.Multiplayer.ControlAsync(initial.Id, "retry", ct: ct);
                var unknown = await WaitAsync(client, run, room => room.ControlId != ready.ControlId, ct);
                Assert.AreEqual("connected", unknown.State, "Status unavailability must not become a login gate.");
                Assert.AreEqual("MULTIPLAYER_GAME_RESTART_REQUIRED", unknown.ErrorCode, "Refreshing network status must not erase the actionable game error.");
                await AssertForwardedAsync(game, upstream, 0x92, ct);

                server.StopAccepting();
                await WaitAsync(client, run, room => room.State == "disconnected", ct);
                await AssertForwardedAsync(game, upstream, 0x93, ct);
                await client.Multiplayer.ControlAsync(initial.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
                Assert.AreEqual(0, await game.GetStream().ReadAsync(new byte[1], ct), "Only explicit room stop revokes the still-working game stream.");
            }
            finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } }
        });
    }

    private static async Task AssertForwardedAsync(TcpClient game, TcpClient upstream, byte value, CancellationToken ct)
    {
        var buffer = new byte[1];
        await game.GetStream().WriteAsync(new[] { value }, ct);
        await upstream.GetStream().ReadExactlyAsync(buffer, ct);
        Assert.AreEqual(value, buffer[0]);
        await upstream.GetStream().WriteAsync(buffer, ct);
        await game.GetStream().ReadExactlyAsync(buffer, ct);
        Assert.AreEqual(value, buffer[0]);
    }

    private sealed class FallbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<TcpClient> _game = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serve;
        private volatile bool _accepting = true;
        internal volatile bool AnswerStatus = true;
        internal int Port { get; }
        internal Task<TcpClient> Game => _game.Task;

        internal FallbackServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = ServeAsync();
        }

        internal void StopAccepting() { _accepting = false; _listener.Stop(); }

        private async Task ServeAsync()
        {
            try
            {
                while (_accepting)
                {
                    var peer = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var transferred = false;
                    try
                    {
                        var stream = peer.GetStream();
                        var header = new byte[1];
                        if (await stream.ReadAsync(header, _stop.Token) == 0 || header[0] >= 128) continue;
                        var handshake = new byte[header[0]];
                        await stream.ReadExactlyAsync(handshake, _stop.Token);
                        if (handshake[^1] == 2)
                        {
                            transferred = _game.TrySetResult(peer);
                            continue;
                        }

                        await stream.ReadExactlyAsync(new byte[2], _stop.Token);
                        if (!AnswerStatus) continue;
                        var json = Encoding.UTF8.GetBytes("{\"version\":{},\"description\":\"Fallback fixture\"}");
                        await stream.WriteAsync(new byte[] { (byte)(json.Length + 2), 0, (byte)json.Length }.Concat(json).ToArray(), _stop.Token);
                    }
                    catch (IOException) { }
                    finally { if (!transferred) peer.Dispose(); }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception error) when (!_accepting && error is SocketException or ObjectDisposedException) { }
        }

        /// <summary>Closes only this fixture's listener and transferred game connection.</summary>
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); StopAccepting();
            await _serve;
            if (_game.Task.IsCompletedSuccessfully) _game.Task.Result.Dispose();
            _listener.Dispose();
            _stop.Dispose();
        }
    }

    private async Task<MultiplayerRoom> WaitAsync(LauncherClient client, Task<MultiplayerRoom> run, Func<MultiplayerRoom, bool> predicate, CancellationToken ct)
    {
        string? previous = null;
        while (true)
        {
            if (run.IsCompleted) await run;
            if (client.Multiplayer.Get() is { } room)
            {
                var phase = $"{room.State}/{room.ErrorCode}/component={room.ComponentState}/gamePortObserved={room.GamePort is not null}";
                if (phase != previous) { TestContext.WriteLine(phase); previous = phase; }

                if (predicate(room)) return room;
            }

            await Task.Delay(100, ct);
        }
    }

    private static void RequireLive()
    {
        if (Environment.GetEnvironmentVariable("RODEL_MULTIPLAYER_LIVE") != "1") Assert.Inconclusive("Set RODEL_MULTIPLAYER_LIVE=1 to run the bundled network component.");
    }

    /// <summary>Gets or sets the test deadline source.</summary>
    public TestContext TestContext { get; set; }
}