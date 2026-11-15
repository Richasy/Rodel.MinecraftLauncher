// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

/// <summary>Coordinates one explicit instance operation and observes its durable Core result.</summary>
internal sealed class InstanceManagementController(LauncherClient client, HomeController home) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task _configurationSave = Task.CompletedTask;
    internal InstanceManagementState State { get; private set; } = new();
    private static readonly string[] ContentKinds = ["mods", "resourcepacks", "shaderpacks"];
    internal event Action<InstanceManagementState>? Changed;

    private void Publish(InstanceManagementState state)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            State = state;
            Changed?.Invoke(state);
        }
    }

    internal async Task OpenAsync(string id, InstanceAction action)
    {
        if (State.Active || !_configurationSave.IsCompleted)
            return;
        var cancellation = _lifetime.Token;
        Publish(new() { Action = action });
        var instance = await Task.Run(() => client.Instances.Get(id), cancellation);
        cancellation.ThrowIfCancellationRequested();
        Publish(State with
        {
            Instance = instance,
            DeleteFiles = instance.Managed,
            Name = action == InstanceAction.Copy ? string.Format(Resources.InstanceCopyName, instance.Name) : instance.Name
        });
        if (action == InstanceAction.Configure)
            Publish(State with { Draft = await Task.Run(() => HomeLaunchSettingsService.ReadAsync(client, new(id, instance.Name, instance.VersionId ?? string.Empty, false))) });
        if (action == InstanceAction.Restore)
        {
            var backups = await Task.Run(() => client.Backups.List(id));
            var readable = backups.Where(b => !b.HasError).ToArray();
            var unreadable = backups.Where(b => b.HasError).ToArray();
            Publish(State with
            {
                Backups = readable,
                BackupReadErrors = unreadable,
                BackupIndex = readable.Length > 0 ? 0 : -1
            });
        }

        if (action == InstanceAction.Content)
        {
            var result = await Task.Run(async () =>
                {
                    var data = new JsonObject();
                    foreach (var kind in ContentKinds)
                    {
                        var entries = await client.Content.ListAsync(instance.GameDirectory, kind, ct: cancellation).ConfigureAwait(false);
                        data[kind] = JsonSerializer.SerializeToNode([.. entries.Where(entry => entry.IsContent)], Rodel.MinecraftLauncher.Core.Content.ContentJsonContext.Entries);
                    }

                    var worlds = Path.Combine(instance.GameDirectory, "saves");
                    data["worldDirectories"] = Directory.Exists(worlds) ? Directory.GetDirectories(worlds).Length : 0;
                    return data;
                }, cancellation);
            Publish(State with { Result = result });
        }
    }

    internal void ChangeName(string value) => Publish(State with { Name = value });

    internal void ChangeDraft(Func<LaunchSettingsDraft, LaunchSettingsDraft> update)
    {
        if (State.Action != InstanceAction.Configure || State.Draft is not { } draft)
            return;
        Publish(State with { Draft = update(draft) with { Error = string.Empty } });
        ScheduleConfigurationSave();
    }

    private void ScheduleConfigurationSave()
    {
        if (_configurationSave.IsCompleted && !_lifetime.IsCancellationRequested)
            _configurationSave = SaveConfigurationAsync();
    }

    internal async Task<bool> FlushConfigurationAsync()
    {
        ChangeDraft(draft => draft with { MemoryEditing = false, PlayerEditing = false });
        Task saving;
        do
        {
            saving = _configurationSave;
            await saving;
        }
        while (!_configurationSave.IsCompleted && !ReferenceEquals(saving, _configurationSave));
        return State.Draft is null or { HasChanges: false, Error.Length: 0 };
    }

    private async Task SaveConfigurationAsync()
    {
        using var work = home.TrackManagementWork();
        var id = State.Instance?.Id;
        // Assign the owned task before Changed can reenter through an edit or a close.
        await Task.Yield();
        var saved = false;
        try
        {
            while (!_lifetime.IsCancellationRequested && State.Instance?.Id == id && State.Action == InstanceAction.Configure &&
                State.Draft?.AutoSaveChanges is { CanSave: true } draft)
            {
                if (!HomeLaunchSettingsService.TryValidate(draft, out var memory))
                    break;
                home.SetManagementBusy(draft.InstanceId, true);
                Publish(State with { Active = true, Draft = State.Draft with { Saving = true } });
                await Task.Run(() => HomeLaunchSettingsService.SaveAsync(client, draft, memory));
                if (_lifetime.IsCancellationRequested)
                    return;
                Publish(State with { Draft = State.Draft!.AcceptSaved(draft) });
                saved = true;
            }

            if (saved && !_lifetime.IsCancellationRequested)
                await home.RefreshAsync();
        }
        catch (Exception exception)
        {
            if (State.Draft is { } draft)
                Publish(State with { Draft = draft with { Error = exception.Message } });
        }
        finally
        {
            _configurationSave = Task.CompletedTask;
            if (id is not null)
                home.SetManagementBusy(id, false);
            if (State.Draft is { } draft)
                Publish(State with { Active = false, Draft = draft with { Saving = false } });
            if (State.Draft is { Error.Length: 0, AutoSaveChanges.CanSave: true })
                ScheduleConfigurationSave();
        }
    }

    internal void ToggleDeleteFiles()
    {
        if (!State.Active && !State.Succeeded && State.Instance is { Managed: true })
            Publish(State with { DeleteFiles = !State.DeleteFiles });
    }

    internal void SelectBackup(int index) => Publish(State with { BackupIndex = index });

    internal void ReportError(Exception exception) => Publish(State with
    {
        Error = exception.Message,
        ErrorCode = LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString() ?? "OPERATION_FAILED",
        StatusCode = "failed",
    });

    internal async Task ExecuteAsync(string? path = null)
    {
        if (State.Active || State.Instance is not { } instance)
            return;
        Publish(State with
        {
            Active = true,
            Error = string.Empty,
            ErrorCode = string.Empty,
            Conflict = false,
            CancelRequested = false,
            Job = null,
            Result = null,
            StatusCode = string.Empty
        });
        home.SetManagementBusy(instance.Id, true);
        try
        {
            if (await Task.Run(() => IsBusy(instance.Id)))
            {
                Publish(State with { Conflict = true });
                return;
            }

            LauncherTaskRequest? request = State.Action switch
            {
                InstanceAction.Copy => new CopyInstanceTask(instance.Id, State.Name.Trim()),
                InstanceAction.Backup => new CreateBackupTask(instance.Id),
                InstanceAction.Restore when State.BackupIndex >= 0 && State.BackupIndex < State.Backups.Count => new RestoreBackupTask(instance.Id, State.Backups[State.BackupIndex].Id!),
                InstanceAction.Export when path is not null => new ExportInstanceTask(instance.Id, path),
                InstanceAction.Delete => new RemoveInstanceTask(instance.Id, State.DeleteFiles),
                _ => null,
            };
            if (request is not null)
            {
                var queued = await Task.Run(() => client.Tasks.StartAsync(request, _lifetime.Token));
                Publish(State with { Job = queued });
                if (State.CancelRequested)
                    await SendCancellationAsync(queued.Id!);
                var id = queued.Id!;
                while (!_lifetime.IsCancellationRequested)
                {
                    try
                    {
                        var job = await Task.Run(() => client.Jobs.Get(id), _lifetime.Token);
                        Publish(State with
                        {
                            Job = job,
                            Error = string.Empty
                        });
                        if (job.Status is "succeeded" or "failed" or "canceled" or "interrupted")
                        {
                            Publish(State with
                            {
                                StatusCode = job.Status,
                                Result = job.Result?.DeepClone()
                            });
                            break;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                    {
                        Publish(State with { Error = exception.Message });
                    }

                    await Task.Delay(200, _lifetime.Token);
                }
            }
            else if (State.Action == InstanceAction.Rename)
            {
                var renamed = await Task.Run(() => client.Instances.RenameAsync(instance.Id, State.Name.Trim(), _lifetime.Token));
                Publish(State with
                {
                    Instance = renamed,
                    StatusCode = "succeeded",
                    Result = renamed.ToJson()
                });
            }

            if (State.Succeeded)
            {
                await client.Activity.WriteAsync("instance.operation-completed", instance.Id, stage: State.Action.ToString());
                var selected = State.Action == InstanceAction.Copy
                    ? State.Result?["id"]?.ToString()
                    : State.Action == InstanceAction.Delete ? null : instance.Id;
                await home.RefreshAsync(selected);
            }
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
            home.SetManagementBusy(instance.Id, false);
            if (!_lifetime.IsCancellationRequested)
                Publish(State with { Active = false });
        }
    }

    private bool IsBusy(string id) => client.Sessions.HasActiveGame(client.Instances.Get(id).GameDirectory) || client.Jobs.List().Any(job => job.Request?.Parameters?["instanceId"]?.ToString() == id && (job.Status is "running" or "queued" || job.ActivityUnknown == true));

    internal async Task CancelAsync()
    {
        if (!State.Active || State.CancelRequested)
            return;
        Publish(State with { CancelRequested = true });
        if (State.Job?.Id is { } id)
            await SendCancellationAsync(id);
    }

    private async Task SendCancellationAsync(string id)
    {
        try
        {
            await client.Jobs.CancelAsync(id, _lifetime.Token);
        }
        catch (Exception exception)
        {
            Publish(State with { CancelRequested = false });
            ReportError(exception);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}