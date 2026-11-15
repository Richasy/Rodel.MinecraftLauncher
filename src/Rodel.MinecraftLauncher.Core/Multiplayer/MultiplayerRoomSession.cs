// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Owns the game observation and bounded network recovery for one admitted room.</summary>
internal sealed class MultiplayerRoomSession
{
    private readonly LauncherClient _client;
    private readonly MultiplayerRequest _request;
    private readonly string _directory;
    private readonly MultiplayerRoom? _previous;
    private readonly List<RoomEvent> _events = [];
    private MultiplayerRoom _room;
    private TerracottaClient? _component;
    private RoomGameEntry? _entry;
    private GameEditRequest? _publication;
    private string? _incarnation;
    private string? _world;
    private string _invitation;
    private string _saved = string.Empty;
    private long _index = -1;
    private bool _begun;
    private bool _automationAttempted;
    private bool _publicationRetryAllowed;
    private string? _fallbackAddress;
    private Servers.ServerEndpoint? _fallbackEndpoint;
    private GameStatusProtocol _fallbackProtocol;
    private string? _gameErrorCode;
    private DateTimeOffset _nextFallbackProbe;
    private bool _blockedPort;
    private int _attempts;
    private int _pollFailures;
    private bool _startedNewGame;
    private bool _startingOwnedGame;
    private bool _guestLaunchAttempted;
    private DateTimeOffset _retryAt = DateTimeOffset.MaxValue;
    private DateTimeOffset _connectDeadline = DateTimeOffset.MaxValue;

    internal MultiplayerRoomSession(LauncherClient client, MultiplayerRequest request, string directory, MultiplayerRoom? previous)
    {
        _client = client; _request = request; _directory = directory; _previous = previous;
        using var process = Process.GetCurrentProcess();
        _room = new()
        {
            Id = request.RoomId,
            Role = request.Role,
            Name = string.IsNullOrWhiteSpace(request.Name) ? client.Instances.Get(request.InstanceId).Name : request.Name.Trim(),
            InstanceId = request.InstanceId,
            Active = true,
            OwnerProcessId = process.Id,
            OwnerStartedAt = process.StartTime.ToUniversalTime().ToString("O"),
            SessionId = request.SessionId
        };
        _invitation = request.Role == "guest" ? ProtectedStore.Read(SecretFile)?["invitation"]?.GetValue<string>() ?? throw new InvalidDataException("Missing protected invitation.") : string.Empty;
    }

    private string SecretFile => Path.Combine(_directory, _room.Id + ".secret.json");

