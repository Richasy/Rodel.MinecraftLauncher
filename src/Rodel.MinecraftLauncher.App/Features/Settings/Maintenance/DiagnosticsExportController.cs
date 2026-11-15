// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.Core.Application;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record DiagnosticSession(
    string Id,
    string InstanceId,
    string Name,
    string Time,
    string Status,
    bool Readable)
{
    internal string Label => string.Format(Resources.DiagnosticsSessionLabel, Name, Time, Status);
}

internal sealed record DiagnosticInstance(string Id, string Name);

internal sealed record DiagnosticsExportState
{
    internal bool Launcher { get; init; } = true;
    internal bool Reading { get; init; }
    internal bool Exporting { get; init; }
    internal bool Completed { get; init; }
    internal bool Bundle { get; init; } = true;
    internal DiagnosticSession[] AllSessions { get; init; } = [];
    internal DiagnosticInstance[] Instances { get; init; } = [];
    internal int SelectedInstance { get; init; } = -1;
    internal DiagnosticSession[] Sessions => SelectedInstance >= 0 && SelectedInstance < Instances.Length
        ? [.. AllSessions.Where(session => session.InstanceId == Instances[SelectedInstance].Id)] : [];
    internal int Selected { get; init; } = -1;
    internal string Destination { get; init; } = string.Empty;
    internal string Error { get; init; } = string.Empty;
    internal string ErrorCode { get; init; } = string.Empty;
    internal string Outcome { get; init; } = string.Empty;
    internal long Bytes { get; init; }
    internal IReadOnlyList<string> SourceWarnings { get; init; } = [];
    internal DiagnosticSession? Session => Selected >= 0 && Selected < Sessions.Length ? Sessions[Selected] : null;
    internal bool Editable => !Reading && !Exporting && !Completed;
    internal bool CanExport => Editable && (Launcher || Session?.Readable == true) && !string.IsNullOrWhiteSpace(Destination);
}

