// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Servers;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomeServerState
{
    internal bool ServerMode { get; init; }
    internal IReadOnlyList<ServerFavorite> Favorites { get; init; } = [];
    internal string SelectedId { get; init; } = string.Empty;
    internal bool Loading { get; init; }
    internal bool ReadingTarget { get; init; }
    internal bool Submitting { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string PreferenceError { get; init; } = string.Empty;
    internal IReadOnlyDictionary<string, ServerStatus> Statuses { get; init; } = new Dictionary<string, ServerStatus>();
    internal LaunchTask? Request { get; init; }
    internal HomeInstanceDetails? Details { get; init; }
    internal string IdentityName { get; init; } = string.Empty;
    internal HomePageState Home { get; init; } = new();
    internal ServerFavorite? Selected => Favorites.FirstOrDefault(item => item.Id == SelectedId);
    internal string? InstanceId => Request?.InstanceId;
    internal HomeLaunchState Launch => Home.LaunchStates.GetValueOrDefault(InstanceId ?? string.Empty) ?? new("idle");
    internal bool SameTarget => Selected is { } server && Launch.ServerAddress == server.Address;
    internal bool CancelRequested => Launch.JobId is { } job && Home.CancelRequests.GetValueOrDefault(InstanceId ?? string.Empty) == job;
    internal bool CanConfigure => Selected is not null && !Loading && !ReadingTarget && !Submitting && (InstanceId is null || (!Home.PendingInstances.Contains(InstanceId) && !Launch.Busy));
    internal bool CanAct => Selected is not null && !Loading && !ReadingTarget && !Submitting && Home.LaunchObserved && Home.LaunchReadError.Length == 0 && (Request is null || (!Home.PendingInstances.Contains(Request.InstanceId) && (!Launch.Busy || (SameTarget && Launch.TaskActive && Launch.TaskOwned && !CancelRequested))));

    internal HomePageState Project() => Home with
    {
        SelectedId = InstanceId ?? string.Empty,
        Details = Details,
        ConfigurationError = string.Empty,
        ActionErrors = Error.Length > 0 || PreferenceError.Length > 0
            ? new Dictionary<string, string>
            {
                [InstanceId ?? string.Empty] = Error.Length > 0 ? Error : PreferenceError
            }
            : SameTarget && InstanceId is { } owner && Home.ActionErrors.TryGetValue(owner, out var error) ? new Dictionary<string, string>
            {
                [owner] = error
            }
            : [],
        LaunchStates = InstanceId is not { } id ? [] : new Dictionary<string, HomeLaunchState>
        {
            [id] = SameTarget
                ? Launch.Status == "exited" && !Launch.SessionOwned && !LaunchProblemText.HasFinding(Launch.Problem) ? new("idle") : Launch
                : Launch.Busy ? Launch with
                {
                    TaskOwned = false,
                    Owned = false,
                    SessionOwned = false,
                    Problem = null
                }
                : new("idle"),
        },
    };
    internal HomePageText Presentation
    {
        get
        {
            var projected = Project();
            var instance = Home.Instances.FirstOrDefault(item => item.Id == InstanceId);
            var action = Selected is null
                ? Resources.HomeConnectServer
                : Submitting || ReadingTarget
                ? Resources.LaunchUpdating
                : Launch.Busy
                ? projected.LaunchAction
                : InstanceId is { } pendingId && Home.InstallationIncomplete(pendingId)
                ? Resources.CreateResumeInstallation
                : Request is null
                ? Resources.HomeChooseClient
                : SameTarget && Launch.Status is "failed" or "canceled" or "interrupted" ? Resources.ServerRetry : Resources.HomeConnectServer;
            return new(
                Selected?.Name ?? Resources.HomeStartTitle,
                instance is null ? Resources.HomeServerChooseHint : string.Format(Resources.ServerInstanceChoice, instance.DisplayName, Details?.Version ?? instance.DisplayVersion),
                IdentityName.Length == 0
                    ? string.Empty
                    : Details is null ? IdentityName : string.Format(Resources.HomeServerIdentityConfiguration, IdentityName, Details.Configuration),
                action,
                Launch.Busy && !SameTarget ? Resources.InstanceBusyConflict : projected.LaunchTitle,
                Launch.Busy && !SameTarget ? string.Empty : projected.LaunchDetail,
                Resources.ServerEmpty,
                Home.LaunchReadError.Length > 0 ? Home.LaunchReadError : string.Empty);
        }
    }
}

/// <summary>Keeps home target selection separate from the local instance and delegates every launch to the existing pipeline.</summary>
internal sealed class HomeServerController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly AppSettings _settings;
    private readonly ServerConnectionPresenter _connections;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _reading;
    private long _revision;
    private long _listRevision;
    private bool _initialSelection;
    private bool _active = true;
    private bool _targetDirty = true;
    private string? _clientOverride;
    private Task _targetRead = Task.CompletedTask;

    internal HomeServerController(
        LauncherClient client,
        HomeController home,
        AppSettings settings,
        ServerConnectionPresenter connections)
    {
        _client = client;
        _home = home;
        _settings = settings;
        _connections = connections;
        State = new()
        {
            ServerMode = settings.HomeTargetKind == "server",
            SelectedId = settings.SelectedServerId,
            Home = home.State
        };
        _initialSelection = settings.SelectedServerId.Length == 0;
    }

    internal HomeServerState State { get; private set; }
    internal event Action<HomeServerState>? Changed;

    internal void Start()
    {
        _home.StateChanged += OnHomeChanged;
        _connections.Changed += OnFavoritesChanged;
        _connections.ClientSelected += OnClientSelected;
        _connections.Statuses.Changed += OnStatusesChanged;
        OnStatusesChanged();
        if (State.ServerMode)
            _ = RefreshAsync();
    }

    private void Publish(HomeServerState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    internal void ChooseMode(int index)
    {
        Publish(State with { ServerMode = index == 1 });
        UpdateStatusTargets();
        if (_active && State.ServerMode)
            _ = RefreshAsync();
        try
        {
            _settings.HomeTargetKind = index == 1 ? "server" : "local";
            Publish(State with { PreferenceError = string.Empty });
        }
        catch (Exception exception)
        {
            Publish(State with { PreferenceError = exception.Message });
            _home.ReportError(_home.State.SelectedId, exception.Message);
        }
    }

    internal void Select(string id) => SelectClient(id, null);

    private void SelectClient(string id, string? instanceId)
    {
        try
        {
            _settings.SelectedServerId = id;
            Publish(State with { PreferenceError = string.Empty });
        }
        catch (Exception exception)
        {
            Publish(State with { PreferenceError = exception.Message });
            return;
        }

        var targetChanged = _clientOverride != instanceId;
        _clientOverride = instanceId;
        ReadTarget(id, force: targetChanged);
    }

    internal async Task LaunchClientAsync(string serverId, string instanceId)
    {
        await RefreshAsync();
        ChooseMode(1);
        SelectClient(serverId, instanceId);
        await _targetRead;
        if (State.SelectedId == serverId && State.Request?.InstanceId == instanceId)
            await RunAsync();
    }

    internal void RefreshIdentity() => ReadTarget(State.SelectedId, force: true);

    private void OnFavoritesChanged() => _ = RefreshAsync();

    private void OnClientSelected(string serverId, string instanceId)
    {
        if (State.SelectedId != serverId)
            return;
        _clientOverride = instanceId;
        ReadTarget(serverId, force: true);
    }

    internal async Task RefreshAsync(bool force = false)
    {
        var revision = ++_listRevision;
        Publish(State with { Loading = true });
        try
        {
            var favorites = await _connections.ReadFavoritesAsync(_lifetime.Token, force);
            if (_lifetime.IsCancellationRequested || revision != _listRevision)
                return;
            var selected = State.SelectedId;
            var previous = State.Selected;
            var current = favorites.FirstOrDefault(item => item.Id == selected);
            if (previous?.PreferredInstanceId != current?.PreferredInstanceId)
                _clientOverride = null;
            if (previous?.Address != current?.Address || previous?.PreferredInstanceId != current?.PreferredInstanceId ||
                !(previous?.InstanceIds ?? []).SequenceEqual(current?.InstanceIds ?? []))
            {
                _targetDirty = true;
            }

            if (_initialSelection && selected.Length == 0)
            {
                selected = favorites.FirstOrDefault()?.Id ?? string.Empty;
                if (selected.Length > 0)
                {
                    try
                    {
                        _settings.SelectedServerId = selected;
                        Publish(State with { PreferenceError = string.Empty });
                    }
                    catch (Exception exception)
                    {
                        Publish(State with { PreferenceError = exception.Message });
                    }
                }
            }

            if (favorites.Length > 0)
                _initialSelection = false;
            var missing = selected.Length > 0 && favorites.All(item => item.Id != selected);
            Publish(State with
            {
                Favorites = favorites,
                Loading = false,
                Error = missing ? Resources.HomeServerRemoved : string.Empty
            });
            UpdateStatusTargets();
            if (missing)
            {
                ReadTarget(string.Empty);
                Publish(State with { Error = Resources.HomeServerRemoved });
            }
            else
            {
                ReadTarget(selected);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested && revision == _listRevision)
            {
                Publish(State with
                {
                    Loading = false,
                    Error = exception.Message
                });
            }
        }
    }

    internal void Activate(bool active)
    {
        _active = active;
        UpdateStatusTargets();
        if (active && State.ServerMode)
            _ = RefreshAsync();
    }

    private void UpdateStatusTargets() => _connections.Statuses.SetTargets("home", State.Favorites, _active && State.ServerMode);

    private void OnStatusesChanged() => Publish(State with { Statuses = _connections.Statuses.Values });

    private void ReadTarget(string id, bool force = false)
    {
        if (id != State.SelectedId || force)
            _targetDirty = true;
        if (!_targetDirty)
            return;
        if (!_active || !State.ServerMode)
        {
            _reading?.Cancel();
            _revision++;
            if (id != State.SelectedId)
            {
                Publish(State with { SelectedId = id, Request = null, Details = null, IdentityName = string.Empty, ReadingTarget = false });
            }
            else if (State.ReadingTarget)
            {
                Publish(State with { ReadingTarget = false });
            }

            return;
        }

        _targetDirty = false;
        _reading?.Cancel();
        _reading = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var revision = ++_revision;
        Publish(State with
        {
            SelectedId = id,
            Request = null,
            Details = null,
            IdentityName = string.Empty,
            ReadingTarget = true,
            Error = string.Empty
        });
        _targetRead = ReadTargetAsync(revision, _reading);
    }

    private async Task ReadTargetAsync(long revision, CancellationTokenSource reading)
    {
        var token = reading.Token;
        try
        {
            var server = State.Selected;
            if (server is null)
                return;
            var instanceId = _clientOverride ?? server.PreferredInstanceId ?? (server.InstanceIds.Length == 1 ? server.InstanceIds[0] : null);
            if (instanceId is null || State.Home.Instances.All(item => item.Id != instanceId || item.Unreadable))
                return;
            var request = await Task.Run(() => _client.CreateServerLaunchAsync(server.Address, instanceId, new LaunchOptions { ServerAddress = server.Address }, token), token);
            var identity = request.Options.AccountId is { } account ? (await Task.Run(() => _client.Identities.Get(account), token)).Name ?? string.Empty : request.Options.PlayerName ?? string.Empty;
            if (token.IsCancellationRequested || revision != _revision)
                return;
            Publish(State with
            {
                Request = request,
                IdentityName = identity,
                ReadingTarget = false
            });
            HomeInstanceDetails? details = null;
            var warning = string.Empty;
            try
            {
                details = await _home.ReadInstanceDetailsAsync(instanceId, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warning = exception.Message;
            }

            if (!token.IsCancellationRequested && revision == _revision)
            {
                _targetDirty = warning.Length > 0;
                Publish(State with
                {
                    Request = request,
                    Details = details,
                    IdentityName = identity,
                    Error = warning
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && revision == _revision)
            {
                _targetDirty = true;
                Publish(State with { Error = exception.Message });
            }
        }
        finally
        {
            if (!token.IsCancellationRequested && revision == _revision)
                Publish(State with { ReadingTarget = false });
            if (ReferenceEquals(_reading, reading))
                _reading = null;
            reading.Dispose();
        }
    }

    internal void Configure()
    {
        if (State.CanConfigure && State.Selected is { } server)
            _connections.Show(ServerAction.Connect, server, closed: OnFavoritesChanged, instanceId: State.InstanceId);
    }

    internal void Add() => _connections.Show(ServerAction.Add);

    internal async Task RunAsync()
    {
        if (!State.CanAct)
            return;
        if (State.Request is not { } request)
        {
            Configure();
            return;
        }

        if (State.SameTarget && State.Launch.TaskActive && State.Launch.TaskOwned)
        {
            try
            {
                await _home.CancelLaunchAsync(request.InstanceId);
            }
            catch (Exception exception)
            {
                if (!_lifetime.IsCancellationRequested)
                    Publish(State with { Error = exception.Message });
            }

            return;
        }

        var server = State.Selected!;
        var revision = _revision;
        Publish(State with
        {
            Submitting = true,
            Error = string.Empty
        });
        try
        {
            var cancellation = _lifetime.Token;
            var current = ServerFavorite.Read(await Task.Run(() => _client.Servers.GetAsync(server.Id, cancellation), cancellation));
            if (_lifetime.IsCancellationRequested || revision != _revision)
                return;
            if (current.Address != server.Address || current.PreferredInstanceId != server.PreferredInstanceId || !current.InstanceIds.SequenceEqual(server.InstanceIds))
            {
                await RefreshAsync(force: true);
                Publish(State with { Error = Resources.HomeServerChanged });
                return;
            }

            var job = await _home.StartLaunchAsync(request);
            if (job is null && !_lifetime.IsCancellationRequested && revision == _revision)
                Publish(State with { Error = _home.State.ActionErrors.GetValueOrDefault(request.InstanceId) ?? string.Empty });
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested && revision == _revision)
                Publish(State with { Error = exception.Message });
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                Publish(State with { Submitting = false });
        }
    }

    private void OnHomeChanged(HomePageState state)
    {
        var instancesChanged = State.Home.InstancesRevision != state.InstancesRevision;
        var details = State.InstanceId == state.SelectedId && state.Details is not null ? state.Details : State.Details;
        if (instancesChanged)
        {
            _targetDirty = true;
            _reading?.Cancel();
            _revision++;
        }

        Publish(State with { Home = state, Details = details });
        if (instancesChanged && _active && State.ServerMode)
            _ = RefreshAsync();
    }

    public void Dispose()
    {
        _home.StateChanged -= OnHomeChanged;
        _connections.Changed -= OnFavoritesChanged;
        _connections.ClientSelected -= OnClientSelected;
        _connections.Statuses.Changed -= OnStatusesChanged;
        _connections.Statuses.SetTargets("home", [], false);
        _lifetime.Cancel();
        _reading?.Cancel();
        _reading?.Dispose();
        _reading = null;
        _lifetime.Dispose();
    }
}