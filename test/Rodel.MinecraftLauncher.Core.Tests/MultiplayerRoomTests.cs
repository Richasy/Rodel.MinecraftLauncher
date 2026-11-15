// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Tests;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects native invitation compatibility and the real component/room lifecycle; synthetic game sockets do not establish Minecraft gameplay.</summary>
[TestClass]
[DoNotParallelize]
public sealed class MultiplayerRoomTests
{
    /// <summary>Provides cooperative cancellation for owned processes and sockets.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Review: native checksum and alias rules are required for cross-client invitations; a shape-only parser accepts the independently known invalid sample.</summary>
    [TestMethod]
    public void NativeCodes_ExtractAndValidateWithoutReadingEnvironmentText()
    {
        Assert.AreEqual("U/YNZE-U61D-2206-HXRG", RoomCode.Parse("join u/ynze-u61d-22o6-hxrg 1.20.1"));
        Assert.AreEqual("U/4K3M-9V6A-2R8D-5XQ4", RoomCode.Parse("U/7K3M-9V6A-2R8D-5XQ4 then U/4K3M-9V6A-2R8D-5XQ4"));
        Assert.IsNull(RoomCode.Parse("U/7K3M-9V6A-2R8D-5XQ4"));
        var created = RoomCode.Create();
        Assert.AreEqual(created, RoomCode.Parse(created));
    }

