// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Tasks;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Packs;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed record PackVersionChoice(string Id, string Name);

internal sealed record PackMaintenanceState
{
    internal string Name { get; init; } = string.Empty;
    internal string Archive { get; init; } = string.Empty;
    internal string ManualDirectory { get; init; } = string.Empty;
    internal bool UseSource { get; init; }
    internal bool Download { get; init; } = true;
    internal bool PrepareRuntime { get; init; } = true;
    internal bool Active { get; init; }
    internal bool CancelRequested { get; init; }
    internal PackContentState? Pack { get; init; }
    internal PackReview? Review { get; init; }
    internal PackOperationList? History { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string Progress { get; init; } = string.Empty;
    internal string Report { get; init; } = string.Empty;
    internal string Channel { get; init; } = "release";
    internal string VersionId { get; init; } = string.Empty;
    internal IReadOnlyList<PackVersionChoice> Versions { get; init; } = [];
    internal bool ReadingVersions { get; init; }
    internal bool OnlineSource => Pack?.OnlineSource is not null && Pack.Format != "hmcl";
    internal bool HasSource => Pack?.SourceDetached != true && (Pack?.HasDistributionSource == true || Pack?.OnlineSource is not null);
    internal bool CanReview => !Active && (UseSource ? HasSource && (!OnlineSource || (!ReadingVersions && VersionId.Length > 0)) : Archive.Trim().Length > 0);
    internal bool CanApply => !Active && Review is { Phase: "reviewed", Incoming.Complete: true, HasChanges: true };
    internal string? RollbackId => History?.Operations.FirstOrDefault(item => item.Current && item.Phase == "applied")?.OperationId;
    internal string CurrentVersion => Pack?.VersionId?.ToString() ?? string.Empty;
    internal string TargetVersion => Review?.Incoming.VersionId?.ToString() ?? string.Empty;
    internal int MissingCount => Review?.Incoming.Missing.Count ?? 0;
    internal int ReadWarnings => (History?.Warnings.Count ?? 0) + (Review?.ChangeSummary.Warnings.Count ?? 0);
}

/// <summary>Coordinates exact pack review and existing typed application/rollback tasks without duplicating pack business.</summary>
internal sealed class PackMaintenanceController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly string _instanceId;
    private readonly FeatureTaskController _tasks;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _working;
    private bool _sourceSelected;
    private CancellationTokenSource? _versionsRead;
    private string _versionsProject = string.Empty;

    internal PackMaintenanceController(LauncherClient client, HomeController home, string instanceId)
    {
        _client = client;
        _home = home;
        _instanceId = instanceId;
        _tasks = new(client, home);
        _tasks.Changed += OnTaskChanged;
    }

    internal PackMaintenanceState State { get; private set; } = new();
    internal bool Succeeded { get; private set; }
    internal event Action<PackMaintenanceState>? Changed;