/// <summary>Exports launcher activity or one selected game session through Core, then publishes the owned staging ZIP.</summary>
internal sealed class DiagnosticsExportController(LauncherClient client, HomeController home, bool launcher = true) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _export;
    private string? _approvedReplacement;
    private bool _disposed;
    internal DiagnosticsExportState State { get; private set; } = new() { Launcher = launcher };
    internal event Action<DiagnosticsExportState>? Changed;

    private void Publish(DiagnosticsExportState state)
    {
        if (_disposed)
            return;
        State = state;
        Changed?.Invoke(state);
    }

    internal async Task LoadAsync()
    {
        if (_disposed || State.Launcher || State.Reading || State.Exporting)
            return;
        Publish(State with
        {
            Reading = true,
            Error = string.Empty,
            ErrorCode = string.Empty
        });
        try
        {
            var records = await Task.Run(() =>
                {
                    var names = client.Instances.List((_, _) =>
                        {
                        }).ToDictionary(instance => instance.Id, instance => instance.Name);
                    return client.Sessions.List().OrderByDescending(value => value.CreatedAt, StringComparer.Ordinal).Select(value =>
                        {
                            var id = value.Id ?? string.Empty;
                            var status = value.Status;
                            var readable = status != "unreadable" && Guid.TryParseExact(id, "N", out _);
                            var name = names.GetValueOrDefault(value.InstanceId ?? string.Empty) ?? Resources.DiagnosticsUnknownInstance;
                            var time = DateTimeOffset.TryParse(value.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created.ToLocalTime().ToString("G", CultureInfo.CurrentCulture) : Resources.DiagnosticsUnknownTime;
                            var label = status switch
                            {
                                "running" => Resources.DiagnosticsRunning,
                                "preparing" or "queued" => Resources.DiagnosticsPreparing,
                                "exited" => string.Format(Resources.DiagnosticsExited, value.ExitCode?.ToString() ?? Resources.ValueNotAvailable),
                                "failed" => Resources.DiagnosticsFailed,
                                "interrupted" => Resources.DiagnosticsInterrupted,
                                "unreadable" => Resources.DiagnosticsUnreadable,
                                _ => Resources.DiagnosticsUnknownStatus,
                            };
                            return new DiagnosticSession(
                                id,
                                value.InstanceId ?? string.Empty,
                                name,
                                time,
                                label,
                                readable);
                        }).ToArray();
                }, _lifetime.Token);
            var instances = records.GroupBy(record => record.InstanceId).Select(group => new DiagnosticInstance(group.Key, group.First().Name)).ToArray();
            instances = [.. instances.Select(instance => instances.Count(other => other.Name == instance.Name) > 1
                ? instance with { Name = string.Format(Resources.DiagnosticsInstanceLabel, instance.Name, instance.Id.Length >= 8 ? instance.Id[..8] : Resources.ValueNotAvailable) } : instance)
                .OrderBy(instance => instance.Name, StringComparer.CurrentCultureIgnoreCase)];
            Publish(State with
            {
                Reading = false,
                AllSessions = records,
                Instances = instances,
                SelectedInstance = instances.Length > 0 ? 0 : -1,
                Selected = -1
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
                Error = exception.Message,
                ErrorCode = exception is LauncherOperationException operation ? operation.Code : string.Empty
            });
        }
    }

    internal void Select(int index)
    {
        if (!State.Editable || index < 0 || index >= State.Sessions.Length || State.Selected == index)
            return;
        Publish(State with
        {
            Selected = index,
            Error = string.Empty,
            ErrorCode = string.Empty,
            Outcome = string.Empty
        });
    }

    internal void SelectInstance(int index)
    {
        if (!State.Editable || index < 0 || index >= State.Instances.Length || index == State.SelectedInstance) return;
        Publish(State with { SelectedInstance = index, Selected = -1, Error = string.Empty, ErrorCode = string.Empty });
    }

    internal void SetMode(int index)
    {
        if (State.Editable && index is 0 or 1 && State.Bundle != (index == 0))
        {
            Publish(State with
            {
                Bundle = index == 0,
                Error = string.Empty,
                ErrorCode = string.Empty,
                Outcome = string.Empty
            });
        }
    }

    internal void SetDestination(string path, bool replacementApproved = false)
    {
        if (!State.Editable || (State.Destination == path && !replacementApproved))
            return;
        _approvedReplacement = replacementApproved ? Path.GetFullPath(path) : null;
        Publish(State with
        {
            Destination = path,
            Error = string.Empty,
            ErrorCode = string.Empty,
            Outcome = string.Empty
        });
    }

    internal void ReportError(Exception exception) => Publish(State with
    {
        Error = exception.Message,
        ErrorCode = exception is LauncherOperationException operation ? operation.Code : string.Empty
    });

    internal async Task ExportAsync()
    {
        if (_disposed || !State.CanExport)
            return;
        var session = State.Session?.Id;
        var launcher = State.Launcher;
        var destination = State.Destination;
        var bundle = State.Bundle;
        _export?.Dispose();
        _export = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _export.Token;
        Publish(State with
        {
            Exporting = true,
            Error = string.Empty,
            ErrorCode = string.Empty,
            Outcome = string.Empty
        });
        using var ownedWork = home.TrackManagementWork();
        try
        {
            var result = await Task.Run(async () =>
                {
                    var target = Path.GetFullPath(destination);
                    var replace = string.Equals(_approvedReplacement, target, StringComparison.OrdinalIgnoreCase);
                    if (File.Exists(target) && !replace)
                        throw new LauncherOperationException("EXPORT_DESTINATION_EXISTS", Resources.DiagnosticsDestinationExists);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var stage = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".export.zip");
                    try
                    {
                        IReadOnlyList<string> warnings = [];
                        if (launcher)
                            warnings = (await client.Maintenance.ExportLauncherLogsAsync(stage, ct)).Manifest.Warnings;
                        else if (bundle)
                            await client.Maintenance.ExportBundleAsync(session!, stage, redact: true, ct: ct);
                        else
                            await client.Maintenance.ExportLogsAsync(session!, stage, ct);
                        ct.ThrowIfCancellationRequested();
                        File.Move(stage, target, replace);
                        return (Path: target, Bytes: new FileInfo(target).Length, Warnings: warnings);
                    }
                    finally
                    {
                        if (File.Exists(stage))
                            File.Delete(stage);
                    }
                }, ct);
            Publish(State with
            {
                Exporting = false,
                Completed = true,
                Destination = result.Path,
                Bytes = result.Bytes,
                SourceWarnings = result.Warnings,
                Outcome = Resources.DiagnosticsExported
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Publish(State with
            {
                Exporting = false,
                Outcome = Resources.DiagnosticsCanceled
            });
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Exporting = false,
                Error = exception.Message,
                ErrorCode = exception is LauncherOperationException operation ? operation.Code : string.Empty
            });
        }
    }

    internal void Cancel() => _export?.Cancel();

    /// <summary>Cancels only this dialog's observation/export and stops publishing after its owner closes.</summary>
    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _export?.Dispose();
        _lifetime.Dispose();
    }
}