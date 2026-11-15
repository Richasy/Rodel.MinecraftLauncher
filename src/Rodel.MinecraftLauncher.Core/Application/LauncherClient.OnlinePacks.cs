// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    private enum OnlinePackChange
    {
        Install,
        Review,
        Update
    }

    /// <summary>Resolves a Modrinth pack and imports it through the shared automatic content, runtime and Java preparation flow.</summary>
    public Task<PackResult> InstallOnlinePackAsync(string projectId, PackWorkflowOptions? options = null, CancellationToken cancellationToken = default) => OnlinePackAsync(OnlinePackChange.Install, null, (options ?? new()) with { ProjectId = projectId }, cancellationToken);
    /// <summary>Stages the selected online release as a frozen review without changing the installed instance.</summary>
    public Task<PackResult> ReviewOnlinePackAsync(string instanceId, PackWorkflowOptions? options = null, CancellationToken cancellationToken = default) => OnlinePackAsync(OnlinePackChange.Review, instanceId, options ?? new(), cancellationToken);
    /// <summary>Updates an online pack with the same preparation and rollback policy as a local archive update.</summary>
    public Task<PackResult> UpdateOnlinePackAsync(string instanceId, PackWorkflowOptions? options = null, CancellationToken cancellationToken = default) => OnlinePackAsync(OnlinePackChange.Update, instanceId, options ?? new(), cancellationToken);
    private async Task<PackResult> OnlinePackAsync(OnlinePackChange action, string? instanceId, PackWorkflowOptions options, CancellationToken ct)
    {
        using var cacheUse = await CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false);
        var platform = options.Platform;
        if (platform.Equals("curseforge", StringComparison.OrdinalIgnoreCase))
            throw new Rodel.MinecraftLauncher.Core.Content.ContentPlatformUnavailableException();
        if (!platform.Equals("modrinth", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Online pack installation currently supports Modrinth. Local pack archives remain supported.");
        var target = action == OnlinePackChange.Install ? null : Instances.Get(instanceId!);
        var sourcePath = target is null ? null : Path.Combine(target.GameDirectory, ".rodel", "pack-lock.json");
        var packLock = sourcePath is not null && File.Exists(sourcePath) ? JsonFiles.ReadObject(sourcePath) : [];
        if (packLock["sourceDetached"]?.GetValue<bool>() == true && options.ProjectId is null)
            throw new InvalidOperationException("This client is detached from online pack updates. Select an explicit project to attach a new source.");
        var source = packLock["onlineSource"] as JsonObject ?? [];
        var project = action == OnlinePackChange.Install ? options.ProjectId ?? throw new ArgumentException("A project ID is required.") : options.ProjectId ?? source["projectId"]?.GetValue<string>() ?? throw new ArgumentException("This pack has no online source. Supply --project PROJECT.");
        var online = new OnlinePackService(paths);
        var selection = await online.SelectAsync(project: project, versionId: options.VersionId, offline: options.Offline, channel: options.Channel, ct: ct, persist: !options.PlanOnly).ConfigureAwait(false);
        if (options.PlanOnly)
        {
            return new OnlinePackPlan
            {
                ProjectId = selection.ProjectId,
                VersionId = selection.VersionId,
                VersionName = selection.VersionName,
                VersionType = selection.VersionType,
                Channel = selection.Channel,
                Name = selection.Name,
                File = selection.File,
                GameVersions = selection.GameVersions,
                Loaders = selection.Loaders,
                ArchivePath = selection.ArchivePath,
                SelectionWarnings = selection.SelectionWarnings,
                AdditionalData = selection.AdditionalData,
                InstanceId = target?.Id,
                Action = action switch
                {
                    OnlinePackChange.Install => "install",
                    OnlinePackChange.Review => "review-online",
                    _ => "update-online"
                }
            };
        }

        Report(new LauncherProgress { Stage = "pack-archive", ProjectId = project, VersionId = selection.VersionId });
        var archive = await online.DownloadAsync(selection: selection, offline: options.Offline, ct: ct, progress: DownloadEvents).ConfigureAwait(false);
        var packs = new PackService(paths, progress: DownloadEvents);
        if (action == OnlinePackChange.Install)
            return await PreparePackWorkflowAsync(null, archive, options with { Name = options.Name ?? selection.Name?.GetValue<string>() }, ct, selection).ConfigureAwait(false);
        var result = action == OnlinePackChange.Review
            ? await packs.ReviewAsync(instanceId: target!.Id, archivePath: archive, download: !options.Offline, manualDirectory: options.ManualDirectory, cancellationToken: ct, optionalSelection: options.OptionalSelection, onlineSource: selection).ConfigureAwait(false)
            : await packs.UpdateAsync(instanceId: target!.Id, archivePath: archive, download: !options.Offline, manualDirectory: options.ManualDirectory, cancellationToken: ct, optionalSelection: options.OptionalSelection, onlineSource: selection).ConfigureAwait(false);
        result.OnlineSource = selection;
        if (action == OnlinePackChange.Review)
            return result;
        return await PrepareAppliedPackAsync(result, options, ct).ConfigureAwait(false);
    }
}