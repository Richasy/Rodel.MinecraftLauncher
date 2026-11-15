// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

internal sealed class GameBridgeOwner : IAsyncDisposable
{
    private readonly LauncherPaths _paths;
    private readonly string _session;
    private readonly string _incarnation = Guid.NewGuid().ToString("N");
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _controlSlots = new(8, 8);
    private readonly Task _accept;
    private readonly Task _control;
    private readonly TaskCompletionSource<Process> _process = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile BridgeConnection? _connection;
    private volatile string? _error;
    private volatile string[] _capabilities = [];
    private readonly HashSet<Task> _controls = [];
    private int _retainedEdits;
    private readonly GameAiSession _ai;

    internal GameBridgeOwner(LauncherPaths paths, string session)
    {
        _paths = paths;
        _session = session;
        _ai = new(paths, session);
        _listener.Start(4);
        _accept = AcceptAsync();
        _control = ControlAsync();
    }

    internal void Configure(ProcessStartInfo start, string bindings, string? inputHooks = null)
    {
        start.Environment["RODEL_BRIDGE_PORT"] = ((IPEndPoint)_listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["RODEL_BRIDGE_SECRET"] = Convert.ToHexStringLower(_secret);
        start.Environment["RODEL_BRIDGE_SESSION"] = _session;
        start.Environment["RODEL_BRIDGE_INCARNATION"] = _incarnation;
        start.Environment["RODEL_BRIDGE_BINDINGS"] = bindings;
        start.Environment["RODEL_BRIDGE_INPUT_HOOKS"] = inputHooks ?? string.Empty;
    }

    internal void Bind(Process process) => _process.TrySetResult(process);

    private async Task AcceptAsync()
    {
        while (!_closed.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_closed.Token).ConfigureAwait(false);
                client.NoDelay = true;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                var process = await _process.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (!ProcessAlive(process)) break;
                var stream = client.GetStream();
                var challenge = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                await FramedJsonPipe.WriteAsync(stream, new BridgeHandshake { SessionId = _session, Challenge = challenge }, BridgeWireJsonContext.Default.BridgeHandshake, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false);
                var reply = await FramedJsonPipe.ReadAsync(stream, BridgeWireJsonContext.Default.BridgeHandshake, "GAME_BRIDGE", deadline.Token, maximumFrameBytes: 4096).ConfigureAwait(false);
                if (reply.Version != 1 || reply.SessionId != _session || reply.Challenge.Length != 64 || !reply.Challenge.All(Uri.IsHexDigit)
                    || !ValidProof(reply.Proof, "agent", challenge, reply.Challenge))
                {
                    throw new InvalidDataException("Invalid bridge authentication.");
                }

                await FramedJsonPipe.WriteAsync(stream, new BridgeHandshake { SessionId = _session, Challenge = reply.Challenge, Proof = Proof("host", challenge, reply.Challenge) }, BridgeWireJsonContext.Default.BridgeHandshake, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false);
                var connection = new BridgeConnection(stream, ReceiveGameRequestAsync);
                await using var connectionLifetime = connection.ConfigureAwait(false);
                connection.Start();
                var capabilities = (await connection.CallAsync("bridge.capabilities", null, deadline.Token).ConfigureAwait(false)).Deserialize(BridgeWireJsonContext.Default.BridgeCapabilities)
                    ?? throw new InvalidDataException("Missing bridge capabilities.");
                _capabilities = capabilities.Capabilities;
                _connection = connection;
                _error = null;
                while (connection.Connected && !_closed.IsCancellationRequested && ProcessAlive(process))
                    await Task.Delay(200, _closed.Token).ConfigureAwait(false);
                _connection = null;
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or JsonException or InvalidDataException or LauncherOperationException)
            {
                _error = error is OperationCanceledException ? "GAME_BRIDGE_HANDSHAKE_TIMEOUT" : "GAME_BRIDGE_CONNECTION_FAILED";
                if (!_closed.IsCancellationRequested) await Task.Delay(200, _closed.Token).ConfigureAwait(false);
            }
        }
    }

    private static bool ProcessAlive(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private string Proof(string role, string host, string agent) => Convert.ToHexStringLower(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes("rodel-bridge-v1\n" + role + "\n" + _session + "\n" + _incarnation + "\n" + host + "\n" + agent)));
    private bool ValidProof(string? proof, string role, string host, string agent) => proof?.Length == 64 && proof.All(Uri.IsHexDigit)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(proof), Convert.FromHexString(Proof(role, host, agent)));

    private async Task ControlAsync()
    {
        while (!_closed.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                await _controlSlots.WaitAsync(_closed.Token).ConfigureAwait(false);
                pipe = new NamedPipeServerStream(GameBridgeService.PipeName(_paths, _session), PipeDirection.InOut, 9, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_closed.Token).ConfigureAwait(false);
                lock (_controls)
                {
                    _controls.RemoveWhere(task => task.IsCompleted);
                    _controls.Add(HandleControlAsync(pipe));
                }

                pipe = null;
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                if (_closed.IsCancellationRequested) break;
                _controlSlots.Release();
                await Task.Delay(200, _closed.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleControlAsync(NamedPipeServerStream pipe)
    {
        await using var disposal = pipe.ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var request = await FramedJsonPipe.ReadAsync(pipe, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false);
            if (request.Jsonrpc != "2.0" || request.Id.Length is 0 or > 64 || request.Method is null || request.Result is not null || request.Error is not null) return;
            BridgeRpc response;
            try { response = new() { Id = request.Id, Result = await DispatchAsync(request, deadline.Token).ConfigureAwait(false) }; }
            catch (Exception error) when (error is LauncherOperationException or ArgumentException or IOException or JsonException or OperationCanceledException)
            {
                response = new() { Id = request.Id, Error = new() { Code = -32000, Message = error is LauncherOperationException failure ? failure.Code : "GAME_BRIDGE_REQUEST_FAILED" } };
            }

            await FramedJsonPipe.WriteAsync(pipe, response, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or LauncherOperationException) { }
        finally { _controlSlots.Release(); }
    }

    private async Task<JsonElement> DispatchAsync(BridgeRpc request, CancellationToken ct)
    {
        if (request.Method == "launcher.ai") return await ReceiveGameRequestAsync(request, ct).ConfigureAwait(false);
        if (request.Method == "bridge.status")
            return JsonSerializer.SerializeToElement(new GameBridgeStatus { SessionId = _session, State = _connection?.Connected == true ? "connected" : "disconnected", GameIncarnation = _incarnation, ErrorCode = _error, Capabilities = _connection?.Connected == true ? _capabilities : [] }, GameBridgeJsonContext.Default.GameBridgeStatus);
        var connection = _connection is { Connected: true } current ? current : throw new LauncherOperationException("GAME_BRIDGE_DISCONNECTED", "The game bridge is not connected.");
        switch (request.Method)
        {
            case "game.snapshot":
                var snapshot = (await connection.CallAsync("game.snapshot", null, ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameSnapshot)
                    ?? throw new InvalidDataException("Missing game snapshot.");
                if (snapshot.SessionId != _session || snapshot.GameIncarnation != _incarnation) throw new InvalidDataException("Game identity mismatch.");
                return JsonSerializer.SerializeToElement(snapshot, GameBridgeJsonContext.Default.GameSnapshot);
            case "game.edit":
                var edit = request.Params?.Deserialize(GameBridgeJsonContext.Default.GameEditRequest) ?? throw new ArgumentException("A typed edit is required.");
                return JsonSerializer.SerializeToElement(await EditAsync(connection, edit, ct).ConfigureAwait(false), GameBridgeJsonContext.Default.GameOperation);
            case "game.region":
                var region = (await connection.CallAsync("game.region", request.Params, ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameRegion)
                    ?? throw new InvalidDataException("Missing game region.");
                if (region.SessionId != _session || region.GameIncarnation != _incarnation) throw new InvalidDataException("Game identity mismatch.");
                return JsonSerializer.SerializeToElement(region, GameBridgeJsonContext.Default.GameRegion);
            case "game.query":
                var query = (await connection.CallAsync("game.query", request.Params, ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameQueryResult)
                    ?? throw new InvalidDataException("Missing semantic game observation.");
                if (query.SessionId != _session || query.GameIncarnation != _incarnation) throw new InvalidDataException("Game identity mismatch.");
                return JsonSerializer.SerializeToElement(query, GameBridgeJsonContext.Default.GameQueryResult);
            case "game.operation.show":
            case "game.operation.cancel":
                var target = request.Params?.Deserialize(BridgeWireJsonContext.Default.BridgeOperationTarget) ?? throw new ArgumentException("An operation is required.");
                GameBridgeService.ValidateId(target.OperationId);
                var operation = await connection.CallAsync(request.Method, request.Params, ct).ConfigureAwait(false);
                var recorded = await RecordReceiptAsync(target.OperationId, operation.Deserialize(GameBridgeJsonContext.Default.GameOperation)!, ct).ConfigureAwait(false);
                return JsonSerializer.SerializeToElement(recorded, GameBridgeJsonContext.Default.GameOperation);
            default: throw new LauncherOperationException("GAME_BRIDGE_METHOD_UNKNOWN", "This bridge operation is not supported.");
        }
    }

    private async Task<JsonElement> ReceiveGameRequestAsync(BridgeRpc request, CancellationToken ct)
    {
        if (request.Method != "launcher.ai") throw new LauncherOperationException("GAME_BRIDGE_METHOD_UNKNOWN", "Only the game AI ingress is available in this direction.");
        var input = request.Params?.Deserialize(GameAiJsonContext.Default.GameAiRequest) ?? throw new ArgumentException("A typed game AI request is required.");
        return JsonSerializer.SerializeToElement(await _ai.HandleAsync(input, ct).ConfigureAwait(false), GameAiJsonContext.Default.GameAiState);
    }

    private async Task<GameOperation> EditAsync(BridgeConnection connection, GameEditRequest edit, CancellationToken ct)
    {
        GameBridgeService.ValidateEdit(edit);
        var file = GameBridgeService.JournalPath(_paths, _session, edit.OperationId);
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        var fingerprint = GameBridgeService.RequestFingerprint(edit);
        if (File.Exists(file))
        {
            var existing = await GameBridgeService.ReadJournalAsync(file, ct).ConfigureAwait(false);
            if (existing.Fingerprint != fingerprint) throw new LauncherOperationException("GAME_OPERATION_CONFLICT", "This operation ID already belongs to a different edit.");
            if (existing.Operation.State is "applied" or "rejected" or "canceled") return existing.Operation;
            // A persisted intent is evidence of an attempted submission, never permission to replay it.
            try
            {
                var target = JsonSerializer.SerializeToElement(new BridgeOperationTarget { OperationId = edit.OperationId }, BridgeWireJsonContext.Default.BridgeOperationTarget);
                var observed = (await connection.CallAsync("game.operation.show", target, ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameOperation)!;
                await GameBridgeService.WriteJournalAsync(file, existing with { Operation = observed }, ct).ConfigureAwait(false);
                return observed;
            }
            catch (LauncherOperationException) { return existing.Operation with { State = "unknown", ErrorCode = "GAME_OPERATION_OUTCOME_UNKNOWN" }; }
        }

        var snapshot = (await connection.CallAsync("game.snapshot", null, ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameSnapshot)!;
        var rejection = snapshot.Mode == "remote_read_only" ? "SERVER_READ_ONLY" : !snapshot.Ready ? "GAME_WORLD_NOT_READY"
            : snapshot.Mode != "local_owned" ? "GAME_WORLD_NOT_LOCAL" : snapshot.GameIncarnation != edit.GameIncarnation || snapshot.WorldEpoch != edit.WorldEpoch || snapshot.Dimension != edit.Dimension ? "GAME_WORLD_STALE" : null;
        if (rejection is null && !_capabilities.Contains(edit.Kind, StringComparer.Ordinal)) rejection = "GAME_OPERATION_UNSUPPORTED";
        if (rejection is null && edit.Kind == "command.execute")
        {
            var target = new GameQueryRequest { GameIncarnation = edit.GameIncarnation, WorldEpoch = edit.WorldEpoch, Dimension = edit.Dimension, Kind = "world" };
            var permission = (await connection.CallAsync("game.query", JsonSerializer.SerializeToElement(target, GameBridgeJsonContext.Default.GameQueryRequest), ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameQueryResult)!;
            rejection = permission.Data.TryGetProperty("allowCommands", out var allowed) && allowed.ValueKind is JsonValueKind.True ? null : "GAME_CHEATS_DISABLED";
        }

        var receipt = new GameOperation { OperationId = edit.OperationId, State = rejection is null ? "unknown" : "rejected", ErrorCode = rejection ?? "GAME_OPERATION_OUTCOME_UNKNOWN" };
        var entry = new BridgeJournalEntry { Fingerprint = fingerprint, Request = edit, Operation = receipt };
        if (Interlocked.Increment(ref _retainedEdits) > GameBridgeLimits.MaximumRetainedOperations)
        {
            Interlocked.Decrement(ref _retainedEdits);
            throw new LauncherOperationException("GAME_OPERATION_LIMIT", "This game session reached its retained edit limit.");
        }

        try { await GameBridgeService.WriteJournalAsync(file, entry, ct).ConfigureAwait(false); }
        catch { Interlocked.Decrement(ref _retainedEdits); throw; }

        if (rejection is not null) return receipt;
        try
        {
            receipt = (await connection.CallAsync("game.edit", JsonSerializer.SerializeToElement(edit, GameBridgeJsonContext.Default.GameEditRequest), ct).ConfigureAwait(false)).Deserialize(GameBridgeJsonContext.Default.GameOperation)!;
            if (receipt.OperationId != edit.OperationId) throw new InvalidDataException("Operation identity mismatch.");
            await GameBridgeService.WriteJournalAsync(file, entry with { Operation = receipt }, CancellationToken.None).ConfigureAwait(false);
            return receipt;
        }
        catch (LauncherOperationException error) when (error.Code is "GAME_BRIDGE_BUSY" or "GAME_OPERATION_LIMIT" or "INVALID_PARAMS" or "GAME_BRIDGE_METHOD_UNKNOWN")
        {
            receipt = receipt with { State = "rejected", ErrorCode = error.Code };
            await GameBridgeService.WriteJournalAsync(file, entry with { Operation = receipt }, CancellationToken.None).ConfigureAwait(false);
            return receipt;
        }
        catch (Exception error) when (error is LauncherOperationException or IOException or OperationCanceledException or JsonException)
        {
            return receipt;
        }
    }

    private async Task<GameOperation> RecordReceiptAsync(string id, GameOperation receipt, CancellationToken ct)
    {
        var file = GameBridgeService.JournalPath(_paths, _session, id);
        if (receipt.OperationId != id) throw new InvalidDataException("Operation identity mismatch.");
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        if (File.Exists(file))
        {
            var existing = await GameBridgeService.ReadJournalAsync(file, ct).ConfigureAwait(false);
            if (existing.Operation.State is "applied" or "rejected" or "canceled" || existing.Operation == receipt) return existing.Operation;
            await GameBridgeService.WriteJournalAsync(file, existing with { Operation = receipt }, ct).ConfigureAwait(false);
        }

        return receipt;
    }

    /// <summary>Closes listeners and revokes this monitor's bridge credentials; it never terminates Java.</summary>
    public async ValueTask DisposeAsync()
    {
        _closed.Cancel();
        _listener.Stop();
        try
        {
            if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
            try { await Task.WhenAll(_accept, _control).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }

            Task[] controls;
            lock (_controls) controls = [.. _controls];
            await Task.WhenAll(controls).ConfigureAwait(false);
        }
        finally
        {
            try { await _ai.StopAsync().ConfigureAwait(false); }
            finally
            {
                _listener.Dispose();
                _controlSlots.Dispose();
                _closed.Dispose();
                CryptographicOperations.ZeroMemory(_secret);
            }
        }
    }
}