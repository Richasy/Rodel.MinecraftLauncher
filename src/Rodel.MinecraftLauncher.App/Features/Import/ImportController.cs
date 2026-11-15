// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.App.Features.Import;

/// <summary>Owns one preview/association attempt; Core alone identifies and registers the source.</summary>
internal sealed class ImportController(LauncherClient client) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private long _previewRevision;
    private CancellationTokenSource? _previewDebounce;
    private string? _jobId;
    private string? _serverId;
    internal ImportState State { get; private set; } = new()
    {
        DestinationDirectory = client.Paths.InstancesDirectory
    };
    internal event Action<ImportState>? StateChanged;
    internal event Action<string?>? Completed;

    private void Publish(ImportState state)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        State = state;
        StateChanged?.Invoke(state);
    }

    internal void Reset(string? serverId = null)
    {
        if (State.Active)
            return;
        _serverId = serverId;
        CancelScheduledPreview();
        _previewRevision++;
        _jobId = null;
        Publish(new() { DestinationDirectory = client.Paths.InstancesDirectory });
    }

    internal void ChangeDirectory(string value)
    {
        if (State.Active || State.RegisteredId is not null)
            return;
        _previewRevision++;
        Publish(new()
        {
            Directory = value,
            DestinationDirectory = client.Paths.InstancesDirectory
        });
        SchedulePreview();
    }

    internal void ChangeName(string value) => Publish(State with { Name = value });

    internal void ChangeVersion(string value)
    {
        Publish(State with { Version = value });
        SchedulePreview();
    }

    internal void ChangeSharedDirectory(string value)
    {
        Publish(State with { SharedInput = value });
        SchedulePreview();
    }

    internal void ChangeGameDirectory(string value)
    {
        Publish(State with { GameInput = value });
        SchedulePreview();
    }

    internal void ChangeLauncherConfig(string value)
    {
        Publish(State with { LauncherConfig = value });
        SchedulePreview();
    }

    private void CancelScheduledPreview()
    {
        _previewDebounce?.Cancel();
        _previewDebounce?.Dispose();
        _previewDebounce = null;
    }

    private void SchedulePreview()
    {
        CancelScheduledPreview();
        var revision = ++_previewRevision;
        var hasPath = !string.IsNullOrWhiteSpace(State.Directory);
        Publish(State with
        {
            PreviewDirty = hasPath,
            Previewing = hasPath,
            Error = string.Empty,
            ErrorCode = string.Empty
        });
        if (!hasPath)
            return;
        _previewDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = RecognizeAfterEditingAsync(revision, _previewDebounce.Token);
    }

    private async Task RecognizeAfterEditingAsync(long revision, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (revision == _previewRevision && !token.IsCancellationRequested)
                await PreviewAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    internal Task SelectVersionAsync(string value) => PreviewAsync(version: value);

    internal Task SelectGameDirectoryAsync(string value) => PreviewAsync(gameDirectory: value);

    internal void ReportError(string error) => Publish(State with
    {
        Error = error,
        ErrorCode = string.Empty
    });

    internal async Task PreviewAsync(string? profile = null, string? version = null, string? gameDirectory = null)
    {
        if (State.Active || string.IsNullOrWhiteSpace(State.Directory))
            return;
        CancelScheduledPreview();
        var revision = ++_previewRevision;
        var source = State.Directory.Trim();
        var selectedProfile = profile ?? (State.Profile.Length > 0 ? State.Profile : null);
        var selectedVersion = version ?? (profile is not null && profile != State.Profile ? null : NullIfEmpty(State.Version));
        var selectedGame = gameDirectory ?? NullIfEmpty(State.GameInput);
        var shared = NullIfEmpty(State.SharedInput);
        var config = NullIfEmpty(State.LauncherConfig);
        Publish(State with
        {
            Previewing = true,
            Preview = null,
            Job = null,
            Error = string.Empty,
            RegisteredId = null,
        });
        try
        {
            var preview = await Task.Run(() => client.Instances.PreviewImport(
                    source,
                    selectedProfile,
                    shared,
                    config,
                    selectedVersion,
                    selectedGame), _lifetime.Token);
            if (revision != _previewRevision || _lifetime.IsCancellationRequested)
                return;
            Publish(State with
            {
                Directory = source,
                PreviewDirty = false,
                GameInput = selectedGame ?? string.Empty,
                Preview = preview,
                Previewing = false,
                Version = preview.VersionId ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(State.Name) ? preview.Name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(source)) : State.Name,
            });
        }
        catch (Exception exception)
        {
            if (revision == _previewRevision)
            {
                Publish(State with
                {
                    Previewing = false,
                    Error = exception.Message,
                    ErrorCode = FailureCode(exception),
                });
            }
        }
    }

    internal async Task ConfirmAsync()
    {
        if (!State.CanConfirm)
            return;
        var draft = State;
        _jobId = null;
        Publish(State with
        {
            Active = true,
            Error = string.Empty,
            CancelRequested = false
        });
        try
        {
            var before = await Task.Run(() => client.Instances.List().Select(i => i.Id).ToHashSet(), _lifetime.Token);
            var job = await Task.Run(() => client.Tasks.StartAsync(new ImportInstanceTask(draft.Name.Trim(), draft.Directory, new InstanceImportOptions
            {
                ServerId = _serverId,
                Migrate = true,
                GameDirectory = NullIfEmpty(draft.GameInput),
                MinecraftDirectory = NullIfEmpty(draft.SharedInput),
                LauncherConfiguration = NullIfEmpty(draft.LauncherConfig),
                Copy = true,
                Profile = draft.Profile.Length > 0 ? draft.Profile : null,
                VersionId = string.IsNullOrWhiteSpace(draft.Version) ? null : draft.Version.Trim(),
            })), _lifetime.Token);
            _jobId = job.Id!;
            Publish(State with { Job = job });
            if (State.CancelRequested)
                await CancelAsync();
            await ObserveAsync(before, draft);
        }
        catch (Exception exception)
        {
            // Submission failures do not authorize source cleanup or automatic replay.
            Publish(State with
            {
                Active = false,
                Error = exception.Message,
                ErrorCode = FailureCode(exception),
            });
        }
    }

    private async Task ObserveAsync(HashSet<string> before, ImportState draft)
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                var job = await Task.Run(() => client.Jobs.Get(_jobId!), _lifetime.Token);
                Publish(State with
                {
                    Job = job,
                    Error = string.Empty
                });
                if (job.Status is "succeeded" or "failed" or "canceled" or "interrupted")
                {
                    var registered = await Task.Run(() => client.Instances.List().Where(i => !before.Contains(i.Id) && i.Name == draft.Name.Trim() && i.Managed).Select(i => i.Id).ToArray(), _lifetime.Token);
                    var id = job.Result?["id"]?.ToString() ?? (registered.Length == 1 ? registered[0] : null);
                    Publish(State with
                    {
                        Active = false,
                        RegisteredId = id,
                        Error = job.Error ?? string.Empty,
                        ErrorCode = job.Failure?["error"]?["code"]?.ToString() ?? string.Empty,
                    });
                    Completed?.Invoke(id);
                    return;
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A failed observation is not a failed import. Keep owning/observing this job.
                Publish(State with
                {
                    Error = string.Format(Resources.ImportObservationError, exception.Message),
                    ErrorCode = "OBSERVATION",
                });
            }

            try
            {
                await Task.Delay(150, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
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
                Error = exception.Message,
                ErrorCode = FailureCode(exception),
            });
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FailureCode(Exception exception) => LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString() ?? string.Empty;

    public void Dispose()
    {
        CancelScheduledPreview();
        _previewRevision++;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}