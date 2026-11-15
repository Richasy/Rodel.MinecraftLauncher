// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Import;
using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Home;

/// <summary>Coordinates instance reads and Core tasks on the caller's UI context, independently of window chrome.</summary>
internal sealed class HomeController(
    LauncherClient client,
    string initialSelection = "",
    Action<string>? selectionChanged = null,
    Func<Task<LaunchOptions>>? launchOptions = null) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly InstanceDetailsCache _details = new((id, ct) => HomeInstanceReader.ReadAsync(client, id, ct));
    private readonly LaunchProblemObserver _problems = new(client);
    private readonly Dictionary<string, string> _problemTargets = [with(StringComparer.Ordinal)];

    internal void ObserveProblemsFor(string? instanceId, string owner = "home")
    {
        if (string.IsNullOrEmpty(instanceId))
            _problemTargets.Remove(owner);
        else
            _problemTargets[owner] = instanceId;
    }
    private readonly HashSet<string> _handledSessions = [];
    private readonly HashSet<string> _preparingLaunches = [];
    private CancellationTokenSource? _detailLoad;
    private long _observationRevision;
    private long _instanceLoadRevision;
    private int _managementWork;
    internal ImportController Import { get; } = new(client);
    internal Features.Create.CreateController Create { get; } = new(client);
    internal bool HasOwnedWork => State.HasOwnedWork || Import.State.Active || Create.State.Active || Volatile.Read(ref _managementWork) > 0;
    internal bool HasPreparingLaunch => _preparingLaunches.Count > 0;

    internal IDisposable TrackManagementWork()
    {
        Interlocked.Increment(ref _managementWork);
        return new ManagementWork(() => Interlocked.Decrement(ref _managementWork));
    }

    private sealed class ManagementWork(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    internal HomePageState State { get; private set; } = new()
    {
        SelectedId = initialSelection
    };
    internal event Action<HomePageState>? StateChanged;
    internal event Action<string>? GameStarted;
    internal Func<LaunchTask, CancellationToken, Task<bool>>? PrepareServerConnection { get; set; }
    internal Func<CancellationToken, Task<string?>>? RequireLaunchIdentity { get; set; }

    internal void Start()
    {
        Import.Completed += OnImported;
        Create.Completed += OnImported;
        Create.StateChanged += OnCreateStateChanged;
        _ = LoadAsync();
        _ = ObserveAsync();
    }

    private void Publish(HomePageState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(state);
    }

    internal void Select(string id)
    {
        if (id == State.SelectedId && (State.Details is not null || (_detailLoad is not null && State.ConfigurationError.Length == 0)))
            return;
        if (id.Length > 0 && id != State.SelectedId)
            selectionChanged?.Invoke(id);
        _detailLoad?.Cancel();
        _detailLoad?.Dispose();
        _detailLoad = null;
        Publish(State with
        {
            SelectedId = id,
            Details = null,
            ConfigurationError = string.Empty,
        });
        if (State.Selected is not { Unreadable: false })
            return;
        _detailLoad = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = LoadDetailsAsync(id, _detailLoad.Token);
    }
    private string? _installingId;

    private void OnCreateStateChanged(Features.Create.CreateState state)
    {
        var pending = State.PendingInstances.Where(id => id != _installingId).ToList();
        _installingId = state.Active ? state.InstanceId : null;
        if (_installingId is not null && !pending.Contains(_installingId))
            pending.Add(_installingId);
        Publish(State with { PendingInstances = pending });
    }

    private void OnImported(string? id) => _ = LoadAsync(id);

    internal async Task LaunchCreatedAsync(string id)
    {
        await LoadAsync(id);
        if (State.SelectedId == id && !State.CurrentLaunch.Busy)
        {
            await LoadDetailsAsync(id, _lifetime.Token);
            await RunLaunchActionAsync();
        }
    }

    internal Task RefreshAsync(string? preferredId = null) => LoadAsync(preferredId);

    internal Task<HomeInstanceDetails> ReadInstanceDetailsAsync(string id, CancellationToken cancellationToken) => _details.ReadAsync(id, cancellationToken);

    internal Task<SessionDiagnosis> DiagnoseAsync(string sessionId, CancellationToken cancellationToken) => client.Maintenance.DiagnoseSessionAsync(sessionId, cancellationToken);

    internal void SetManagementBusy(string id, bool busy) => Publish(State with { PendingInstances = busy ? State.PendingInstances.Append(id).Distinct().ToArray() : [.. State.PendingInstances.Where(value => value != id)], });

    private async Task LoadAsync(string? preferredId = null)
    {
        var revision = ++_instanceLoadRevision;
        _detailLoad?.Cancel();
        _detailLoad?.Dispose();
        _detailLoad = null;
        _details.Invalidate();
        try
        {
            var data = await Task.Run(client.ListInstances, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || revision != _instanceLoadRevision)
                return;
            var instances = data.Select(HomeInstanceReader.Summarize).ToArray();
            Publish(State with
            {
                Instances = instances,
                InstancesLoaded = true,
                InstancesRevision = revision,
                InstanceLoadError = string.Empty
            });
            var selected = preferredId ?? State.SelectedId;
            var next = instances.FirstOrDefault(i => i.Id == selected && !i.Unreadable && i.ServerId is null)?.Id ?? instances.FirstOrDefault(i => !i.Unreadable && i.ServerId is null)?.Id ?? string.Empty;
            var detailsRead = Task.CompletedTask;
            if (next != State.SelectedId)
            {
                Select(next);
            }
            else if (next.Length > 0)
            {
                // A refresh of the same instance must not blank its details.
                _detailLoad?.Cancel();
                _detailLoad?.Dispose();
                _detailLoad = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                detailsRead = LoadDetailsAsync(next, _detailLoad.Token);
            }

            // History and profile enrichment must not delay the first usable inventory.
            var recoveryRead = Task.Run(client.InstallationRecovery.ReadPending, _lifetime.Token);
            var summaries = new InstanceSummary[data.Count];
            var summariesRead = Parallel.ForEachAsync(
                Enumerable.Range(0, data.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = _lifetime.Token },
                async (index, ct) => summaries[index] = await HomeInstanceReader.ReadSummaryAsync(client, data[index], ct).ConfigureAwait(false));
            await Task.WhenAll(summariesRead, recoveryRead);
            if (!_lifetime.IsCancellationRequested && revision == _instanceLoadRevision)
                Publish(State with { Instances = summaries, InstallationRecoveries = await recoveryRead });
            await detailsRead;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested && revision == _instanceLoadRevision)
            {
                Publish(State with
                {
                    InstancesLoaded = true,
                    InstanceLoadError = exception.Message
                });
            }
        }
    }

    private async Task LoadDetailsAsync(string id, CancellationToken ct)
    {
        try
        {
            var details = await ReadInstanceDetailsAsync(id, ct);
            if (!ct.IsCancellationRequested && State.SelectedId == id)
                Publish(State with { Details = details, ConfigurationError = string.Empty });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested && State.SelectedId == id)
                Publish(State with { ConfigurationError = exception.Message });
        }
    }

    private async Task ObserveAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            var revision = _observationRevision;
            try
            {
                var states = await Task.Run(() => HomeLaunchReader.Read(client), _lifetime.Token);
                var targets = _problemTargets.Count == 0 ? [State.SelectedId] : _problemTargets.Values.Distinct().ToArray();
                states = await _problems.EnrichAsync(states, targets, _lifetime.Token);
                if (_lifetime.IsCancellationRequested)
                    return;
                if (revision == _observationRevision)
                {
                    PublishLaunches(states);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (_lifetime.IsCancellationRequested)
                    return;
                if (revision == _observationRevision)
                    Publish(State with { LaunchReadError = exception.Message });
            }

            try
            {
                await Task.Delay(500, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task RunLaunchActionAsync()
    {
        if (!State.CanLaunch)
            return;
        await RunLaunchAsync(State.SelectedId, State.CurrentLaunch.TaskActive ? null : new LaunchTask(State.SelectedId, new LaunchOptions()));
    }

    internal Task<string?> StartLaunchAsync(LaunchTask request, bool applyDefaults = true)
    {
        if (_preparingLaunches.Contains(request.InstanceId) || State.PendingInstances.Contains(request.InstanceId) || State.LaunchStates.GetValueOrDefault(request.InstanceId)?.Busy == true)
            throw new InvalidOperationException(Resources.InstanceBusyConflict);
        return RunLaunchAsync(request.InstanceId, request, applyDefaults);
    }

    internal async Task CancelLaunchAsync(string instanceId)
    {
        if (State.LaunchStates.GetValueOrDefault(instanceId) is not { TaskActive: true, TaskOwned: true })
            throw new InvalidOperationException(Resources.InstanceBusyConflict);
        await RunLaunchAsync(instanceId, null);
    }

    private async Task<string?> RunLaunchAsync(string id, LaunchTask? request, bool applyDefaults = true)
    {
        if (request is not null && !_preparingLaunches.Add(id))
            throw new InvalidOperationException(Resources.InstanceBusyConflict);
        var launch = State.LaunchStates.GetValueOrDefault(id) ?? new("idle");
        string? startedJob = null;
        _observationRevision++;
        Publish(State with { PendingInstances = [.. State.PendingInstances, id] });
        ReportError(id, string.Empty);
        try
        {
            if (request is null && launch.TaskActive && launch.TaskOwned && launch.JobId is { } job)
            {
                await Task.Run(() => client.Jobs.CancelAsync(job));
                Publish(State with { CancelRequests = new Dictionary<string, string>(State.CancelRequests) { [id] = job, }, });
            }
            else if (request is not null)
            {
                var cancellation = _lifetime.Token;
                var options = request.Options;
                request = await Task.Run(() => client.CreateLaunchAsync(id, options, cancellation), cancellation);
                if (!request.Options.PlanOnly && request.Options.AccountId is null && RequireLaunchIdentity is { } requireIdentity)
                {
                    var accountId = await requireIdentity(cancellation);
                    if (accountId is null)
                        return null;
                    request = await Task.Run(() => client.CreateLaunchAsync(id, request.Options with
                    {
                        AccountId = accountId,
                        PlayerName = null,
                        InheritIdentity = true
                    }, cancellation), cancellation);
                }

                if (request.Options.ServerAddress is not null && !request.Options.Offline && !request.Options.PlanOnly && PrepareServerConnection is { } prepare)
                {
                    EndOperation(id);
                    if (!await prepare(request, _lifetime.Token))
                        return null;
                    Publish(State with { PendingInstances = [.. State.PendingInstances, id] });
                }

                if (applyDefaults && launchOptions is not null)
                {
                    var defaults = await launchOptions();
                    cancellation.ThrowIfCancellationRequested();
                    request = request with
                    {
                        Options = request.Options with
                        {
                            JvmArguments = [.. defaults.JvmArguments, .. request.Options.JvmArguments],
                            GameLanguage = request.Options.GameLanguage ?? defaults.GameLanguage,
                        }
                    };
                }

                var queued = await Task.Run(() => client.Tasks.StartAsync(request));
                startedJob = queued.Id!;
            }

            var revision = ++_observationRevision;
            var states = await Task.Run(() => HomeLaunchReader.Read(client));
            if (!_lifetime.IsCancellationRequested && revision == _observationRevision)
                PublishLaunches(states);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
                ReportError(id, exception.Message);
        }
        finally
        {
            if (request is not null)
                _preparingLaunches.Remove(id);
            if (!_lifetime.IsCancellationRequested)
                EndOperation(id);
        }

        return startedJob;
    }

    internal void ReportError(string id, string error)
    {
        var values = new Dictionary<string, string>(State.ActionErrors);
        if (error.Length == 0)
            values.Remove(id);
        else
            values[id] = error;
        Publish(State with { ActionErrors = values });
    }

    internal void PublishLaunches(IReadOnlyDictionary<string, HomeLaunchState> states)
    {
        // Window-wait progress is clock-derived and must advance until the real window is observed.
        if (states.Count == State.LaunchStates.Count &&
            states.All(pair => !pair.Value.WaitingForWindow &&
                State.LaunchStates.TryGetValue(pair.Key, out var previous) && previous == pair.Value))
        {
            states = State.LaunchStates;
        }

        Publish(State with
        {
            LaunchStates = states,
            LaunchReadError = string.Empty,
            LaunchObserved = true
        });
        foreach (var (id, launch) in states)
        {
            if (launch is not { SessionOwned: true, Status: "running", GameWindowVisible: true, SessionId: { } session } || !_handledSessions.Add(session))
            {
                continue;
            }

            try
            {
                GameStarted?.Invoke(id);
            }
            catch (Exception exception)
            {
                ReportError(id, exception.Message);
            }
        }
    }

    private void EndOperation(string id) => Publish(State with { PendingInstances = [.. State.PendingInstances.Where(value => value != id)], });

    /// <summary>Cancels observation, shared reads and the window's creation/import workflows.</summary>
    public void Dispose()
    {
        Import.Completed -= OnImported;
        Import.Dispose();
        Create.Completed -= OnImported;
        Create.StateChanged -= OnCreateStateChanged;
        Create.Dispose();
        _lifetime.Cancel();
        _detailLoad?.Cancel();
        _detailLoad?.Dispose();
        _details.Dispose();
        _lifetime.Dispose();
    }
}