    internal async Task<MultiplayerRoom> RunAsync(CancellationToken ct)
    {
        await SaveAsync().ConfigureAwait(false);
        try
        {
            await SelectGameAsync(ct).ConfigureAwait(false);
            if (_room.State is not ("ending" or "exit-pending" or "stopped"))
            {
                if (_room.Role == "guest") _entry = new();
                await StartComponentAsync(ct).ConfigureAwait(false);
                _room = _room with { State = _room.Role == "host" ? _request.AutoOpen ? "waiting-world" : "manual" : "connecting" };
            }

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (_room.State == "stopped") break;
                if (await ApplyControlAsync(ct).ConfigureAwait(false)) break;
                if (_room.State is "ending" or "exit-pending")
                {
                    _room = await MultiplayerGameExit.AdvanceAsync(_client, _room, requestClose: false).ConfigureAwait(false);
                    if (_room.State == "stopped") break;
                    await SaveAsync().ConfigureAwait(false);
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    continue;
                }

                if (_room.State != "closed" && _room.Role == "host") await ObserveGameAsync(ct).ConfigureAwait(false);
                if (_room.State == "closed") break;
                if (_room.Role == "guest" && _room.SessionId is { } guestSession && !MultiplayerService.OwnerMatchesGame(_client.Sessions.Get(guestSession)))
                { await CloseWorldAsync("MULTIPLAYER_GAME_EXITED", ct).ConfigureAwait(false); break; }

                if (_room.State != "closed" && _room.Path == "terracotta" && !_blockedPort && (_room.Role == "guest" || _room.GamePort is not null))
                    await AdvanceNetworkAsync(ct).ConfigureAwait(false);
                if (_room.Path == "fallback") await ObserveFallbackAsync(ct).ConfigureAwait(false);
                if (_room.State == "closed") break;
                if (_room.Role == "guest" && _request.StartGame && _room.State == "connected" && !_guestLaunchAttempted)
                {
                    _guestLaunchAttempted = true;
                    await LaunchGuestAsync(ct).ConfigureAwait(false);
                }

                await SaveAsync().ConfigureAwait(false);
                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            if (_room.State != "closed") _room = _room with { State = "stopped" };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _room = _room.EndOperationId is not null && _room.State is "ending" or "exit-pending"
                ? _room with { State = "exit-pending", ErrorCode = "MULTIPLAYER_GAME_EXIT_PENDING" }
                : _room with { State = "stopped", ErrorCode = null };
            throw;
        }
        catch (Exception error)
        {
            _room = _room with { State = "failed", ErrorCode = error is LauncherOperationException operation ? operation.Code : "MULTIPLAYER_OPERATION_FAILED" };
            throw;
        }
        finally
        {
            if (ct.IsCancellationRequested && _startedNewGame && (!_begun || _startingOwnedGame) && _room.SessionId is not null)
            {
                // The exact session was captured before monitor dispatch, including cancellation at the start boundary.
                using var startupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { _ = await _client.Sessions.WaitForStartAsync(_room.SessionId, startupDeadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (startupDeadline.IsCancellationRequested) { }

                BindGame();
                _room = _room with { State = "ending", EndOperationId = _room.EndOperationId ?? Guid.NewGuid().ToString("N") };
                _room = await MultiplayerGameExit.AdvanceAsync(_client, _room, requestClose: true).ConfigureAwait(false);
                while (_room.State == "ending")
                {
                    await SaveAsync().ConfigureAwait(false);
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                    _room = await MultiplayerGameExit.AdvanceAsync(_client, _room, requestClose: false).ConfigureAwait(false);
                }
            }

            _room = _room with { Active = false, Invitation = null };
            if (_publication is not null && _room.SessionId is { } sessionId)
            {
                try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await _client.GameBridge.CancelOperationAsync(sessionId, _publication.OperationId, deadline.Token).ConfigureAwait(false); }
                catch (Exception error) when (Recoverable(error)) { }
            }

            await StopComponentAsync().ConfigureAwait(false);
            if (_entry is not null) await _entry.DisposeAsync().ConfigureAwait(false);
            await SaveAsync().ConfigureAwait(false);
        }

        return _room;
    }

    private async Task SelectGameAsync(CancellationToken ct)
    {
        if (_request.SessionId is not null && !string.IsNullOrWhiteSpace(_request.Launch.World))
            throw new ArgumentException("Choose either an existing game session or a world to launch.");
        if (_room.SessionId is null)
        {
            var running = _client.Sessions.List().Where(session => session.InstanceId == _request.InstanceId && session.Status == "running" && MultiplayerService.OwnerMatchesGame(session)).Take(2).ToArray();
            if (_request.Role == "host" && !string.IsNullOrWhiteSpace(_request.Launch.World) && running.Length > 0)
                throw new LauncherOperationException("MULTIPLAYER_GAME_SELECTION_REQUIRED", "Select the existing running game explicitly before hosting; a world launch cannot silently reuse another world.");
            if (running.Length > 1) throw new LauncherOperationException("MULTIPLAYER_GAME_SELECTION_REQUIRED", "Select the exact running game session.");
            _room = _room with { SessionId = running.SingleOrDefault()?.Id };
        }

        if (_room.Role == "host" && _room.SessionId is null)
        {
            var result = await LaunchNewGameAsync(_request.Launch with { GameBridge = _request.AutoOpen, WaitForExit = false, Server = null, ServerAddress = null, InheritServer = false }, ct).ConfigureAwait(false);
            if (_room.State is "ending" or "exit-pending" or "stopped") return;
            _room = _room with { SessionId = (result as SessionStatus)?.Id ?? throw new IOException("The game did not return a monitored session.") };
        }

        if (_room.SessionId is { } selected)
        {
            var game = _client.Sessions.Get(selected);
            if (game.InstanceId != _request.InstanceId)
                throw new LauncherOperationException("MULTIPLAYER_GAME_MISMATCH", "The selected game session belongs to another instance.");
            if (!MultiplayerService.OwnerMatchesGame(game))
                throw new LauncherOperationException("MULTIPLAYER_GAME_EXITED", "The selected game has already exited.");
        }

        BindGame();
    }

    private async Task<ILaunchResult> LaunchNewGameAsync(LaunchOptions options, CancellationToken ct)
    {
        using var starting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var launch = _client.LaunchAsync(_request.InstanceId,
            options with { SessionCreated = async id => { _room = _room with { SessionId = id, GameProcessId = null, GameStartedAt = null }; _startedNewGame = _startingOwnedGame = true; await SaveAsync().ConfigureAwait(false); } }, starting.Token);
        RoomControl? ending = null;
        var file = Path.Combine(_directory, "control.json");
        while (!launch.IsCompleted)
        {
            if (File.Exists(file))
            {
                var control = JsonSerializer.Deserialize(JsonFiles.ReadObject(file), TerracottaJsonContext.Default.RoomControl);
                if (control?.RoomId == _room.Id && control.Id != _room.ControlId && control.Action == "end")
                { ending = control; starting.Cancel(); break; }
            }

            await Task.WhenAny(launch, Task.Delay(200, ct)).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                starting.Cancel();
                try { _ = await launch.ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }

                ct.ThrowIfCancellationRequested();
            }
        }

        try
        {
            var result = await launch.ConfigureAwait(false);
            if (ending is null) { _startingOwnedGame = false; return result; }
        }
        catch (OperationCanceledException) when (ending is not null && !ct.IsCancellationRequested) { }

        if (ending is null) throw new InvalidOperationException("The game start did not return a result.");
        _room = _room with { State = "ending", ControlId = ending.Id, EndOperationId = ending.Id, Invitation = null, Address = null, Members = [] };
        _entry?.Target(null);
        await StopComponentAsync().ConfigureAwait(false);
        if (_startedNewGame && _room.SessionId is { } id)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { _ = await _client.Sessions.WaitForStartAsync(id, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }

            BindGame();
        }

        _room = await MultiplayerGameExit.AdvanceAsync(_client, _room, true).ConfigureAwait(false);
        await SaveAsync().ConfigureAwait(false);
        _startingOwnedGame = false;
        return new SessionStatus { Id = _room.SessionId, Status = "canceled" };
    }

