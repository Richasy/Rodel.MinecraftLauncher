// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Installation;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Create;

internal sealed class CreateController(LauncherClient client) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private string? _jobId;
    private long _selectionRevision;
    internal CreateState State { get; private set; } = new();
    internal event Action<CreateState>? StateChanged;
    internal event Action<string?>? Completed;
    internal string Destination => client.Paths.InstancesDirectory;
    internal string? ServerId { get; private set; }

    private void Publish(CreateState state)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        State = state;
        StateChanged?.Invoke(state);
    }

    internal void Open(string? instanceId = null, string? serverId = null, bool fresh = false)
    {
        if (State.Active)
            return;
        if (ServerId != serverId)
        {
            _selectionRevision++;
            _jobId = null;
            Publish(new() { Catalog = State.Catalog });
        }

        ServerId = serverId;
        _ = RestoreAsync(instanceId, fresh);
    }

    private InstanceInstallationRecovery[] ReadRecoveries(string? serverId) => [.. client.InstallationRecovery.ReadPending().Where(item => client.Instances.Get(item.InstanceId).ServerId == serverId)];

    private async Task RestoreAsync(string? preferredId, bool fresh = false)
    {
        if (State.Loading)
            return;
        var revision = ++_selectionRevision;
        var ownerId = ServerId;
        Publish(State with { Loading = true });
        try
        {
            var pending = await Task.Run(() => ReadRecoveries(ownerId), _lifetime.Token);
            if (revision != _selectionRevision || _lifetime.IsCancellationRequested)
                return;
            var selected = fresh ? null : pending.FirstOrDefault(item => item.InstanceId == (preferredId ?? State.InstanceId)) ?? (pending is [] ? null : pending[0]);
            VersionCatalog? cached = null;
            try
            {
                cached = await Task.Run(() => client.Installation.GetVersionsAsync(offline: true, cancellationToken: _lifetime.Token));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
            }

            if (revision != _selectionRevision || _lifetime.IsCancellationRequested)
                return;
            Publish(State with
            {
                Recoveries = pending,
                Catalog = cached?.Versions is { Count: > 0 } ? cached : State.Catalog,
                Loading = false
            });
            if (fresh)
                StartNew();
            else if (selected is not null)
                await SelectRecoveryAsync(selected);
            else if (State.Succeeded || State.InstanceId is not null)
                StartNew();
            else
                SelectDefault();
            await LoadVersionsAsync();
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Loading = false,
                Error = exception.Message
            });
        }
    }

    internal Task SelectRecoveryAsync(int index) => index >= 0 && index < State.Recoveries.Count ? SelectRecoveryAsync(State.Recoveries[index]) : Task.CompletedTask;

    private async Task SelectRecoveryAsync(InstanceInstallationRecovery recovery)
    {
        if (State.Active)
            return;
        var revision = ++_selectionRevision;
        Publish(State with { Loading = true });
        try
        {
            var cancellation = _lifetime.Token;
            var owner = await Task.Run(() => client.Instances.Get(recovery.InstanceId).ServerId, cancellation);
            if (revision != _selectionRevision || cancellation.IsCancellationRequested)
                return;
            if (owner != ServerId)
                throw new InvalidOperationException(Resources.ServerClientOwnershipChanged);
            var job = await Task.Run(() => client.Jobs.Get(recovery.JobId), _lifetime.Token);
            if (revision != _selectionRevision || _lifetime.IsCancellationRequested)
                return;
            if (job.Status is not ("failed" or "canceled" or "interrupted"))
            {
                Publish(State with { Loading = false });
                await RestoreAsync(null);
                return;
            }

            _jobId = recovery.JobId;
            Publish(State with
            {
                Name = recovery.Name,
                Version = recovery.Version,
                VersionText = recovery.Version,
                InstanceId = recovery.InstanceId,
                Job = job,
                Activity = null,
                Loading = false,
                Error = job.Status == "canceled" ? string.Empty : job.Error ?? string.Empty
            });
            await client.Activity.WriteAsync("recovery.selected", recovery.InstanceId, recovery.JobId);
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Loading = false,
                Error = exception.Message
            });
        }
    }

    internal void StartNew()
    {
        if (State.Active)
            return;
        _selectionRevision++;
        _jobId = null;
        Publish(new()
        {
            Catalog = State.Catalog,
            Recoveries = State.Recoveries,
            RefreshingCatalog = State.RefreshingCatalog
        });
        if (State.Catalog?.Versions is { Count: > 0 })
            SelectDefault();
        else
            _ = LoadVersionsAsync();
    }

    internal void ChangeName(string name)
    {
        if (!State.Active && !State.Loading && State.InstanceId is null)
            Publish(State with { Name = name });
    }

    internal void ChangeVersionText(string text)
    {
        if (State.Active || State.Loading || State.InstanceId is not null)
            return;
        var match = State.Versions.FirstOrDefault(version => version.Equals(text, StringComparison.OrdinalIgnoreCase));
        Publish(State with
        {
            VersionText = text,
            Version = match ?? string.Empty,
            VersionValidationRequested = false
        });
    }

    internal void ValidateVersion() => Publish(State with { VersionValidationRequested = true });

    private void SelectDefault()
    {
        if (State.Version.Length != 0 || State.VersionText.Length != 0)
            return;
        var latest = State.Catalog?.Latest?["release"]?.ToString();
        var version = latest is not null && State.Versions.Contains(latest) ? latest : State.Versions.FirstOrDefault() ?? string.Empty;
        Publish(State with
        {
            Version = version,
            VersionText = version
        });
    }

    internal async Task LoadVersionsAsync(bool refresh = false)
    {
        if (State.Active || State.Loading || State.RefreshingCatalog)
            return;
        Publish(State with
        {
            Loading = State.Catalog?.Versions is not { Count: > 0 },
            RefreshingCatalog = true,
            Error = string.Empty
        });
        try
        {
            var catalog = await Task.Run(() => client.Installation.GetVersionsAsync(cancellationToken: _lifetime.Token, refresh: refresh));
            Publish(State with
            {
                Catalog = catalog,
                Loading = false,
                RefreshingCatalog = false
            });
            if (State.InstanceId is null)
                Publish(State with { Version = State.Versions.FirstOrDefault(version => version.Equals(State.VersionText, StringComparison.OrdinalIgnoreCase)) ?? string.Empty });
            SelectDefault();
            await client.Activity.WriteAsync("versions.loaded", code: catalog.Cached ? "CACHE" : "REMOTE", count: catalog.Versions?.Count);
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Loading = false,
                RefreshingCatalog = false,
                Error = exception.Message
            });
            await client.Activity.WriteAsync("versions.failed", code: LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString());
        }
    }

    internal async Task ConfirmAsync()
    {
        if (!State.CanConfirm)
            return;
        var previousJob = State.Job?.Id;
        Publish(State with
        {
            Active = true,
            CancelRequested = false,
            Error = string.Empty,
            Job = null,
            Activity = null
        });
        _jobId = null;
        try
        {
            if (State.InstanceId is null)
            {
                var instance = await Task.Run(() => client.Instances.CreateAsync(State.Name.Trim(), State.Version, _lifetime.Token));
                Publish(State with { InstanceId = instance.Id });
                if (ServerId is not null)
                    await client.Servers.SetClientOwnerAsync(instance.Id, ServerId, ct: _lifetime.Token);
            }

            var job = await Task.Run(() => previousJob is not null ? client.Tasks.ResumeAsync(previousJob) : client.Tasks.StartAsync(new InstallInstanceTask(State.InstanceId!, new() { PrepareJava = true })));
            ArgumentNullException.ThrowIfNull(job.Id);
            _jobId = job.Id;
            Publish(State with { Job = job });
            if (State.CancelRequested)
                await CancelAsync();
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Active = false,
                Error = exception.Message
            });
            await client.Activity.WriteAsync("installation.submission-failed", State.InstanceId, code: LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString());
            Completed?.Invoke(State.InstanceId);
            return;
        }

        await ObserveAsync();
    }

    private async Task ObserveAsync()
    {
        var observedJob = _jobId;
        var ownerId = ServerId;
        var observationFailed = false;
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                var job = await Task.Run(() => client.Jobs.Get(observedJob!), _lifetime.Token);
                if (_jobId != observedJob || ServerId != ownerId)
                    return;
                var terminal = job.Status is "succeeded" or "failed" or "canceled" or "interrupted";
                Publish(State with
                {
                    Job = job,
                    Active = !terminal,
                    Error = job.Status == "canceled" ? string.Empty : job.Error ?? string.Empty
                });
                if (observationFailed)
                    await client.Activity.WriteAsync("observation.recovered", State.InstanceId, _jobId);
                observationFailed = false;
                if (terminal)
                {
                    var pending = await Task.Run(() => ReadRecoveries(ownerId), _lifetime.Token);
                    if (_jobId != observedJob || ServerId != ownerId)
                        return;
                    Publish(State with { Recoveries = pending });
                    Completed?.Invoke(State.InstanceId);
                    return;
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Publish(State with { Error = exception.Message });
                if (!observationFailed)
                    await client.Activity.WriteAsync("observation.failed", State.InstanceId, _jobId, code: LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString());
                observationFailed = true;
            }

            try
            {
                await Task.Delay(200, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task LoadActivityAsync()
    {
        try
        {
            var entries = await Task.Run(() => client.Activity.ReadAsync(State.InstanceId, State.InstanceId is null ? _jobId : null, cancellationToken: _lifetime.Token));
            Publish(State with { Activity = entries });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
                Publish(State with { Activity = [new LauncherActivityEntry { Code = "ACTIVITY_LOG_UNAVAILABLE" }] });
        }
    }

    internal async Task CancelAsync()
    {
        if (!State.Active)
            return;
        Publish(State with { CancelRequested = true });
        if (_jobId is null)
            return;
        try
        {
            await Task.Run(() => client.Jobs.CancelAsync(_jobId), _lifetime.Token);
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                CancelRequested = false,
                Error = exception.Message
            });
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}