// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed class ContentController(LauncherClient client, HomeController home) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ContentIconReader _icons = new();
    private readonly Dictionary<string, string> _rowKeys = [with(StringComparer.Ordinal)];
    private CancellationTokenSource? _read;
    private CancellationTokenSource? _operation;
    private long _revision;
    internal ContentState State { get; private set; } = new();
    internal event Action<ContentState>? Changed;

    private void Publish(ContentState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    internal Task SelectAsync(string id, ContentKind kind = ContentKind.Mods)
    {
        if (id == State.InstanceId && kind == State.Kind)
            return Task.CompletedTask;
        _rowKeys.Clear();
        Publish(State with
        {
            InstanceId = id,
            Kind = kind,
            Items = [],
            BundledMods = null,
            BundledModsError = string.Empty,
            ReadError = string.Empty,
            SelectionError = string.Empty,
            ResourceSelection = null,
            ShaderSelection = null,
            ShaderEngine = null,
            Outcome = State.Active ? State.Outcome : string.Empty,
            Failure = State.Active ? State.Failure : null,
            Result = State.Active ? State.Result : null,
            Job = State.Active ? State.Job : null,
        });
        return RefreshAsync();
    }

    internal Task ChooseShaderEngineAsync(string engine)
    {
        Publish(State with { ShaderEngine = engine });
        return RefreshAsync();
    }

    internal async Task RefreshAsync()
    {
        var revision = ++_revision;
        _read?.Cancel();
        _read?.Dispose();
        _read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _read.Token;
        var id = State.InstanceId;
        var kind = State.Kind;
        var engine = State.ShaderEngine;
        Publish(State with
        {
            Loading = id.Length > 0,
            ReadError = string.Empty
        });
        if (id.Length == 0)
            return;
        try
        {
            var (Rows, Resources, Shaders, SelectionError, Bundled, BundledError) = await Task.Run(async () =>
                {
                    ResourcePackSelectionSummary? resources = null;
                    ShaderPackSelectionSummary? shaders = null;
                    var selectionError = string.Empty;
                    try
                    {
                        if (kind == ContentKind.ResourcePacks)
                            resources = await client.GetResourcePacksAsync(id, cancellationToken: ct);
                        if (kind == ContentKind.Shaders)
                            shaders = await client.GetShaderPacksAsync(id, engine, ct);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        selectionError = error.Message;
                    }

                    var result = resources?.Installed ?? shaders?.Installed ?? await client.ListContentAsync(id, new() { Kind = kind.Directory(), IncludeHashes = false }, ct);
                    var rows = result.Where(item => item.IsContent).Select(ContentItem.Read).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                    var localNames = rows.Select(item => item.FileName).ToHashSet(StringComparer.Ordinal);
                    var selectedResources = resources?.Selected.Select(value => value!.ToString()).Select(value => value.StartsWith("file/", StringComparison.Ordinal) ? value[5..] : value).Where(localNames.Contains).ToArray() ?? [];
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var item = rows[index];
                        if (resources is not null)
                        {
                            var position = Array.IndexOf(selectedResources, item.FileName);
                            rows[index] = item with
                            {
                                InUse = item.Enabled && position >= 0,
                                Priority = position >= 0 ? selectedResources.Length - position : null
                            };
                        }
                        else if (shaders?.Engine is not null)
                        {
                            rows[index] = item with
                            {
                                InUse = shaders.Enabled && shaders.Present && shaders.Selected == item.FileName
                            };
                        }
                    }

                    var game = client.Instances.Get(id).GameDirectory;
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var item = rows[index];
                        rows[index] = rows[index] with
                        {
                            ReadIcon = token => _icons.ReadLocalAsync(game, item, kind, token)
                        };
                    }

                    BundledModCatalog? bundled = null;
                    var bundledError = string.Empty;
                    if (kind == ContentKind.Mods)
                    {
                        try
                        {
                            bundled = await client.GetBundledModsAsync(id, result, ct);
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        {
                            bundledError = error.Message;
                        }
                    }

                    return (Rows: rows, Resources: resources, Shaders: shaders, SelectionError: selectionError, Bundled: bundled, BundledError: bundledError);
                }, ct);
            if (!ct.IsCancellationRequested && revision == _revision)
            {
                var keys = new Dictionary<string, string>(StringComparer.Ordinal);
                var rows = Rows.Select(item =>
                    {
                        var key = _rowKeys.GetValueOrDefault(item.FileName) ?? Guid.NewGuid().ToString("N");
                        keys[item.FileName] = key;
                        return item with
                        {
                            Key = key
                        };
                    }).ToArray();
                _rowKeys.Clear();
                foreach (var pair in keys)
                    _rowKeys.Add(pair.Key, pair.Value);
                Publish(State with
                {
                    Items = rows,
                    Loading = false,
                    ResourceSelection = Resources,
                    ShaderSelection = Shaders,
                    SelectionError = SelectionError,
                    BundledMods = Bundled,
                    BundledModsError = BundledError,
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested && revision == _revision)
            {
                Publish(State with
                {
                    Loading = false,
                    ReadError = exception.Message
                });
            }
        }
    }

    internal void ClearOutcome() => Publish(State with
    {
        Outcome = string.Empty,
        Failure = null,
        Result = null,
        Job = null
    });

    internal void ReportError(Exception exception, string instanceName, ContentKind? kind = null) => Publish(State with
    {
        Outcome = "failed",
        OperationInstance = instanceName,
        OperationKind = kind ?? State.Kind,
        Failure = LauncherOperationException.ToResult(exception),
        Result = null,
    });

    internal async Task ExecuteAsync(
        string id,
        ContentAction action,
        string item,
        ContentKind? kind = null) => await ChangeAsync(id, kind ?? State.Kind, action, [item]);

    internal Task ImportFilesAsync(string id, ContentKind kind, IReadOnlyList<string> files) => ChangeAsync(id, kind, ContentAction.Import, files);

    internal Task InstallAsync(
        string id,
        ContentKind kind,
        string projectId,
        string versionId,
        string? sha1 = null) => ChangeAsync(
        id,
        kind,
        ContentAction.Install,
        [projectId],
        versionId,
        sha1);

    internal Task RepairDependenciesAsync(string id, string reviewId) => ChangeAsync(id, ContentKind.Mods, ContentAction.RepairDependencies, [reviewId]);

    internal Task ApplyReviewAsync(string id, ContentKind operationKind, string reviewId) => ChangeAsync(id, operationKind, ContentAction.ApplyReview, [reviewId]);

    internal Task InstallBundledModsAsync(string id, IReadOnlyList<string> modIds) => ChangeAsync(id, ContentKind.Mods, ContentAction.BundledMods, modIds);

    internal Task ManageWorldAsync(ManageWorldTask request) => ChangeAsync(
        request.InstanceId,
        State.Kind,
        ContentAction.World,
        [],
        worldTask: request);

    internal Task ConfigurePolicyAsync(
        string id,
        ContentKind kind,
        string fileName,
        bool? pinned,
        string? channel) => ChangeAsync(
        id,
        kind,
        ContentAction.Policy,
        [fileName],
        pinned: pinned,
        channel: channel);

    private async Task ChangeAsync(
        string id,
        ContentKind kind,
        ContentAction action,
        IReadOnlyList<string> items,
        string? versionId = null,
        string? sha1 = null,
        ManageWorldTask? worldTask = null,
        bool? pinned = null,
        string? channel = null)
    {
        if (State.Active || _lifetime.IsCancellationRequested)
            return;
        if (home.State.PendingInstances.Contains(id))
        {
            ReportError(new LauncherOperationException("CONTENT_INSTANCE_BUSY", "The instance has an active task."), home.State.Instances.FirstOrDefault(instance => instance.Id == id)?.Name ?? id, kind);
            return;
        }

        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _operation.Token;
        var existing = action == ContentAction.Install && State.InstanceId == id && State.Kind == kind && sha1 is { Length: > 0 } ? State.Items.FirstOrDefault(item => string.Equals(item.Sha1, sha1, StringComparison.OrdinalIgnoreCase)) : null;
        var inline = action is ContentAction.Enable or ContentAction.Disable or ContentAction.Policy or ContentAction.Use or ContentAction.StopUsing or ContentAction.RaisePriority or ContentAction.LowerPriority;
        var appliedEntry = false;
        Publish(State with
        {
            Active = true,
            InlineChange = inline,
            CancelRequested = false,
            Outcome = string.Empty,
            Failure = null,
            Result = null,
            Job = null,
            OperationKind = kind,
            WorldOperation = worldTask is not null,
            OperationInstance = home.State.Instances.FirstOrDefault(instance => instance.Id == id)?.Name ?? id,
        });
        home.SetManagementBusy(id, true);
        try
        {
            var instance = await Task.Run(() => client.Instances.Get(id), ct);
            Publish(State with { OperationInstance = instance.Name });
            await Task.Run(() => client.EnsureInstanceIdle(id), ct);
            var options = new ContentWorkflowOptions
            {
                Kind = kind.Directory(),
                VersionId = versionId,
                InstallOptions = new()
                {
                    Channel = channel
                }
            };
            if (action is ContentAction.Use or ContentAction.StopUsing or ContentAction.RaisePriority or ContentAction.LowerPriority)
            {
                if (kind == ContentKind.ResourcePacks)
                {
                    if (action is ContentAction.RaisePriority or ContentAction.LowerPriority)
                    {
                        await client.MoveResourcePackAsync(
                            id,
                            items[0],
                            action == ContentAction.RaisePriority,
                            State.ResourceSelection?.Fingerprint,
                            ct);
                    }
                    else
                    {
                        await client.SetResourcePackUsageAsync(
                            id,
                            items[0],
                            action == ContentAction.Use,
                            State.ResourceSelection?.Fingerprint,
                            ct);
                    }
                }
                else if (kind == ContentKind.Shaders)
                {
                    await client.SelectShaderPackAsync(id, new()
                    {
                        FileName = action == ContentAction.Use ? items[0] : null,
                        Engine = State.ShaderSelection?.Engine,
                        ExpectedFingerprint = State.ShaderSelection?.Fingerprint
                    }, ct);
                }

                return;
            }

            if (worldTask is not null)
            {
                var queued = await client.Tasks.StartAsync(worldTask, _lifetime.Token);
                Publish(State with { Job = queued });
                if (State.CancelRequested)
                    await SendCancellationAsync(queued.Id!);
                await ObserveInstallAsync(queued.Id!);
                return;
            }

            if (action is ContentAction.ApplyReview or ContentAction.BundledMods)
            {
                var reviewId = action == ContentAction.BundledMods ? (await Task.Run(() => client.ReviewBundledModsAsync(id, items, ct), ct)).Id : items[0];
                ct.ThrowIfCancellationRequested();
                var queued = await client.Tasks.StartAsync(new ApplyContentReviewTask(id, reviewId), _lifetime.Token);
                Publish(State with { Job = queued });
                if (State.CancelRequested)
                    await SendCancellationAsync(queued.Id!);
                await ObserveInstallAsync(queued.Id!);
                return;
            }

            if (action == ContentAction.RepairDependencies)
            {
                var queued = await client.Tasks.StartAsync(new RepairDependenciesTask(id, items[0]), _lifetime.Token);
                Publish(State with { Job = queued });
                if (State.CancelRequested)
                    await SendCancellationAsync(queued.Id!);
                await ObserveInstallAsync(queued.Id!);
                return;
            }

            if (action == ContentAction.Install)
            {
                if (existing is not null)
                {
                    if (existing.ProjectId is not null && existing.ProjectId != items[0])
                        throw new LauncherOperationException("CONTENT_SOURCE_CONFLICT", "The same content file is already associated with another project; it was retained.");
                    var identified = await client.IdentifyContentAsync(id, existing.FileName, options with
                    {
                        Attach = true,
                        Algorithm = "sha512",
                        ExpectedProjectId = items[0]
                    }, ct);
                    if (!identified.Matched)
                        throw new LauncherOperationException("CONTENT_IDENTITY_CHANGED", "The existing content file could not be matched to the selected source; no duplicate was installed.");
                }

                var queued = await Task.Run(() => client.Tasks.StartAsync(new InstallContentTask(id, items[0], options), _lifetime.Token));
                Publish(State with { Job = queued });
                if (State.CancelRequested)
                    await SendCancellationAsync(queued.Id!);
                await ObserveInstallAsync(queued.Id!);
                return;
            }

            var result = await Task.Run<ContentResult>(async () => action switch
                {
                    ContentAction.Import => await client.AddContentAsync(id, items, options, ct),
                    ContentAction.Remove => await client.RemoveContentAsync(id, items, options, ct),
                    ContentAction.Policy => await client.SetContentPolicyAsync(
                        id,
                        items[0],
                        pinned,
                        options,
                        ct),
                    _ => await client.SetContentEnabledAsync(
                        id,
                        items,
                        action == ContentAction.Enable,
                        options,
                        ct),
                }, ct);
            if (State.InstanceId == id && State.Kind == kind && inline && result is ContentEntry entry && State.Items.FirstOrDefault(item => item.FileName == items[0]) is { } previous)
            {
                _rowKeys.Remove(previous.FileName);
                _rowKeys[entry.FileName] = previous.Key;
                var updated = ContentItem.Read(entry) with
                {
                    Key = previous.Key,
                    IconBytes = previous.IconBytes,
                    IconWarning = previous.IconWarning,
                    InUse = previous.InUse,
                    Priority = previous.Priority
                };
                var iconItem = updated;
                var game = client.Instances.Get(id).GameDirectory;
                updated = updated with { ReadIcon = token => _icons.ReadLocalAsync(game, iconItem, kind, token) };
                Publish(State with { Items = [.. State.Items.Select(item => item.Key == previous.Key ? updated : item)] });
                // The package bytes are process-constant; only the actual instance installation state is refreshed.
                if (kind == ContentKind.Mods)
                {
                    try
                    {
                        var catalog = await Task.Run(() => client.GetBundledModsAsync(id, _lifetime.Token), _lifetime.Token);
                        if (State.InstanceId == id && State.Kind == kind)
                        {
                            Publish(State with
                            {
                                BundledMods = catalog,
                                BundledModsError = string.Empty
                            });
                        }
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        if (State.InstanceId == id && State.Kind == kind)
                        {
                            Publish(State with
                            {
                                BundledMods = null,
                                BundledModsError = error.Message
                            });
                        }
                    }
                }

                appliedEntry = true;
            }

            Publish(State with
            {
                Outcome = "succeeded",
                Result = result
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_lifetime.IsCancellationRequested)
                Publish(State with { Outcome = "canceled" });
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
                ReportError(exception, State.OperationInstance, kind);
        }
        finally
        {
            _operation?.Dispose();
            _operation = null;
            home.SetManagementBusy(id, false);
            if (!_lifetime.IsCancellationRequested)
            {
                Publish(State with
                {
                    Active = false,
                    CancelRequested = false
                });
                if (!appliedEntry)
                    await RefreshAsync();
                await home.RefreshAsync();
            }
        }
    }

    private async Task ObserveInstallAsync(string id)
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                var job = await Task.Run(() => client.Jobs.Get(id), _lifetime.Token);
                Publish(State with
                {
                    Job = job,
                    Failure = null
                });
                var status = job.Status;
                if (status is "succeeded" or "failed" or "canceled" or "interrupted")
                {
                    var failure = job.Failure?.DeepClone() as JsonObject;
                    if (status is "failed" or "interrupted" && failure is null)
                        failure = LauncherOperationException.ToResult(new IOException(job.Error ?? "The content installation task was interrupted."));
                    Publish(State with
                    {
                        Outcome = status,
                        Result = null,
                        Failure = failure
                    });
                    return;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Publish(State with { Failure = LauncherOperationException.ToResult(exception) });
            }

            await Task.Delay(200, _lifetime.Token);
        }
    }

    internal void Cancel() => _ = CancelAsync();

    internal async Task CancelAsync()
    {
        if (!State.Active || State.CancelRequested)
            return;
        Publish(State with { CancelRequested = true });
        try
        {
            if (State.JobId is { } id)
                await SendCancellationAsync(id);
            else
                _operation?.Cancel();
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Publish(State with { CancelRequested = false });
                ReportError(exception, State.OperationInstance);
            }
        }
    }

    private async Task SendCancellationAsync(string id)
    {
        try
        {
            await client.Jobs.CancelAsync(id, _lifetime.Token);
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Publish(State with { CancelRequested = false });
                ReportError(exception, State.OperationInstance);
            }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _read?.Cancel();
        _read?.Dispose();
        _operation?.Dispose();
        _icons.Dispose();
        _lifetime.Dispose();
    }
}