    private void Publish(PackMaintenanceState state)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            State = state;
            Changed?.Invoke(state);
        }
    }

    internal void SetArchive(string value)
    {
        if (!State.Active)
        {
            Publish(State with
            {
                Archive = value,
                Review = null,
                Error = string.Empty
            });
        }
    }

    internal void SetManual(string value)
    {
        if (!State.Active)
        {
            Publish(State with
            {
                ManualDirectory = value,
                Review = null,
                Error = string.Empty
            });
        }
    }

    internal void SetSource(bool value)
    {
        if (State.Active)
            return;
        _sourceSelected = true;
        if (!value)
            _versionsRead?.Cancel();
        Publish(State with
        {
            UseSource = value,
            Review = null,
            Error = string.Empty
        });
        if (value && State.OnlineSource && State.Versions.Count == 0)
            _ = ReadVersionsAsync();
    }

    internal void SetMode(int index) => SetSource(index == 1);

    internal void SetChannel(int index)
    {
        var channel = index == 1
            ? "beta"
            : index == 2 ? "alpha" : "release";
        if (State.Active || State.Channel == channel)
            return;
        Publish(State with
        {
            Channel = channel,
            Review = null
        });
        _ = ReadVersionsAsync();
    }

    internal void SetVersion(int index)
    {
        if (State.Active || State.ReadingVersions || index < 0 || index >= State.Versions.Count)
            return;
        var version = State.Versions[index].Id;
        if (State.VersionId != version)
        {
            Publish(State with
            {
                VersionId = version,
                Review = null,
                Error = string.Empty
            });
        }
    }

    internal void ReloadVersions() => _ = ReadVersionsAsync();

    internal void SetDownload(bool value)
    {
        if (!State.Active)
        {
            Publish(State with
            {
                Download = value,
                PrepareRuntime = value && State.PrepareRuntime,
                Review = null,
                Error = string.Empty
            });
        }
    }

    internal void SetRuntime(bool value)
    {
        if (!State.Active)
            Publish(State with { PrepareRuntime = value });
    }

    internal void ReportError(Exception exception) => Publish(State with { Error = exception.Message });

    internal void Reselect()
    {
        if (!State.Active)
        {
            Publish(State with
            {
                Review = null,
                Error = string.Empty
            });
        }
    }

    internal async Task OpenAsync()
    {
        Publish(State with { Active = true });
        try
        {
            await RefreshAsync();
            if (State.UseSource && State.OnlineSource)
                await ReadVersionsAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            Publish(State with { Active = false });
        }
    }

    private async Task RefreshAsync()
    {
        var (pack, history) = await Task.Run(() => (Pack: _client.Packs.GetState(_instanceId), History: _client.Packs.GetOperations(_instanceId)), _lifetime.Token);
        var state = State with
        {
            Name = pack.Instance?.Name ?? string.Empty,
            Pack = pack.Pack,
            History = history,
        };
        Publish(state with { UseSource = _sourceSelected ? state.UseSource && state.HasSource : state.HasSource });
        if (state.Pack?.OnlineSource?.ProjectId != _versionsProject)
        {
            _versionsRead?.Cancel();
            var channel = state.Pack?.OnlineSource?.Channel;
            Publish(State with
            {
                Versions = [],
                VersionId = string.Empty,
                Channel = channel is "release" or "beta" or "alpha" ? channel : "release"
            });
        }
    }

    private async Task ReadVersionsAsync()
    {
        _versionsRead?.Cancel();
        if (!State.HasSource || State.Pack?.OnlineSource?.ProjectId is not { Length: > 0 } project)
            return;
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = reading.Token;
        _versionsRead = reading;
        _versionsProject = project;
        var channel = State.Channel;
        Publish(State with
        {
            ReadingVersions = true,
            Versions = [],
            VersionId = string.Empty,
            Review = null,
            Error = string.Empty
        });
        try
        {
            var versions = await Task.Run(() => _client.OnlinePacks.GetVersionsAsync(project, channel, token), token);
            if (reading.IsCancellationRequested || !ReferenceEquals(_versionsRead, reading))
                return;
            var choices = versions.Select(version => new PackVersionChoice(version.Id, version.DisplayName)).ToArray();
            Publish(State with
            {
                Versions = choices,
                VersionId = choices.FirstOrDefault()?.Id ?? string.Empty,
                Error = choices.Length == 0 ? Resources.ContentVersionsEmpty : string.Empty
            });
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_versionsRead, reading))
                ReportError(exception);
        }
        finally
        {
            if (ReferenceEquals(_versionsRead, reading))
            {
                _versionsRead = null;
                Publish(State with { ReadingVersions = false });
            }
        }
    }

    private void OnTaskChanged() => Publish(State with
    {
        Active = _working || _tasks.Active,
        CancelRequested = _tasks.CancelRequested,
        Error = _tasks.Error,
        Progress = _tasks.Progress,
        Report = _tasks.Report
    });

    internal Task ReviewAsync()
    {
        if (!State.CanReview)
            return Task.CompletedTask;
        LauncherTaskRequest request = State.UseSource
            ? State.Pack?.Format == "hmcl" ? new ReviewPackDistributionTask(_instanceId) : new ReviewOnlinePackTask(_instanceId, new()
            {
                VersionId = State.VersionId,
                Channel = State.Channel
            })
            : new ReviewPackUpdateTask(_instanceId, State.Archive.Trim(), new()
            {
                Offline = !State.Download,
                ManualDirectory = string.IsNullOrWhiteSpace(State.ManualDirectory) ? null : State.ManualDirectory.Trim()
            });
        Publish(State with { Review = null });
        return RunAsync(request, reviewing: true);
    }

    internal Task ApplyAsync() => State.CanApply ? RunAsync(new ApplyPackUpdateTask(State.Review!.OperationId, new() { ContentOnly = !State.PrepareRuntime })) : Task.CompletedTask;

    internal Task RollbackAsync(string operationId)
    {
        if (State.Active)
            return Task.CompletedTask;
        if (State.RollbackId == operationId)
            return RunAsync(new RollbackPackUpdateTask(operationId));
        Publish(State with { Error = Resources.PackUpdateReviewChanged });
        return Task.CompletedTask;
    }

    private async Task RunAsync(LauncherTaskRequest request, bool reviewing = false)
    {
        Succeeded = false;
        _working = true;
        Publish(State with
        {
            Active = true,
            Error = string.Empty,
            Progress = string.Empty
        });
        try
        {
            await _tasks.RunAsync(request, _instanceId, refreshHome: false);
            var result = _tasks.Job?.Result ?? _tasks.Job?.Failure?["data"];
            if (result is JsonObject review && review["operationId"] is not null)
                Publish(State with { Review = review.Deserialize(PackJsonContext.Default.PackReview) });
            else if (reviewing)
                Publish(State with { Review = null });
            await RefreshAsync();
            Succeeded = _tasks.Outcome == "succeeded";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            _working = false;
            Publish(State with
            {
                Active = false,
                CancelRequested = false
            });
        }

        await _home.RefreshAsync();
    }

    internal async Task DetachAsync()
    {
        if (State.Active)
            return;
        Succeeded = false;
        Publish(State with
        {
            Active = true,
            Error = string.Empty
        });
        try
        {
            await _client.Packs.DetachSourceAsync(_instanceId, _lifetime.Token);
            Publish(State with
            {
                UseSource = false,
                Review = null
            });
            await RefreshAsync();
            Succeeded = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            Publish(State with { Active = false });
        }
    }

    internal Task CancelAsync()
    {
        _versionsRead?.Cancel();
        return _tasks.CancelAsync();
    }

    /// <summary>Releases this observer; running tasks retain their durable Core state and are not reported as completed by disposal.</summary>
    public void Dispose()
    {
        _tasks.Changed -= OnTaskChanged;
        _tasks.Dispose();
        _versionsRead?.Cancel();
        _versionsRead?.Dispose();
        _versionsRead = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}