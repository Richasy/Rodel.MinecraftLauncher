// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Tasks;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed record PackOptionalEntry(int Index, string Path);

internal sealed record PackImportState
{
    internal string Source { get; init; } = string.Empty;
    internal long? ArchiveBytes { get; init; }
    internal string Name { get; init; } = string.Empty;
    internal string ManualDirectory { get; init; } = string.Empty;
    internal bool Download { get; init; } = true;
    internal bool PrepareRuntime { get; init; } = true;
    internal bool Reading { get; init; }
    internal bool Running { get; init; }
    internal PackInspection? Inspection { get; init; }
    internal IReadOnlySet<string> Excluded { get; init; } = new HashSet<string>();
    internal string Error { get; init; } = string.Empty;
    internal string Outcome { get; init; } = string.Empty;
    internal string Status { get; init; } = string.Empty;
    internal string Progress { get; init; } = string.Empty;
    internal string Report { get; init; } = string.Empty;
    internal bool CancelRequested { get; init; }
    internal bool Active => Reading || Running;
    internal string RetainedInstanceId { get; init; } = string.Empty;
    internal PackState? Result { get; init; }
    internal bool Existing { get; init; }
    internal bool Submitted { get; init; }
    internal PackPreparation? Preparation { get; init; }
    internal LauncherProgress? TaskProgress { get; init; }
    internal IReadOnlyList<PackMissingFile> MissingFiles { get; init; } = [];
    internal bool HasWorkflow => Submitted || Existing || RetainedInstanceId.Length > 0;
    internal bool Completed => Preparation?.Status is "completed" or "completed-with-notices" or "content-only";
    internal bool HasNotices => Preparation?.Status == "completed-with-notices";
    internal bool HasMissing => Preparation?.Steps[0].Status != "completed" && Result?.Missing.Count > 0;
    internal bool RuntimePrepared => Result?.RuntimePrepared == true;
    internal bool CanContinue => !Completed;
    internal bool HasPack => Inspection is not null || Result?.Pack is not null;
    internal bool CanImport => !Active && HasPack && CanContinue;
    internal PackOptionalEntry[] OptionalFiles => Inspection?.OptionalFiles.Select((file, index) => new PackOptionalEntry(index, file.Path!)).ToArray() ?? [];
}

