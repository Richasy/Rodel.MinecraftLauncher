// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentPlatformService
{
    /// <summary>Checks installed files and resolves all available roots in one frozen dependency plan.</summary>
    public async Task<ContentUpdatesPlan> PlanUpdatesAsync(string game, string kind, IReadOnlyList<string> fileNames,
        string? world = null, ContentInstallOptions? options = null, IProgress<ContentCheckProgress>? progress = null, CancellationToken ct = default)
    {
        if (fileNames.Count == 0) throw new ArgumentException("Select at least one installed file.", nameof(fileNames));
        options ??= new();
        var baseline = await ContentService.LockFingerprintAsync(game, ct).ConfigureAwait(false);
        if (options.ExpectedLockFingerprint is { } expected && !string.Equals(expected, baseline, StringComparison.OrdinalIgnoreCase))
            throw new LauncherOperationException("CONTENT_BASELINE_CHANGED", "Content tracking or policies changed after checking updates. Check again before selecting a new review.");
        var inventory = await _content.ListAsync(game, kind, world, ct).ConfigureAwait(false);
        var items = new List<ContentReviewItem>();
        var roots = new List<(string ProjectId, string? VersionId, string? Channel)>();
        var contexts = new List<(string? MinecraftVersion, string? Loader)>();
        var identifiedFiles = new Dictionary<string, ContentPlanNode>(StringComparer.Ordinal);
        foreach (var fileName in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var local = inventory.FirstOrDefault(item => item.FileName == fileName);
            LocalUpdateSource identified = new(null, []);
            Exception? identificationFailure = null;
            if (local is not null)
            {
                try
                {
                    identified = await IdentifyUpdateSourceAsync(local, options, ct).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    identificationFailure = error;
                }
            }

            var tracking = local is null ? null : UpdateTracking(local, identified.Provenance);
            var item = new ContentReviewItem
            {
                FileName = fileName,
                ProjectId = tracking?["projectId"]?.GetValue<string>(),
                CurrentVersionId = tracking?["versionId"]?.GetValue<string>(),
                CurrentVersion = tracking?["upstream"]?["version_number"]?.DeepClone(),
                Enabled = local?.Enabled,
                Pinned = tracking?["pinned"]?.GetValue<bool>(),
                Channel = tracking?["channel"]?.GetValue<string>(),
                Status = "skipped",
                SourceCandidates = identified.Candidates,
            };
            items.Add(item);
            if (local is null)
            {
                item.Reason = "file-missing";
            }
            else if (local.Directory)
            {
                item.Reason = "directory-content";
            }
            else if (identificationFailure is not null)
            {
                item.Status = "failed";
                item.Failure = LauncherOperationException.CreateResult(identificationFailure);
            }
            else if (tracking?["pinned"]?.GetValue<bool>() == true)
            {
                item.Reason = "pinned";
            }
            else if (HasOnlineSource(local) && tracking?["platform"]?.ToString() != "modrinth")
            {
                item.Reason = "manual-source";
            }
            else
            {
                try
                {
                    var plan = await PlanUpdateCoreAsync(game, kind, fileName, world, options, local, identified, ct).ConfigureAwait(false);
                    if (plan is ContentUpdateSkipped skipped)
                    {
                        item.Reason = skipped.Reason;
                        item.Warning = skipped.Warning;
                        item.SourceCandidates = skipped.SourceCandidates;
                    }
                    else
                    {
                        var installation = (ContentInstallPlan)plan;
                        var projectId = installation.PrimaryProject;
                        var candidate = installation.Nodes.First(node => node.ProjectId == projectId);
                        item.Status = "available";
                        item.ProjectId = projectId;
                        item.TargetVersionId = candidate.VersionId;
                        item.TargetVersion = candidate.Upstream["version_number"]?.DeepClone();
                        item.TargetFileName = candidate.FileName;
                        if (candidate.LocalReplacement is not null && !identifiedFiles.TryAdd(projectId, candidate))
                            throw new InvalidDataException("Several local files resolve to the same project. Review them separately before replacing files.");
                        roots.Add((projectId, candidate.VersionId, installation.EffectiveChannel is "any" ? null : installation.EffectiveChannel));
                        contexts.Add((installation.UpdateContext?.MinecraftVersion, installation.UpdateContext?.Loader));
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    item.Status = "failed";
                    item.Failure = LauncherOperationException.CreateResult(error);
                }
            }

            progress?.Report(new ContentCheckProgress(items.Count, fileNames.Count));
        }

        ContentInstallPlan? combined = null;
        LauncherOperationResult? planningFailure = null;
        if (roots.Count > 0)
        {
            try
            {
                var distinctContexts = contexts.Distinct().ToArray();
                var (MinecraftVersion, Loader) = distinctContexts[0];
                combined = await PlanInstallProjectsAsync("modrinth", roots, game, kind, world,
                    MinecraftVersion, Loader, options with { ExpectedLockFingerprint = baseline }, ct).ConfigureAwait(false);
                foreach (var (projectId, verified) in identifiedFiles)
                {
                    var candidate = combined.Nodes.Single(node => node.ProjectId == projectId);
                    if (candidate.Sha1 != verified.Sha1 || candidate.Sha512 != verified.Sha512 || candidate.FileName != verified.FileName)
                        throw new IOException("The selected source changed during update review. Prepare a new review before replacing files.");
                    IncludeIdentifiedLocal(combined, projectId, verified.LocalReplacement!, verified.VerifyReplacementModIds);
                }

                if (distinctContexts.Length > 1)
                {
                    combined.Conflicts.Add(new() { Reason = "different-update-contexts", Resolution = "Select a current Minecraft/loader context or review these files separately." });
                    combined.CanApply = false;
                }

                foreach (var node in combined.Nodes)
                {
                    try
                    {
                        var project = await GetProjectAsync("modrinth", node.ProjectId, ct).ConfigureAwait(false);
                        node.Title = project["title"]?.GetValue<string>();
                        node.IconUrl = project["icon_url"]?.GetValue<string>();
                    }
                    catch (Exception error) when (error is HttpRequestException or IOException)
                    {
                        node.Title = node.ProjectId;
                        combined.Warnings.Add(new() { Reason = "project-metadata-unavailable", ProjectId = node.ProjectId });
                    }
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                combined = null;
                planningFailure = LauncherOperationException.CreateResult(error);
            }
        }

        return new ContentUpdatesPlan(items, combined, baseline, planningFailure, combined?.CanApply == true);
    }
}