    // Author review: the real pinned component, Windows PID/port table, DPAPI and durable controls
    // add evidence absent from parser/HTTP fixtures. Only the Minecraft status listener, LAN broadcast
    // and session record are synthetic. Kill occurs after connected with the recorded worker identity:
    // recovery must replace that worker, keep the same code/game, then stop without killing the game.
    /// <summary>Owns one official component room, rejects another owner, hides durable codes and preserves the game when stopped.</summary>
    [TestMethod]
    [TestCategory("Live")]
    public async Task OfficialComponent_HostPortAndStop_PreserveOwnershipAndGame()
    {
        if (Environment.GetEnvironmentVariable("RODEL_MULTIPLAYER_LIVE") != "1")
            Assert.Inconclusive("Set RODEL_MULTIPLAYER_LIVE=1 to run the bundled component with public discovery services.");
        await ReportStorage.RunAsync(async paths =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(150));
            var ct = cancellation.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Protocol fixture", "1.21.1", ct);
            using var process = Process.GetCurrentProcess();
            var sessionId = Guid.NewGuid().ToString("N");
            await JsonFiles.WriteAsync(Path.Combine(paths.SessionsDirectory, sessionId + ".json"), new JsonObject
            {
                ["id"] = sessionId,
                ["instanceId"] = instance.Id,
                ["status"] = "running",
                ["gameDirectory"] = instance.GameDirectory,
                ["processId"] = process.Id,
                ["processStartUtc"] = process.StartTime.ToUniversalTime().ToString("O"),
                ["monitorId"] = process.Id,
                ["monitorStartUtc"] = process.StartTime.ToUniversalTime().ToString("O")
            }, ct);
            await using var game = new StatusListener();
            var request = new MultiplayerRequest { RoomId = Guid.NewGuid().ToString("N"), Role = "host", InstanceId = instance.Id, SessionId = sessionId, AutoOpen = false, Name = "Protocol fixture" };
            var run = client.Multiplayer.RunAsync(request, ct);
            try
            {
                MultiplayerRoom? room;
                string? previous = null;
                do
                {
                    if (run.IsCompleted) await run;
                    await Task.Delay(100, ct);
                    room = client.Multiplayer.Get();
                    var summary = room is null ? "no-room" : $"{room.State}/{room.ErrorCode}/{room.GamePort}";
                    if (summary != previous) { TestContext.WriteLine(summary); previous = summary; }
                } while (room?.State != "connected");
                Assert.AreEqual(game.Port, room.GamePort);
                AssertStableComponent(paths, room);
                Assert.IsNotNull(RoomCode.Parse(room.Invitation!));
                Assert.IsFalse((await File.ReadAllTextAsync(Path.Combine(paths.Root, "multiplayer", "room.json"), ct)).Contains("U/", StringComparison.Ordinal));
                var second = client.Multiplayer.RunAsync(request with { RoomId = Guid.NewGuid().ToString("N") }, ct);
                var failure = await Assert.ThrowsAsync<LauncherOperationException>(() => second);
                Assert.AreEqual("MULTIPLAYER_ROOM_ACTIVE", failure.Code);
                var originalCode = room.Invitation;
                var originalWorker = room.ComponentProcessId;
                using (var worker = Process.GetProcessById(originalWorker!.Value))
                {
                    Assert.AreEqual(room.ComponentStartedAt, worker.StartTime.ToUniversalTime().ToString("O"));
                    worker.Kill(entireProcessTree: true);
                    await worker.WaitForExitAsync(ct);
                }

                do
                {
                    if (run.IsCompleted) await run;
                    await Task.Delay(100, ct);
                    room = client.Multiplayer.Get()!;
                    var summary = $"recovery/{room.State}/{room.ErrorCode}/{room.ComponentProcessId}";
                    if (summary != previous) { TestContext.WriteLine(summary); previous = summary; }
                } while (room.State != "connected" || room.ComponentProcessId == originalWorker);
                Assert.AreEqual(originalCode, room.Invitation);
                Assert.AreEqual(game.Port, room.GamePort);
                Assert.AreEqual(sessionId, room.SessionId);
                AssertStableComponent(paths, room);
                var report = Path.Combine(paths.Root, "connection.txt");
                await client.Multiplayer.ExportLogAsync(room.Id, report, ct);
                Assert.IsFalse((await File.ReadAllTextAsync(report, ct)).Contains(room.Invitation!, StringComparison.Ordinal));
                await client.Multiplayer.ControlAsync(room.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
                Assert.AreEqual("running", client.Sessions.Get(sessionId).Status);
                Assert.IsFalse(process.HasExited);
                // Joining a different room after leaving must retain the same already running game.
                var guestId = Guid.NewGuid().ToString("N");
                var guest = client.Multiplayer.RunAsync(request with { RoomId = guestId, Role = "guest", SessionId = null, StartGame = false, Invitation = originalCode }, ct);
                try
                {
                    do
                    {
                        if (guest.IsCompleted) await guest;
                        await Task.Delay(100, ct);
                        room = client.Multiplayer.Get()!;
                    } while (room.Id != guestId || room.SessionId is null);
                    Assert.AreEqual(sessionId, room.SessionId, "A new guest room must offer the existing game, rather than another launch.");
                    await client.Multiplayer.ControlAsync(guestId, "stop", ct: ct);
                    Assert.AreEqual("stopped", (await guest.WaitAsync(ct)).State);
                }
                finally
                {
                    if (!guest.IsCompleted) await client.Multiplayer.ControlAsync(guestId, "stop", ct: ct);
                    await guest;
                }
            }
            finally
            {
                cancellation.Cancel();
                try { await run; }
                catch (OperationCanceledException) { }
            }
        });
    }

    /// <summary>
    /// Review: a reused game's LAN listener can be discovered before its queued publication receipt completes.
    /// The real room loop must keep observing that receipt and publish open, with one mutation dispatch.
    /// This complements Java's native-call tests: a synthetic authenticated bridge controls the delayed
    /// receipt while the real component, TCP ownership, durable room and stop path remain active.
    /// </summary>
    [TestMethod]
    [TestCategory("Live")]
    public async Task ExistingLan_DelayedPublicationReceipt_UpdatesWorldWithoutReplay()
    {
        if (Environment.GetEnvironmentVariable("RODEL_MULTIPLAYER_LIVE") != "1")
            Assert.Inconclusive("Set RODEL_MULTIPLAYER_LIVE=1 to run the bundled network component.");
        await ReportStorage.RunAsync(async paths =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(60));
            var ct = cancellation.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Existing LAN", "1.21.1", ct);
            await using var bridge = await BridgeFixture.CreateAsync(ct, paths.Root, instance.Id);
            await using var game = new StatusListener();
            var edits = 0;
            var reads = 0;
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
                    result = JsonSerializer.SerializeToElement(new GameQueryResult { SessionId = bridge.Session, GameIncarnation = bridge.Incarnation, WorldEpoch = "1", Dimension = "minecraft:overworld", Kind = "world", Source = "synthetic_server", SampledAt = DateTimeOffset.UtcNow.ToString("O"), Data = JsonSerializer.SerializeToElement(new JsonObject { ["name"] = "Existing world" }) }, GameBridgeJsonContext.Default.GameQueryResult);
                }
                else if (request.Method is "game.edit" or "game.operation.show")
                {
                    if (request.Method == "game.edit") { Interlocked.Increment(ref edits); operationId = request.Params!.Value.GetProperty("operationId").GetString(); }

                    var applied = request.Method == "game.operation.show" && Interlocked.Increment(ref reads) >= 2
                        && client.Multiplayer.Get()?.GamePort == game.Port;
                    result = JsonSerializer.SerializeToElement(new GameOperation { OperationId = operationId!, State = applied ? "applied" : "queued", Result = applied ? JsonSerializer.SerializeToElement(new JsonObject { ["port"] = game.Port, ["reused"] = true }) : null }, GameBridgeJsonContext.Default.GameOperation);
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
                MultiplayerRoom? room;
                do
                {
                    if (run.IsCompleted) await run;
                    await Task.Delay(100, ct);
                    room = client.Multiplayer.Get();
                } while (room?.World?.State != "open");
                Assert.AreEqual(game.Port, room.GamePort);
                Assert.AreEqual("Existing world", room.World.Name);
                Assert.AreEqual(1, edits, "Discovering an existing port must not cause a second publication.");
                Assert.IsGreaterThanOrEqualTo(2, reads, "The late receipt must be observed after early listener discovery.");
                // World exit must finish the owner loop and release the room without a second stop action.
                var sessionFile = Path.Combine(paths.SessionsDirectory, bridge.Session + ".json");
                var stopped = JsonFiles.ReadObject(sessionFile);
                stopped["status"] = "exited";
                await JsonFiles.WriteAsync(sessionFile, stopped, ct);
                do { await Task.Delay(100, ct); room = client.Multiplayer.Get()!; } while (room.State != "closed");
                Assert.AreEqual("unknown", room.World!.State);
                Assert.AreEqual("closed", (await run.WaitAsync(ct)).State);
                Assert.IsFalse(client.Multiplayer.Get()!.Active);
            }
            finally
            {
                cancellation.Cancel();
                try { await run; } catch (OperationCanceledException) { }
            }
        });
    }

    internal sealed class StatusListener : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly Task _broadcast;
        private readonly string? _legacy;
        internal StatusListener(int port = 0, string? legacy = null) { _legacy = legacy; _listener = new(IPAddress.Loopback, port); _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _loop = ServeAsync(); _broadcast = BroadcastAsync(); }
        internal int Port { get; }
        private async Task BroadcastAsync()
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { MulticastLoopback = true };
            var target = new IPEndPoint(IPAddress.Parse("224.0.2.60"), 4445);
            var message = Encoding.UTF8.GetBytes($"[MOTD]Protocol fixture[/MOTD][AD]{Port}[/AD]");
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await udp.SendAsync(message, target, _stop.Token);
                    await Task.Delay(500, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    try
                    {
                        var stream = connection.GetStream();
                        var input = new byte[2048];
                        if (await stream.ReadAsync(input, _stop.Token) == 0) continue;
                        if (_legacy is not null)
                        {
                            if (input[0] != 0xfe) continue;
                            var text = Encoding.BigEndianUnicode.GetBytes(_legacy);
                            await stream.WriteAsync(new byte[] { 0xff, (byte)(_legacy.Length >> 8), (byte)_legacy.Length }.Concat(text).ToArray(), _stop.Token);
                            continue;
                        }

                        var json = Encoding.UTF8.GetBytes("{\"version\":{\"name\":\"1.21.1\",\"protocol\":767},\"players\":{\"max\":8,\"online\":0},\"description\":{\"text\":\"Protocol fixture\"}}");
                        var length = Encode(json.Length);
                        byte[] response = [.. Encode(1 + length.Length + json.Length), 0, .. length, .. json];
                        await stream.WriteAsync(response, _stop.Token);
                    }
                    catch (IOException) { }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        private static byte[] Encode(int number)
        {
            var bytes = new List<byte>();
            do { var value = (byte)(number & 127); number >>= 7; bytes.Add((byte)(value | (number > 0 ? 128 : 0))); } while (number > 0);
            return [.. bytes];
        }
        /// <summary>Stops only the fixture listener and waits for its owned loop.</summary>
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await Task.WhenAll(_loop, _broadcast); _listener.Dispose(); _stop.Dispose(); }
    }

    private static void AssertStableComponent(LauncherPaths paths, MultiplayerRoom room)
    {
        using var worker = Process.GetProcessById(room.ComponentProcessId!.Value);
        Assert.AreEqual(Path.Combine(TerracottaRuntime.DirectoryPath, "terracotta.exe"), worker.MainModule!.FileName, true);
        var component = ResourceLease.CanonicalPath(Path.Combine(paths.Root, "multiplayer", "component")).ToUpperInvariant();
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(component)))[..20];
        var root = Path.Combine(Path.GetTempPath(), "rmcl-net", key, "terracotta");
        var working = Directory.EnumerateDirectories(root, "*-" + worker.Id).OrderByDescending(Directory.GetCreationTimeUtc).First();
        var alias = Path.Combine(working, "embedded-easytier", "easytier-core.exe");
        Assert.AreEqual(Path.Combine(TerracottaRuntime.DirectoryPath, "engine", "easytier-core.exe"), ResourceLease.CanonicalPath(alias), true);
    }
}