internal sealed class PackImportController : IDisposable
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly FeatureTaskController _tasks;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _preflight;
    private bool _importing;
    private string _fingerprint = string.Empty;
    private readonly string? _serverId;

    internal PackImportController(LauncherClient client, HomeController home, string? serverId = null)
    {
        _client = client;
        _home = home;
        _serverId = serverId;
        _tasks = new(client, home);
        _tasks.Changed += OnTaskChanged;
    }

    internal string Destination => _client.Paths.InstancesDirectory;
    internal PackImportState State { get; private set; } = new();
    internal event Action<PackImportState>? Changed;

    /// <summary>Starts a fresh inactive draft without removing any retained instance or prepared files.</summary>
    internal void ResetDraft()
    {
        if (State.Active) return;
        _fingerprint = string.Empty;
        Publish(new());
    }

    private void Publish(PackImportState state)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            State = state;
            Changed?.Invoke(state);
        }
    }

    internal void SetSource(string value)
    {
        if (State.Active || value == State.Source)
            return;
        if (State.RetainedInstanceId.Length > 0)
        {
            Publish(State with { Source = value, ArchiveBytes = null });
            return;
        }

        _fingerprint = string.Empty;
        Publish(State with
        {
            Source = value,
            ArchiveBytes = null,
            Name = State.Name == State.Inspection?.Name?.ToString() ? string.Empty : State.Name,
            Inspection = null,
            Result = null,
            Preparation = null,
            Submitted = false,
            MissingFiles = [],
            Excluded = new HashSet<string>(),
            Error = string.Empty,
            Outcome = string.Empty,
            Report = string.Empty,
            Status = string.Empty
        });
    }

    internal void SetName(string value)
    {
        if (!State.Active)
            Publish(State with { Name = value });
    }

    internal void SetManualDirectory(string value)
    {
        if (!State.Active)
            Publish(State with { ManualDirectory = value });
    }

    internal void SetDownload(bool value)
    {
        if (!State.Active)
            Publish(State with { Download = value });
    }

    internal void SetPrepareRuntime(bool value)
    {
        if (!State.Active)
            Publish(State with { PrepareRuntime = value });
    }

    internal void ToggleOptional(string path)
    {
        if (State.Active)
            return;
        var excluded = State.Excluded.ToHashSet();
        if (!excluded.Add(path))
            excluded.Remove(path);
        Publish(State with { Excluded = excluded });
    }

    internal void ReportError(Exception exception) => Publish(State with { Error = exception.Message });

    internal async Task OpenAsync(string instanceId)
    {
        Publish(State with
        {
            Existing = true,
            Reading = true,
            RetainedInstanceId = instanceId
        });
        try
        {
            var result = await _client.InspectPackPreparationAsync(instanceId, _lifetime.Token);
            Publish(State with
            {
                Reading = false,
                Result = result,
                Preparation = result.Preparation,
                Inspection = null,
                Source = result.Pack?.ArchivePath ?? string.Empty,
                Name = result.Instance?.Name ?? string.Empty,
                Download = result.Pack?.Acquisition?.NetworkAllowed ?? true,
                MissingFiles = result.Missing,
                Report = JsonSerializer.SerializeToNode(result, PackJsonContext.Default.PackState)!.ToJsonString(new() { WriteIndented = true })
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Reading = false,
                Error = exception.Message
            });
        }
    }

    internal async Task InspectAsync()
    {
        if (State.Active || string.IsNullOrWhiteSpace(State.Source))
            return;
        Publish(State with
        {
            Reading = true,
            Inspection = null,
            Error = string.Empty,
            Outcome = string.Empty,
            Report = string.Empty,
            Status = string.Empty
        });
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _preflight = reading;
        try
        {
            var source = State.Source.Trim();
            var (Info, Hash, Bytes) = await Task.Run(async () =>
                {
                    using var file = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, reading.Token));
                    return (Info: _client.Packs.Inspect(source), Hash: hash, Bytes: file.Length);
                }, _lifetime.Token);
            _fingerprint = Hash;
            Publish(State with
            {
                ArchiveBytes = Bytes,
                Source = source,
                Inspection = Info,
                Name = State.Name.Length == 0 ? Info.Name?.ToString() ?? string.Empty : State.Name,
                Excluded = new HashSet<string>(),
                Reading = false
            });
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
            Publish(State with
            {
                Reading = false,
                Outcome = "canceled",
                CancelRequested = false,
                Status = Resources.FeatureTaskCanceled
            });
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Reading = false,
                Error = exception.Message
            });
        }
        finally
        {
            if (ReferenceEquals(_preflight, reading))
                _preflight = null;
        }
    }

    private void OnTaskChanged()
    {
        var result = ReadResult();
        var raw = _tasks.Job?.Result ?? _tasks.Job?.Failure?["data"];
        var preparation = raw?["preparation"]?.Deserialize(PackJsonContext.Default.PackPreparation) ?? _tasks.Job?.Progress?.PackPreparation ?? State.Preparation;
        Publish(State with
        {
            Running = _tasks.Active || _importing,
            CancelRequested = _tasks.CancelRequested,
            Error = _tasks.Error,
            Outcome = _tasks.Outcome,
            Status = _tasks.Status,
            Progress = _tasks.Progress,
            Report = new JsonObject
            {
                ["job"] = JsonSerializer.SerializeToNode(_tasks.Job, ApplicationJsonContext.Job),
                ["error"] = _tasks.Error,
                ["packState"] = JsonSerializer.SerializeToNode(result, PackJsonContext.Default.PackState)
            }.ToJsonString(new() { WriteIndented = true }),
            Result = result,
            Preparation = preparation,
            TaskProgress = _tasks.Job?.Progress,
            MissingFiles = result?.Missing is { Count: > 0 } missing ? missing : State.MissingFiles,
            RetainedInstanceId = result?.Instance?.Id ?? preparation?.InstanceId ?? State.RetainedInstanceId
        });
    }

    private PackState? ReadResult()
    {
        var result = _tasks.Job?.Result ?? _tasks.Job?.Failure?["data"];
        if (result?["instance"] is not null)
            return result.Deserialize(PackJsonContext.Default.PackState);
        if (result is null || State.RetainedInstanceId.Length == 0)
            return State.Result;
        var retained = State.Result is { } previous ? previous with
        {
        }
            : new PackState();
        if (result["preparation"] is { } preparation)
            retained.Preparation = preparation.Deserialize(PackJsonContext.Default.PackPreparation);
        if (result["incoming"] is { } incoming)
        {
            retained.Pack = incoming.Deserialize(PackJsonContext.Default.PackContentState)!;
            retained.Missing = retained.Pack.Missing;
            retained.Complete = retained.Pack.Complete;
        }
        else if (result["prepared"] is not null)
        {
            retained.Runtime = result.Deserialize(RuntimeJsonContext.Default.RuntimePreparationResult)!;
            retained.RuntimePrepared = retained.Runtime.Prepared;
        }

        return retained;
    }

    internal Task CancelAsync()
    {
        if (!_tasks.Active && _preflight is { } preflight)
        {
            Publish(State with { CancelRequested = true });
            preflight.Cancel();
            return Task.CompletedTask;
        }

        return _tasks.CancelAsync();
    }

    internal async Task ImportAsync()
    {
        if (!State.CanImport)
            return;
        _importing = true;
        var options = new PackWorkflowOptions
        {
            ServerId = _serverId,
            Name = string.IsNullOrWhiteSpace(State.Name) ? null : State.Name.Trim(),
            ManualDirectory = string.IsNullOrWhiteSpace(State.ManualDirectory) ? null : State.ManualDirectory.Trim(),
            Offline = !State.Download,
            ContentOnly = !State.Download || !State.PrepareRuntime,
            OptionalSelection = new PackOptionalSelection("include-all", [], [.. State.Excluded])
        };
        if (State.RetainedInstanceId is { Length: > 0 } retained)
        {
            Publish(State with
            {
                Running = true,
                Submitted = true,
                Preparation = null,
                TaskProgress = null,
                Error = string.Empty,
                Outcome = string.Empty
            });
            try
            {
                await _tasks.RunAsync(new PreparePackWorkflowTask(retained, options, string.IsNullOrWhiteSpace(State.Source) ? null : State.Source.Trim()), retained);
                await _home.RefreshAsync(retained);
            }
            catch (Exception exception)
            {
                Publish(State with { Error = exception.Message });
            }
            finally
            {
                _importing = false;
                Publish(State with { Running = false });
            }

            return;
        }

        Publish(State with
        {
            Running = true,
            Submitted = true,
            Preparation = null,
            TaskProgress = null,
            Error = string.Empty,
            Outcome = string.Empty
        });
        using var preflight = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _preflight = preflight;
        try
        {
            // Keep the inspected source immutable until the typed task is terminal.
            using var source = File.Open(State.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, preflight.Token));
            if (hash != _fingerprint)
            {
                Publish(State with
                {
                    Running = false,
                    Inspection = null,
                    Error = Resources.PackArchiveChanged
                });
                return;
            }

            await _tasks.RunAsync(new ImportPackTask(State.Source, options));
            if (State.RetainedInstanceId is { Length: > 0 } id)
                await _home.RefreshAsync(id);
        }
        catch (OperationCanceledException) when (preflight.IsCancellationRequested)
        {
            Publish(State with
            {
                Outcome = "canceled",
                CancelRequested = false,
                Status = Resources.FeatureTaskCanceled
            });
        }
        catch (Exception exception)
        {
            Publish(State with { Error = exception.Message });
        }
        finally
        {
            if (ReferenceEquals(_preflight, preflight))
                _preflight = null;
            _importing = false;
            Publish(State with
            {
                Running = false,
                Submitted = State.RetainedInstanceId.Length > 0
            });
        }
    }

    /// <summary>Stops owned observations and inspection when the dialog is disposed.</summary>
    public void Dispose()
    {
        _preflight?.Cancel();
        _preflight?.Dispose();
        _tasks.Changed -= OnTaskChanged;
        _tasks.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}