// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Tests;

internal sealed class BridgeFixture : IAsyncDisposable
{
    private readonly GameBridgeOwner _owner;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly bool _ownsRoot;
    internal LauncherPaths Paths { get; }
    internal string Session { get; } = Guid.NewGuid().ToString("N");
    internal string Incarnation { get; }
    internal string Secret { get; }
    internal int Port { get; }
    internal GameBridgeService Service => new(Paths);
    private BridgeFixture(string? root)
    {
        _ownsRoot = root is null;
        Paths = new(root ?? Path.Combine(Path.GetTempPath(), "Rodel.Core.Tests", "bridge " + Guid.NewGuid().ToString("N")));
        Paths.EnsureDirectories();
        _owner = new(Paths, Session);
        var start = new ProcessStartInfo();
        _owner.Configure(start, "synthetic-bindings");
        Secret = start.Environment["RODEL_BRIDGE_SECRET"]!;
        Incarnation = start.Environment["RODEL_BRIDGE_INCARNATION"]!;
        Port = int.Parse(start.Environment["RODEL_BRIDGE_PORT"]!, System.Globalization.CultureInfo.InvariantCulture);
        _owner.Bind(_process);
    }
    internal static async Task<BridgeFixture> CreateAsync(CancellationToken ct, string? root = null, string? instanceId = null)
    {
        var fixture = new BridgeFixture(root);
        await JsonFiles.WriteAsync(Path.Combine(fixture.Paths.SessionsDirectory, fixture.Session + ".json"), new JsonObject
        {
            ["id"] = fixture.Session,
            ["instanceId"] = instanceId,
            ["status"] = "running",
            ["gameBridgeEnabled"] = true,
            ["processId"] = fixture._process.Id,
            ["processStartUtc"] = fixture._process.StartTime.ToUniversalTime().ToString("O"),
            ["monitorId"] = fixture._process.Id,
            ["monitorStartUtc"] = fixture._process.StartTime.ToUniversalTime().ToString("O")
        }, ct);
        return fixture;
    }
    internal GameEditRequest Edit() => new() { OperationId = Guid.NewGuid().ToString("N"), GameIncarnation = Incarnation, WorldEpoch = "1", Dimension = "minecraft:overworld", Kind = "player.setHealth", Health = 13 };
    internal BridgeRpc Response(BridgeRpc request, string mode = "local_owned") => request.Method == "bridge.capabilities" ? new()
    {
        Id = request.Id,
        Result = JsonSerializer.SerializeToElement(new BridgeCapabilities { Capabilities = ["game.snapshot", "game.query", "game.region", "world.setTime", "player.setHealth", "player.control", "player.release", "inventory.click", "inventory.button", "screen.close", "player.respawn", "command.execute", "game.operation.show", "game.operation.cancel"] }, BridgeWireJsonContext.Default.BridgeCapabilities)
    } : request.Method == "game.snapshot" ? new()
    {
        Id = request.Id,
        Result = JsonSerializer.SerializeToElement(new GameSnapshot { SessionId = Session, GameIncarnation = Incarnation, WorldEpoch = "1", Dimension = "minecraft:overworld", Ready = true, Mode = mode, Sequence = "1", SampledAt = DateTimeOffset.UtcNow.ToString("O"), Health = 20 }, GameBridgeJsonContext.Default.GameSnapshot)
    } : new() { Id = request.Id, Error = new() { Code = -32000, Message = "GAME_OPERATION_NOT_FOUND" } };

    internal async Task<BridgePeer> ConnectAsync(Func<BridgeRpc, BridgeRpc?> respond, CancellationToken ct)
    {
        var peer = await BridgePeer.ConnectAsync(this, respond, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while ((await Service.GetStatusAsync(Session, deadline.Token)).State != "connected") await Task.Delay(20, deadline.Token);
        return peer;
    }

    /// <summary>Releases the real monitor transports and isolated test data after all peers have stopped.</summary>
    public async ValueTask DisposeAsync()
    {
        await _owner.DisposeAsync();
        _process.Dispose();
        if (_ownsRoot)
        {
            var cleanup = Stopwatch.StartNew();
            while (true)
            {
                try { Directory.Delete(Paths.Root, true); break; }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && cleanup.Elapsed < TimeSpan.FromSeconds(2))
                {
                    // All owned work is drained above; Windows observers may briefly retain a file
                    // handle. Retry only this observed sharing lock, never the test or its assertions.
                    await Task.Delay(20);
                }
            }
        }
    }
}

internal sealed class BridgePeer(TcpClient client, Task reader) : IAsyncDisposable
{
    internal static async Task<BridgePeer> ConnectAsync(BridgeFixture fixture, Func<BridgeRpc, BridgeRpc?> respond, CancellationToken ct)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", fixture.Port, ct);
        var stream = client.GetStream();
        var hello = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeHandshake, "TEST", ct);
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string Proof(string role) => Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromHexString(fixture.Secret), Encoding.UTF8.GetBytes("rodel-bridge-v1\n" + role + "\n" + fixture.Session + "\n" + fixture.Incarnation + "\n" + hello.Challenge + "\n" + nonce)));
        await FramedJsonPipe.WriteAsync(stream, new BridgeHandshake { SessionId = fixture.Session, Challenge = nonce, Proof = Proof("agent") }, BridgeWireJsonContext.Default.BridgeHandshake, "TEST", ct);
        var accepted = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeHandshake, "TEST", ct);
        Assert.AreEqual(Proof("host"), accepted.Proof);
        var reader = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var request = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeRpc, "TEST", ct);
                    var response = respond(request);
                    if (response is null) { client.Dispose(); return; }

                    await FramedJsonPipe.WriteAsync(stream, response, BridgeWireJsonContext.Default.BridgeRpc, "TEST", ct);
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
        }, CancellationToken.None);
        return new(client, reader);
    }
    /// <summary>Disconnects only this synthetic peer and drains its response worker.</summary>
    public async ValueTask DisposeAsync() { client.Dispose(); await reader; }
}