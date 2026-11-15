// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32.SafeHandles;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Observes and edits explicitly selected running games through their actual Core session monitors.</summary>
public sealed partial class GameBridgeService(LauncherPaths paths)
{
    /// <summary>Uses the running game's Core monitor to send or observe a game-scoped AI request with launcher-owned configuration.</summary>
    public Task<GameAiState> RequestAiAsync(string sessionId, GameAiRequest request, CancellationToken ct = default)
        => CallAsync(sessionId, "launcher.ai", JsonSerializer.SerializeToElement(request, GameAiJsonContext.Default.GameAiRequest), GameAiJsonContext.Default.GameAiState, ct);
    internal static string PipeName(LauncherPaths paths, string session) => "rodel-game-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ResourceLease.CanonicalPath(paths.Root).ToUpperInvariant())))[..24] + "-" + session;

    /// <summary>Reads bridge connectivity without treating a visible window or retained data as a live world.</summary>
    public async Task<GameBridgeStatus> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        ValidateId(sessionId);
        var record = JsonFiles.ReadObject(Path.Combine(paths.SessionsDirectory, sessionId + ".json"));
        if (record["gameBridgeEnabled"]?.GetValue<bool>() != true) return new() { SessionId = sessionId, State = "disabled" };
        if (record["gameBridgeError"]?.GetValue<string>() is { } error) return new() { SessionId = sessionId, State = "unavailable", ErrorCode = error };
        try { return await CallAsync(sessionId, "bridge.status", null, GameBridgeJsonContext.Default.GameBridgeStatus, ct).ConfigureAwait(false); }
        catch (LauncherOperationException failure) { return new() { SessionId = sessionId, State = "disconnected", ErrorCode = failure.Code }; }
    }

    /// <summary>Samples synchronized player/world data on Minecraft's client thread; no stale fallback is returned.</summary>
    public Task<GameSnapshot> SnapshotAsync(string sessionId, CancellationToken ct = default)
        => CallAsync(sessionId, "game.snapshot", null, GameBridgeJsonContext.Default.GameSnapshot, ct);

    /// <summary>Queries native game state through a semantic family; remote observations remain client-synchronized.</summary>
    public Task<GameQueryResult> QueryAsync(string sessionId, GameQueryRequest request, CancellationToken ct = default)
    {
        ValidateId(request.GameIncarnation);
        if (!long.TryParse(request.WorldEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch) || epoch <= 0 || epoch.ToString(CultureInfo.InvariantCulture) != request.WorldEpoch
            || string.IsNullOrWhiteSpace(request.Dimension) || request.Dimension.Length > 256 || !GameBridgeLimits.QueryKinds.Contains(request.Kind, StringComparer.Ordinal)
            || request.Offset < 0 || request.Limit is < 1 or > GameBridgeLimits.MaximumQueryItems || request.Resource?.Length > 256 || request.Search?.Length > 256 || request.NbtPath?.Length > 1024)
        {
            throw new ArgumentException("A bounded semantic query and current world identity are required.");
        }

        if (request.EntityId is not null && !Guid.TryParse(request.EntityId, out _)) throw new ArgumentException("An entity UUID is required.");
        if (request.Kind == "block" && (request.X is null || request.Y is null || request.Z is null
            || Math.Abs((long)request.X.Value) > GameBridgeLimits.MaximumHorizontalCoordinate || Math.Abs((long)request.Z.Value) > GameBridgeLimits.MaximumHorizontalCoordinate
            || Math.Abs((long)request.Y.Value) > GameBridgeLimits.MaximumVerticalCoordinate))
        {
            throw new ArgumentException("Bounded block coordinates are required.");
        }

        return CallAsync(sessionId, "game.query", JsonSerializer.SerializeToElement(request, GameBridgeJsonContext.Default.GameQueryRequest), GameBridgeJsonContext.Default.GameQueryResult, ct);
    }

    /// <summary>Reads bounded blocks, nearby visible entities and inventory summaries without forcing chunk loading.</summary>
    public Task<GameRegion> ReadRegionAsync(string sessionId, GameRegionRequest request, CancellationToken ct = default)
    {
        ValidateId(request.GameIncarnation);
        if (request.SizeX is < 1 or > GameBridgeLimits.MaximumRegionEdge || request.SizeY is < 1 or > GameBridgeLimits.MaximumRegionEdge || request.SizeZ is < 1 or > GameBridgeLimits.MaximumRegionEdge
            || Math.Abs((long)request.X) > GameBridgeLimits.MaximumHorizontalCoordinate || Math.Abs((long)request.Z) > GameBridgeLimits.MaximumHorizontalCoordinate || Math.Abs((long)request.Y) > GameBridgeLimits.MaximumVerticalCoordinate)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        return CallAsync(sessionId, "game.region", JsonSerializer.SerializeToElement(request, GameBridgeJsonContext.Default.GameRegionRequest), GameBridgeJsonContext.Default.GameRegion, ct);
    }

    /// <summary>Submits a typed local edit once, recording intent before transport; a lost outcome is never replayed.</summary>
    public Task<GameOperation> EditAsync(string sessionId, GameEditRequest request, CancellationToken ct = default)
    {
        ValidateEdit(request);
        return CallAsync(sessionId, "game.edit", JsonSerializer.SerializeToElement(request, GameBridgeJsonContext.Default.GameEditRequest), GameBridgeJsonContext.Default.GameOperation, ct);
    }

    /// <summary>Reads the live operation receipt, or an explicit unknown result when its monitor has disappeared.</summary>
    public async Task<GameOperation> GetOperationAsync(string sessionId, string operationId, CancellationToken ct = default)
    {
        ValidateId(sessionId);
        ValidateId(operationId);
        try { return await OperationAsync(sessionId, operationId, "game.operation.show", ct).ConfigureAwait(false); }
        catch (LauncherOperationException)
        {
            var entry = await ReadJournalAsync(JournalPath(paths, sessionId, operationId), ct).ConfigureAwait(false);
            return entry.Operation.State is "queued" or "applying" or "unknown" ? entry.Operation with { State = "unknown", ErrorCode = "GAME_OPERATION_OUTCOME_UNKNOWN" } : entry.Operation;
        }
    }

    /// <summary>Cancels only an edit that has not begun execution; applied edits retain their actual receipt.</summary>
    public Task<GameOperation> CancelOperationAsync(string sessionId, string operationId, CancellationToken ct = default)
        => OperationAsync(sessionId, operationId, "game.operation.cancel", ct);

    /// <summary>Observes latest snapshots at a bounded rate with natural consumer backpressure and no unbounded event history.</summary>
    public async IAsyncEnumerable<GameSnapshot> WatchAsync(string sessionId, int intervalMilliseconds = 500, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (intervalMilliseconds is < 200 or > 10000) throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
        while (true)
        {
            yield return await SnapshotAsync(sessionId, ct).ConfigureAwait(false);
            await Task.Delay(intervalMilliseconds, ct).ConfigureAwait(false);
        }
    }

    private Task<GameOperation> OperationAsync(string session, string operation, string method, CancellationToken ct)
    {
        ValidateId(operation);
        return CallAsync(session, method, JsonSerializer.SerializeToElement(new BridgeOperationTarget { OperationId = operation }, BridgeWireJsonContext.Default.BridgeOperationTarget), GameBridgeJsonContext.Default.GameOperation, ct);
    }

    private async Task<T> CallAsync<T>(string session, string method, JsonElement? parameters, JsonTypeInfo<T> type, CancellationToken ct) where T : class
    {
        ValidateId(session);
        var state = new SessionService(paths).Get(session);
        if (state.Status != "running" || state.MonitorId is not { } monitor || state.MonitorStartUtc is not { } started)
            throw new LauncherOperationException("GAME_BRIDGE_UNAVAILABLE", "This session has no live game bridge monitor.");
        try
        {
            using var process = Process.GetProcessById(monitor);
            if (process.HasExited || process.StartTime.ToUniversalTime().ToString("O") != started)
                throw new LauncherOperationException("GAME_BRIDGE_UNAVAILABLE", "The recorded monitor is no longer running.");
        }
        catch (ArgumentException) { throw new LauncherOperationException("GAME_BRIDGE_UNAVAILABLE", "The recorded monitor is no longer running."); }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using var pipe = new NamedPipeClientStream(".", PipeName(paths, session), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(1000, deadline.Token).ConfigureAwait(false);
            if (OperatingSystem.IsWindows() && (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual) || actual != monitor))
                throw new LauncherOperationException("GAME_BRIDGE_IDENTITY_MISMATCH", "The pipe does not belong to this session's monitor.");
            var id = Guid.NewGuid().ToString("N");
            await FramedJsonPipe.WriteAsync(pipe, new BridgeRpc { Id = id, Method = method, Params = parameters }, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false);
            var response = await FramedJsonPipe.ReadAsync(pipe, BridgeWireJsonContext.Default.BridgeRpc, "GAME_BRIDGE", deadline.Token).ConfigureAwait(false);
            if (response.Id != id || response.Jsonrpc != "2.0" || response.Method is not null || (response.Result is null) == (response.Error is null)) throw new InvalidDataException("Bridge response identity mismatch.");
            if (response.Error is { } error) throw new LauncherOperationException(error.Message, "The game bridge could not complete this request.");
            return response.Result is { } result ? result.Deserialize(type) ?? throw new InvalidDataException("Missing bridge result.") : throw new InvalidDataException("Missing bridge result.");
        }
        catch (Exception error) when (error is IOException or TimeoutException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new LauncherOperationException("GAME_BRIDGE_UNAVAILABLE", "The game bridge disconnected or timed out. Observe the operation receipt before submitting another edit.");
        }
    }

    internal static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var parsed) || parsed.ToString("N") != id) throw new ArgumentException("A lowercase GUID in N format is required.");
    }

    internal static void ValidateEdit(GameEditRequest edit)
    {
        ValidateId(edit.OperationId);
        ValidateId(edit.GameIncarnation);
        if (!long.TryParse(edit.WorldEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch) || epoch <= 0 || epoch.ToString(CultureInfo.InvariantCulture) != edit.WorldEpoch || string.IsNullOrWhiteSpace(edit.Dimension) || edit.Dimension.Length > 256)
            throw new ArgumentException("An observed world generation and dimension are required.");
        var payloads = (edit.DayTime is null ? 0 : 1) + (edit.Health is null ? 0 : 1) + (edit.Command is null ? 0 : 1) + (edit.Control is null ? 0 : 1) + (edit.Click is null ? 0 : 1) + (edit.Button is null ? 0 : 1);
        if (!GameBridgeLimits.EditKinds.Contains(edit.Kind, StringComparer.Ordinal) || payloads != (edit.Kind is "player.release" or "screen.close" or "player.respawn" or "world.publish" ? 0 : 1))
            throw new ArgumentException("The operation must contain only its declared payload.");
        if (edit.Kind == "world.setTime")
        {
            if (edit.Health is not null || !long.TryParse(edit.DayTime, NumberStyles.None, CultureInfo.InvariantCulture, out var time) || time < 0 || time.ToString(CultureInfo.InvariantCulture) != edit.DayTime)
                throw new ArgumentException("Day time must be a nonnegative Int64 tick value.");
        }
        else if (edit.Kind == "player.setHealth")
        {
            if (edit.DayTime is not null || edit.Health is not { } health || !float.IsFinite(health) || health is < GameBridgeLimits.MinimumHealth or > GameBridgeLimits.MaximumHealth)
                throw new ArgumentException("Health must be finite and between 1 and 20.");
        }
        else if (edit.Kind == "command.execute")
        {
            if (string.IsNullOrWhiteSpace(edit.Command) || edit.Command.Length > 32767 || edit.Command.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new ArgumentException("A single bounded native command is required.");
        }
        else if (edit.Kind == "player.control")
        {
            if (edit.Control is not { } control || control.Keys is null || control.Keys.Length > GameBridgeLimits.ControlKeys.Count
                || control.Keys.Distinct(StringComparer.Ordinal).Count() != control.Keys.Length || control.Keys.Any(key => !GameBridgeLimits.ControlKeys.Contains(key, StringComparer.Ordinal))
                || control.Ticks is < 1 or > GameBridgeLimits.MaximumControlTicks || control.InteractionDelayTicks < 1 || control.InteractionDelayTicks > control.Ticks || control.Hotbar is < 0 or > 8
                || (control.Yaw is { } yaw && (!float.IsFinite(yaw) || yaw is < -360 or > 360))
                || (control.Pitch is { } pitch && (!float.IsFinite(pitch) || pitch is < -90 or > 90)))
            {
                throw new ArgumentException("The native input lease is outside its supported bounds.");
            }
        }
        else if (edit.Kind == "inventory.click")
        {
            if (edit.Click is not { } click || click.ContainerId < 0 || click.StateId < 0 || click.Slot is < -999 or > 511 || click.Slot is < 0 and not -999
                || !GameBridgeLimits.ClickModes.Contains(click.Mode, StringComparer.Ordinal) || click.Button is < 0 or > 40
                || (click.Mode == "SWAP" ? click.Button is > 8 and not 40 : click.Mode == "CLONE" ? click.Button != 2 : click.Mode == "QUICK_CRAFT" ? click.Button > 10 : click.Button > 1))
            {
                throw new ArgumentException("An observed native container click is required.");
            }
        }
        else if (edit.Kind == "inventory.button" && (edit.Button is not { } button || button.ContainerId < 0 || button.StateId < 0 || button.Button is < 0 or > 255))
        {
            throw new ArgumentException("An observed native menu button is required.");
        }
    }

    internal static string JournalPath(LauncherPaths paths, string session, string operation) => Path.Combine(paths.SessionsDirectory, session + ".bridge-operations", operation + ".json");
    internal static string RequestFingerprint(GameEditRequest request)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, GameBridgeJsonContext.Default.GameEditRequest)));
    internal static async Task<BridgeJournalEntry> ReadJournalAsync(string file, CancellationToken ct)
    {
        var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        await using var disposal = input.ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(input, BridgeWireJsonContext.Default.BridgeJournalEntry, ct).ConfigureAwait(false) ?? throw new InvalidDataException("Missing operation journal.");
    }

    internal static Task WriteJournalAsync(string file, BridgeJournalEntry entry, CancellationToken ct)
        => JsonFiles.WriteBytesAsync(file, JsonSerializer.SerializeToUtf8Bytes(entry, BridgeWireJsonContext.Default.BridgeJournalEntry), ct);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}