    private void BindGame()
    {
        if (_room.SessionId is not { } id) return;
        var game = _client.Sessions.Get(id);
        _room = _room with { GameProcessId = game.ProcessId, GameStartedAt = game.ProcessStartUtc };
    }

    private async Task StartComponentAsync(CancellationToken ct)
    {
        _component = await TerracottaClient.StartAsync(Path.Combine(_directory, "component"), ct).ConfigureAwait(false);
        _room = _room with { ComponentPort = _component.Port, ComponentProcessId = _component.WorkerId, ComponentStartedAt = _component.WorkerStartedAt };
        _index = -1;
        var initial = await _component.StateAsync(ct).ConfigureAwait(false);
        if (initial.State != "waiting")
        {
            if (_previous?.ComponentPort != _component.Port || _previous.Active)
                throw new LauncherOperationException("MULTIPLAYER_COMPONENT_BUSY", "The owned component has an unfinished room; close it before starting another.");
            await _component.ResetAsync(ct).ConfigureAwait(false);
        }

        await _component.PrepareEngineAsync(ct).ConfigureAwait(false);
        await SaveAsync().ConfigureAwait(false);
    }

    private async Task<bool> ApplyControlAsync(CancellationToken ct)
    {
        var file = Path.Combine(_directory, "control.json");
        if (!File.Exists(file)) return false;
        var control = JsonSerializer.Deserialize(JsonFiles.ReadObject(file), TerracottaJsonContext.Default.RoomControl);
        if (control?.RoomId != _room.Id || control.Id == _room.ControlId) return false;
        _room = _room with { ControlId = control.Id };
        if (control.Action == "stop") return true;
        if (control.Action == "end")
        {
            _room = _room with { State = "ending", EndOperationId = _room.EndOperationId ?? control.Id, Members = [], Invitation = null, Address = null, ErrorCode = null };
            _entry?.Target(null);
            await StopComponentAsync().ConfigureAwait(false);
            if (_entry is not null) { await _entry.DisposeAsync().ConfigureAwait(false); _entry = null; }

            await SaveAsync().ConfigureAwait(false);
            _room = await MultiplayerGameExit.AdvanceAsync(_client, _room, requestClose: true).ConfigureAwait(false);
            return _room.State == "stopped";
        }

        if (control.Action is "retry" or "invitation")
        {
            if (_room.State == "closed" && control.Action != "invitation") return false;
            if (control.Action == "retry" && _room.Path == "fallback")
            {
                _nextFallbackProbe = DateTimeOffset.MinValue;
                return false;
            }

            if (control.Action == "invitation")
            {
                _invitation = ProtectedStore.Read(Path.Combine(_directory, _room.Id + ".pending.secret.json"))?["invitation"]?.GetValue<string>() ?? throw new InvalidDataException("Missing replacement invitation.");
                await ProtectedStore.WriteAsync(SecretFile, new JsonObject { ["invitation"] = _invitation }, ct).ConfigureAwait(false);
            }

            if (_publicationRetryAllowed)
            {
                _automationAttempted = false;
                _publicationRetryAllowed = false;
            }
            // An uncertain publication retains its operation identity and is observed, never replayed.
            if (_room.Role == "host" && _room.GamePort is null && _publication is not null)
            {
                Manual("GAME_OPERATION_OUTCOME_UNKNOWN");
                return false;
            }

            _fallbackAddress = null;
            _fallbackEndpoint = null;
            _fallbackProtocol = GameStatusProtocol.Unknown;
            _attempts = 0; _blockedPort = false; _retryAt = DateTimeOffset.UtcNow;
            _room = _room with { State = "retrying", Path = "terracotta", Address = null, ErrorCode = null, Members = [] };
        }

        if (control.Action == "fallback")
        {
            // A configured address is not proof of a reachable Minecraft endpoint.
            try { await _component!.ResetAsync(ct).ConfigureAwait(false); }
            catch (Exception error) when (Recoverable(error) && !ct.IsCancellationRequested) { }

            _fallbackAddress = control.Address;
            _fallbackEndpoint = null;
            _fallbackProtocol = GameStatusProtocol.Unknown;
            _nextFallbackProbe = DateTimeOffset.MinValue;
            _entry?.Target(null);
            _room = _room with { Path = "fallback", Address = null, State = "connecting", ErrorCode = null, Members = [] };
        }

        if (control.Action == "launch" && _room.Role == "guest" && _room.State == "connected")
        {
            await LaunchGuestAsync(ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task LaunchGuestAsync(CancellationToken ct)
    {
        try
        {
            var address = _room.Address;
            if (_room.SessionId is { } id && MultiplayerService.OwnerMatchesGame(_client.Sessions.Get(id)))
            {
                if (_client.Sessions.Get(id).Server == address)
                {
                    _gameErrorCode = null;
                    _room = _room with { ErrorCode = null };
                    return;
                }

                if (!_request.RestartGame)
                { _gameErrorCode = "MULTIPLAYER_GAME_RESTART_REQUIRED"; _room = _room with { ErrorCode = _gameErrorCode }; return; }

                var closing = _room with { State = "ending", EndOperationId = Guid.NewGuid().ToString("N") };
                closing = await MultiplayerGameExit.AdvanceAsync(_client, closing, requestClose: true).ConfigureAwait(false);
                while (closing.State == "ending")
                { await Task.Delay(200, ct).ConfigureAwait(false); closing = await MultiplayerGameExit.AdvanceAsync(_client, closing, requestClose: false).ConfigureAwait(false); }

                if (closing.State != "stopped")
                {
                    _room = closing; _entry?.Target(null);
                    await StopComponentAsync().ConfigureAwait(false);
                    return;
                }

                _room = _room with { SessionId = null, GameProcessId = null, GameStartedAt = null };
            }

            _room = _room with { State = "starting-game" };
            await SaveAsync().ConfigureAwait(false);
            var result = await LaunchNewGameAsync(_request.Launch with { Server = null, World = string.Empty, ServerAddress = address, InheritServer = false, WaitForExit = false }, ct).ConfigureAwait(false);
            if (_room.State is "ending" or "exit-pending" or "stopped") return;
            _room = _room with { State = "connected", SessionId = (result as SessionStatus)?.Id ?? throw new IOException("The game did not return a monitored session."), ErrorCode = null, GameCloseRequestedAt = null, EndOperationId = null };
            _gameErrorCode = null;
            BindGame();
        }
        catch (Exception error) when (Recoverable(error) && !ct.IsCancellationRequested)
        {
            _gameErrorCode = error is LauncherOperationException operation ? operation.Code : "MULTIPLAYER_GAME_LAUNCH_FAILED";
            _room = _room with { State = "connected", ErrorCode = _gameErrorCode };
        }
    }

    private async Task ObserveGameAsync(CancellationToken ct)
    {
        var session = _client.Sessions.Get(_room.SessionId!);
        if (session.Status is "exited" or "failed" or "interrupted" || !MultiplayerService.OwnerMatchesGame(session))
        {
            await CloseWorldAsync("MULTIPLAYER_GAME_EXITED", ct).ConfigureAwait(false);
            return;
        }

        if (_request.AutoOpen)
            await ObserveBridgeAsync(session.Id!, ct).ConfigureAwait(false);
        if (_room.State == "closed") return;
        if (_room.GamePort is null)
        {
            try { _room = _room with { GamePort = await MinecraftListener.FindAsync(session.ProcessId!.Value, ct).ConfigureAwait(false) }; }
            catch (LauncherOperationException error) when (error.Code == "MULTIPLAYER_MULTIPLE_PORTS") { Manual(error.Code); }
        }

        if (_room.GamePort is { } port && !TcpOwnership.Listeners().Contains((port, session.ProcessId!.Value)))
            await CloseWorldAsync("MULTIPLAYER_GAME_PORT_LOST", ct).ConfigureAwait(false);
    }

    private async Task ObserveBridgeAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var snapshot = await _client.GameBridge.SnapshotAsync(sessionId, ct).ConfigureAwait(false);
            if (_world is not null && snapshot.Ready && (snapshot.GameIncarnation != _incarnation || snapshot.LocalWorldEpoch != _world))
            {
                await CloseWorldAsync("MULTIPLAYER_WORLD_CHANGED", ct).ConfigureAwait(false);
                return;
            }

            if (!_automationAttempted && snapshot.Ready && snapshot.Mode == "local_owned" && snapshot.LocalWorldEpoch is { } world)
            {
                _automationAttempted = true; _incarnation = snapshot.GameIncarnation; _world = world;
                var query = new GameQueryRequest { GameIncarnation = snapshot.GameIncarnation, WorldEpoch = snapshot.WorldEpoch!, Dimension = snapshot.Dimension!, Kind = "world" };
                var observed = await _client.GameBridge.QueryAsync(sessionId, query, ct).ConfigureAwait(false);
                var name = observed.Data.TryGetProperty("name", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString()! : string.Empty;
                _room = _room with { State = "opening", World = new(name, world, "opening") };
                _publication = new() { OperationId = Guid.NewGuid().ToString("N"), GameIncarnation = snapshot.GameIncarnation, WorldEpoch = snapshot.WorldEpoch!, Dimension = snapshot.Dimension!, Kind = "world.publish" };
                _ = await _client.GameBridge.EditAsync(sessionId, _publication, ct).ConfigureAwait(false);
            }

            if (_publication is not null && _room.World?.State != "open")
            {
                var operation = await _client.GameBridge.GetOperationAsync(sessionId, _publication.OperationId, ct).ConfigureAwait(false);
                if (operation.State == "applied" && operation.Result is { } result && result.TryGetProperty("port", out var port))
                {
                    _room = _room with { GamePort = port.GetInt32(), World = _room.World is { } current ? current with { State = "open" } : null };
                }
                else if (operation.State is "rejected" or "canceled")
                {
                    _publication = null;
                    _publicationRetryAllowed = true;
                    _room = _room with { World = _room.World is { } current ? current with { State = "unknown" } : null };
                    Manual(operation.ErrorCode);
                }
                else if (operation.State == "unknown")
                {
                    Manual(operation.ErrorCode);
                }
            }

            if (!snapshot.Ready && _world is not null)
                _room = _room with { World = _room.World is { } last ? last with { State = "unknown" } : null };
        }
        catch (Exception error) when (Recoverable(error) && !ct.IsCancellationRequested)
        {
            // Publication intents with an unknown effect are never replayed. Ordinary gameplay and
            // manual LAN remain usable when mappings or the optional bridge are unavailable.
            if (_publication is null && _automationAttempted) _publicationRetryAllowed = true;
            if (_room.GamePort is null) Manual("MULTIPLAYER_AUTOMATION_UNAVAILABLE");
            else _room = _room with { World = _room.World is { } last ? last with { State = "unknown" } : null };
        }
    }

    private void Manual(string? code) => _room = _room with { State = "manual", ErrorCode = code ?? "MULTIPLAYER_AUTOMATION_UNAVAILABLE" };

    private async Task ObserveFallbackAsync(CancellationToken ct)
    {
        if (_fallbackAddress is null || DateTimeOffset.UtcNow < _nextFallbackProbe) return;
        _nextFallbackProbe = DateTimeOffset.UtcNow.AddSeconds(2);
        Servers.ServerEndpoint? target = null;
        var protocol = GameStatusProtocol.Unknown;
        var attempted = new HashSet<Servers.ServerEndpoint>();
        var host = Servers.ServerService.ParseAddress(_fallbackAddress).Host;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            if (_fallbackEndpoint is { } current)
            {
                attempted.Add(current);
                var (currentReachable, observedProtocol) = await MinecraftListener.ProbeFallbackAsync(current, host, deadline.Token).ConfigureAwait(false);
                protocol = observedProtocol;
                if (currentReachable) target = current;
            }

            if (target is null)
            {
                var resolved = await _client.Servers.ResolveAsync(_fallbackAddress, deadline.Token).ConfigureAwait(false);
                if (resolved.ServiceUnavailable != true)
                {
                    Servers.ServerEndpoint[] candidates = resolved.Candidates is { Count: > 0 }
                        ? [.. resolved.Candidates.Take(8).Select(item => new Servers.ServerEndpoint(item.Host, item.Port))]
                        : [new(resolved.ConnectHost!, resolved.ConnectPort!.Value)];
                    foreach (var endpoint in candidates)
                    {
                        if (!attempted.Add(endpoint)) continue;
                        var (candidateReachable, observedProtocol) = await MinecraftListener.ProbeFallbackAsync(endpoint, host, deadline.Token).ConfigureAwait(false);
                        if (!candidateReachable) continue;
                        protocol = observedProtocol;
                        target = endpoint;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException) { }

        var reachable = target is not null;
        if (target is not null)
        {
            if (protocol != GameStatusProtocol.Unknown || target != _fallbackEndpoint) _fallbackProtocol = protocol;
            _fallbackEndpoint = target;
            var targetAddress = target.Host.Contains(':') ? $"[{target.Host}]:{target.Port}" : $"{target.Host}:{target.Port}";
            // Probes only update future connections. Failure cannot revoke an existing game,
            // and changing a resolved endpoint cannot splice its established TCP or UDP flow.
            _entry?.Target(targetAddress, host, _fallbackProtocol, disconnectExisting: false);
        }

        _room = _room with
        {
            State = reachable ? "connected" : "disconnected",
            Address = reachable ? _entry?.Address ?? _fallbackAddress : null,
            ErrorCode = !reachable ? "MULTIPLAYER_FALLBACK_UNAVAILABLE"
                : _gameErrorCode ?? (protocol == GameStatusProtocol.Unknown ? "MULTIPLAYER_FALLBACK_STATUS_UNKNOWN" : null),
            Members = []
        };
    }

    private async Task CloseWorldAsync(string code, CancellationToken ct)
    {
        _entry?.Target(null);
        _room = _room with { State = "closed", ErrorCode = code, Address = null, Members = [], World = _room.World is { } world ? world with { State = "unknown" } : null };
        try { await _component!.ResetAsync(ct).ConfigureAwait(false); }
        catch (Exception error) when (Recoverable(error) && !ct.IsCancellationRequested) { }
    }

    private async Task AdvanceNetworkAsync(CancellationToken ct)
    {
        try
        {
            if (DateTimeOffset.UtcNow >= _retryAt)
            {
                _retryAt = DateTimeOffset.MaxValue; _attempts++;
                _connectDeadline = DateTimeOffset.UtcNow.AddSeconds(60);
                _room = _room with { State = "retrying", RetryCount = _attempts, Members = [] };
                await SaveAsync().ConfigureAwait(false);
                // A dead worker is replaced only after exact-identity cleanup; game and code survive.
                if (_component?.Alive != true)
                { await StopComponentAsync().ConfigureAwait(false); await StartComponentAsync(ct).ConfigureAwait(false); }

                await _component!.ResetAsync(ct).ConfigureAwait(false);
                _begun = false; _index = -1;
            }

            if (!_begun)
            {
                if (_room.Role == "host" && _invitation.Length == 0)
                {
                    _invitation = RoomCode.Create();
                    await ProtectedStore.WriteAsync(SecretFile, new JsonObject { ["invitation"] = _invitation }, ct).ConfigureAwait(false);
                }
                // A timed-out write may already have started the native room. Observe its result
                // within the discovery deadline instead of replaying the state-changing request.
                _begun = true; _connectDeadline = DateTimeOffset.UtcNow.AddSeconds(60);
                _room = _room with { State = _attempts > 0 ? "retrying" : "connecting" };
                await _component!.BeginAsync(_room.Role, _invitation, _request.PlayerName, ct).ConfigureAwait(false);
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var network = await _component!.StateAsync(deadline.Token).ConfigureAwait(false);
            _pollFailures = 0;
            if (network.Index > _index)
            {
                _index = network.Index;
                if (network.Room is { } code && code != _invitation)
                    throw new LauncherOperationException("MULTIPLAYER_COMPONENT_OWNERSHIP", "The component changed to a different room.");
                if (network.State == (_room.Role == "host" ? "host-ok" : "guest-ok"))
                {
                    if (_room.Role == "host" && await _component.HostPortAsync(deadline.Token).ConfigureAwait(false) != _room.GamePort)
                    {
                        _blockedPort = true;
                        await _component.ResetAsync(ct).ConfigureAwait(false);
                        _room = _room with { State = "disconnected", ErrorCode = "MULTIPLAYER_PORT_MISMATCH", Members = [] };
                        return;
                    }

                    if (_room.Role == "guest" && !IsLoopback(network.Url)) throw new InvalidDataException("The component did not return a local game entry.");
                    _entry?.Target(network.Url);
                    _room = _room with { State = "connected", ErrorCode = _gameErrorCode, RetryCount = 0, Address = _entry?.Address, Members = Members(network.Profiles) };
                    _attempts = 0; _retryAt = _connectDeadline = DateTimeOffset.MaxValue;
                }
                else if (network.State == "exception")
                {
                    if (network.Type == 4) await CloseWorldAsync("MULTIPLAYER_CONNECTION_4", ct).ConfigureAwait(false);
                    else Disconnected("MULTIPLAYER_CONNECTION_" + network.Type);
                }
            }

            if (DateTimeOffset.UtcNow >= _connectDeadline)
            { _connectDeadline = DateTimeOffset.MaxValue; Disconnected("MULTIPLAYER_DISCOVERY_TIMEOUT"); }
        }
        catch (Exception error) when (Recoverable(error) && !ct.IsCancellationRequested)
        {
            _pollFailures++;
            // Upstream may briefly serialize state reads behind native-engine initialization.
            // Do not reset a live startup merely because three HTTP reads timed out; use its
            // explicit discovery deadline. An exited worker is independently observable now.
            if (_component?.Alive != true || (_room.State == "connected" && _pollFailures >= 3) || DateTimeOffset.UtcNow >= _connectDeadline)
                Disconnected("MULTIPLAYER_COMPONENT_UNAVAILABLE");
        }
    }

    private void Disconnected(string code)
    {
        _entry?.Target(null);
        _room = _room with { State = "disconnected", ErrorCode = code, Members = [] };
        if (_attempts < 3 && _retryAt == DateTimeOffset.MaxValue) _retryAt = DateTimeOffset.UtcNow.AddSeconds(1 << _attempts);
    }

    private async Task StopComponentAsync()
    {
        if (_component is null) return;
        try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await _component.StopAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception error) when (Recoverable(error)) { }
        finally { _component.Dispose(); _component = null; }
    }

    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_room with { Invitation = null, UpdatedAt = default }, MultiplayerJsonContext.Default.MultiplayerRoom);
        if (json == _saved) return;
        var last = _events.LastOrDefault();
        if (last?.State != _room.State || last.Code != _room.ErrorCode)
        {
            _events.Add(new(DateTimeOffset.UtcNow, _room.State, _room.ErrorCode));
            if (_events.Count > 200) _events.RemoveAt(0);
            try { await JsonFiles.WriteAsync(Path.Combine(_directory, _room.Id + ".events.json"), JsonSerializer.SerializeToNode([.. _events], TerracottaJsonContext.Default.RoomEventArray)!, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Optional diagnostic history cannot terminate a live room. */ }
        }

        _room = _room with { UpdatedAt = DateTimeOffset.UtcNow };
        await JsonFiles.WriteAsync(Path.Combine(_directory, "room.json"), JsonSerializer.SerializeToNode(_room with { Invitation = null }, MultiplayerJsonContext.Default.MultiplayerRoom)!, CancellationToken.None).ConfigureAwait(false);
        _saved = json;
    }

    private static bool Recoverable(Exception error) => error is IOException or HttpRequestException or OperationCanceledException or LauncherOperationException or JsonException;
    private static bool IsLoopback(string? address)
    {
        try { return address is not null && System.Net.IPAddress.TryParse(Servers.ServerService.ParseAddress(address).Host, out var ip) && System.Net.IPAddress.IsLoopback(ip); }
        catch (ArgumentException) { return false; }
    }
    private static MultiplayerMember[] Members(TerracottaProfile[]? profiles) => [.. (profiles ?? []).Where(profile => !string.IsNullOrWhiteSpace(profile.MachineId)).Take(256).Select(profile => new MultiplayerMember(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profile.MachineId)))[..16], profile.Name ?? string.Empty, (profile.Kind ?? "guest").ToLowerInvariant()))];
}