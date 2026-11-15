// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    private enum ContentChange
    {
        Pin,
        Unpin,
        Policy,
        Detach,
        Identify,
        Add,
        Enable,
        Disable,
        Remove,
        Update,
        Install
    }

    /// <summary>Reads the selected content inventory without routing it through a mutation report.</summary>
    public Task<IReadOnlyList<ContentEntry>> ListContentAsync(string instanceId, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        return Content.ListAsync(Instances.Get(instanceId).GameDirectory, options.Kind, options.IncludeHashes, options.World, cancellationToken);
    }
    /// <summary>Recovers persisted content journals, returning their original extensible recovery records.</summary>
    public async Task<JsonArray> RecoverContentAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        using var cacheUse = await Storage.CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        return await Content.RecoverAsync(Instances.Get(instanceId).GameDirectory, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Indexes the real local mod declarations and advisory findings.</summary>
    public Task<ContentAnalysis> AnalyzeContentAsync(string instanceId, CancellationToken cancellationToken = default) =>
        Content.AnalyzeAsync(Instances.Get(instanceId).GameDirectory, cancellationToken);
    /// <summary>Detaches one file's platform source without changing the file.</summary>
    public async Task<ContentDetachResult> DetachContentAsync(string instanceId, string fileName, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) =>
        (ContentDetachResult)await ChangeContentAsync(ContentChange.Detach, instanceId, [fileName], options ?? new(), cancellationToken).ConfigureAwait(false);
    /// <summary>Identifies one file by hash and optionally attaches the verified source.</summary>
    public async Task<ContentIdentityResult> IdentifyContentAsync(string instanceId, string fileName, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) =>
        (ContentIdentityResult)await ChangeContentAsync(ContentChange.Identify, instanceId, [fileName], options ?? new(), cancellationToken).ConfigureAwait(false);
    /// <summary>Returns an exact plan or committed installation receipt according to the workflow options.</summary>
    public Task<ContentResult> InstallContentAsync(string instanceId, string projectId, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangeContentAsync(ContentChange.Install, instanceId, [projectId], options ?? new(), cancellationToken);
    /// <summary>Plans or applies selected tracked updates, preserving per-file outcomes in a batch.</summary>
    public Task<ContentResult> UpdateContentAsync(string instanceId, IReadOnlyList<string> files, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangeContentAsync(ContentChange.Update, instanceId, files, options ?? new(), cancellationToken);
    /// <summary>Imports selected sources and reports the actual single entry or per-source batch outcomes.</summary>
    public Task<ContentResult> AddContentAsync(string instanceId, IReadOnlyList<string> sources, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangeContentAsync(ContentChange.Add, instanceId, sources, options ?? new(), cancellationToken);
    /// <summary>Moves selected packages into recoverable storage with single or per-file batch receipts.</summary>
    public Task<ContentResult> RemoveContentAsync(string instanceId, IReadOnlyList<string> files, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangeContentAsync(ContentChange.Remove, instanceId, files, options ?? new(), cancellationToken);
    /// <summary>Changes selected packages' enabled state while retaining successful partial operations.</summary>
    public Task<ContentResult> SetContentEnabledAsync(string instanceId, IReadOnlyList<string> files, bool enabled, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) => ChangeContentAsync(enabled ? ContentChange.Enable : ContentChange.Disable, instanceId, files, options ?? new(), cancellationToken);
    /// <summary>Changes the selected package's pin or channel policy.</summary>
    public async Task<ContentEntry> SetContentPolicyAsync(string instanceId, string fileName, bool? pinned, ContentWorkflowOptions? options = null, CancellationToken cancellationToken = default) =>
        (ContentEntry)await ChangeContentAsync(pinned is null ? ContentChange.Policy : pinned.Value ? ContentChange.Pin : ContentChange.Unpin, instanceId, [fileName], options ?? new(), cancellationToken).ConfigureAwait(false);
    private async Task<ContentResult> ChangeContentAsync(ContentChange action, string instanceId, IReadOnlyList<string> items, ContentWorkflowOptions options, CancellationToken ct)
    {
        if (items.Count == 0)
            throw new ArgumentException("Select at least one content item.", nameof(items));
        using var cacheUse = await Storage.CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false);
        var platform = options.Platform;
        var online = ContentPlatform;
        var contentInstance = Instances.Get(instanceId);
        var content = new ContentService(paths);
        var kind = options.Kind;
        var world = options.World;
        var contentOptions = options.InstallOptions;
        if (action == ContentChange.Update)
            contentOptions = await ContentUpdateOptionsAsync(instanceId, options, ct).ConfigureAwait(false);

        if (action is ContentChange.Pin or ContentChange.Unpin or ContentChange.Policy)
            return await content.SetPolicyAsync(contentInstance.GameDirectory, kind, items[0], pinned: action == ContentChange.Policy ? null : action == ContentChange.Pin, channel: options.InstallOptions.Channel, world: world, ct: ct).ConfigureAwait(false);
        if (action == ContentChange.Detach)
            return await content.DetachAsync(contentInstance.GameDirectory, kind, items[0], world, ct).ConfigureAwait(false);
        if (action == ContentChange.Identify)
        {
            var identityContext = await GetContentContextAsync(contentInstance.Id, options.MinecraftVersion, options.Loader, ct).ConfigureAwait(false);
            var identified = await online.IdentifyExpectedAsync(game: contentInstance.GameDirectory, kind: kind, fileName: items[0], expectedProjectId: options.ExpectedProjectId, expectedVersionId: options.VersionId, world: world, algorithm: options.Algorithm ?? "sha1", attach: options.Attach, ct: ct, minecraftVersion: identityContext.MinecraftVersion, loader: identityContext.Loader).ConfigureAwait(false);
            identified.ContentCompatibility = identityContext;
            return identified;
        }

        if ((action is ContentChange.Add or ContentChange.Enable or ContentChange.Disable or ContentChange.Remove or ContentChange.Update) && items.Count > 1)
        {
            var outcomes = new List<ContentItemOutcome>();
            var failed = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = action switch
                    {
                        ContentChange.Add => await content.AddAsync(game: contentInstance.GameDirectory, kind: kind, source: item, world: world, ct: ct).ConfigureAwait(false),
                        ContentChange.Enable or ContentChange.Disable => await content.SetEnabledAsync(contentInstance.GameDirectory, kind, item, action == ContentChange.Enable, world, ct).ConfigureAwait(false),
                        ContentChange.Remove => await content.RemoveAsync(contentInstance.GameDirectory, kind, item, world, ct).ConfigureAwait(false),
                        _ => options.PlanOnly ? await online.PlanUpdateAsync(game: contentInstance.GameDirectory, kind: kind, fileName: item, world: world, ct: ct, options: contentOptions).ConfigureAwait(false) : await online.UpdateAsync(game: contentInstance.GameDirectory, kind: kind, fileName: item, world: world, progress: DownloadEvents, ct: ct, options: contentOptions).ConfigureAwait(false)
                    };
                    outcomes.Add(new ContentItemSuccess { Item = item, Data = result });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    outcomes.Add(ContentFailureOutcome(exception, item));
                }
            }

            var batch = new ContentBatchResult
            {
                Items = outcomes,
                FailedCount = failed,
                SucceededCount = outcomes.Count - failed
            };
            if (failed > 0)
                throw new LauncherOperationException("CONTENT_BATCH_PARTIAL", $"{failed} content operations failed. Successful items are retained; inspect each item before retrying.", JsonSerializer.SerializeToNode<ContentResult>(batch, ContentJsonContext.Result));
            return batch;
        }

        if (action == ContentChange.Add)
            return await content.AddAsync(game: contentInstance.GameDirectory, kind: kind, source: items[0], world: world, ct: ct).ConfigureAwait(false);
        if (action is ContentChange.Enable or ContentChange.Disable)
            return await content.SetEnabledAsync(contentInstance.GameDirectory, kind, items[0], action == ContentChange.Enable, world, ct).ConfigureAwait(false);
        if (action == ContentChange.Remove)
            return await content.RemoveAsync(contentInstance.GameDirectory, kind, items[0], world, ct).ConfigureAwait(false);
        if (action == ContentChange.Install)
        {
            var compatibility = await GetContentContextAsync(contentInstance.Id, options.MinecraftVersion, options.Loader, ct).ConfigureAwait(false);
            var gameVersion = compatibility.MinecraftVersion;
            var loader = compatibility.Loader;
            if (options.PlanOnly)
            {
                var plan = await online.PlanInstallAsync(platform: platform, projectId: items[0], versionId: options.VersionId, game: contentInstance.GameDirectory, kind: kind, world: world, minecraftVersion: gameVersion, loader: loader, ct: ct, options: contentOptions).ConfigureAwait(false);
                plan.ContentCompatibility = compatibility;
                return plan;
            }

            var installedContent = await online.InstallAsync(platform: platform, projectId: items[0], versionId: options.VersionId, game: contentInstance.GameDirectory, kind: kind, world: world, progress: DownloadEvents, minecraftVersion: gameVersion, loader: loader, ct: ct, options: contentOptions).ConfigureAwait(false);
            installedContent.ContentCompatibility = compatibility;
            return installedContent;
        }

        if (action == ContentChange.Update)
            return options.PlanOnly ? await online.PlanUpdateAsync(game: contentInstance.GameDirectory, kind: kind, fileName: items[0], world: world, ct: ct, options: contentOptions).ConfigureAwait(false) : await online.UpdateAsync(game: contentInstance.GameDirectory, kind: kind, fileName: items[0], world: world, progress: DownloadEvents, ct: ct, options: contentOptions).ConfigureAwait(false);
        throw new ArgumentOutOfRangeException(nameof(action));
    }

    private static ContentItemFailure ContentFailureOutcome(Exception error, string? item = null)
    {
        var failure = LauncherOperationException.CreateResult(error);
        return new ContentItemFailure
        {
            Item = item,
            SchemaVersion = failure.SchemaVersion,
            Status = failure.Status,
            Data = failure.Data,
            Error = failure.Error
        };
    }
    private async Task<ContentInstallOptions> ContentUpdateOptionsAsync(string instanceId, ContentWorkflowOptions options, CancellationToken ct)
    {
        if (options.UseSourceContext) return options.InstallOptions;
        var compatibility = await GetContentContextAsync(instanceId, options.MinecraftVersion, options.Loader, ct).ConfigureAwait(false);
        return options.InstallOptions with
        {
            UseCurrentContext = options.MinecraftVersion is not null || compatibility.Source != "launch-version-fallback",
            MinecraftVersion = compatibility.MinecraftVersion,
            Loader = compatibility.Loader,
            ContextWarning = compatibility.Source == "launch-version-fallback" && options.MinecraftVersion is null
                ? "Current runtime metadata is unavailable; using recorded source filters. Explicit Minecraft/loader filters remain available." : null
        };
    }

}