// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    private async Task<IReadOnlyList<ContentEntry>> ReadOnlineInventoryAsync(string game, CancellationToken ct)
    {
        var entries = new List<ContentEntry>();
        foreach (var kind in new[] { "mods", "resourcepacks", "shaderpacks" })
            entries.AddRange((await Content.ListAsync(game, kind, ct: ct).ConfigureAwait(false)).Where(entry => entry.IsContent));
        foreach (var entry in entries.Where(entry => entry.Directory))
            entry.Sha1 = await ContentService.SourceFingerprintAsync(Path.GetFullPath(Path.Combine(game, entry.Path)), ct).ConfigureAwait(false);
        return entries;
    }

    // Called inside the content transaction lock, after all downloads. No new release is resolved here.
    // Existing files/policies must remain identical; independent additions and the exact same dependency can coexist.
    private async Task ValidateOnlineReviewAsync(ContentReview review, IReadOnlyList<ContentPlanNode> candidates, CancellationToken ct)
    {
        EnsureInstanceIdle(review.InstanceId, allowOnlineContent: true);
        var instance = Instances.Get(review.InstanceId);
        var context = await GetContentContextAsync(review.InstanceId, ct: ct).ConfigureAwait(false);
        if (instance.GameDirectory != review.GameDirectory || review.OnlineBaseline is null || review.OnlineTracking is null
            || JsonSerializer.Serialize(context, ContentJsonContext.Default.ContentCompatibility) != JsonSerializer.Serialize(review.Context, ContentJsonContext.Default.ContentCompatibility))
        {
            throw OnlineReviewChanged();
        }

        var current = await ReadOnlineInventoryAsync(review.GameDirectory, ct).ConfigureAwait(false);
        var tracking = await Content.GetLockAsync(review.GameDirectory, ct).ConfigureAwait(false);
        foreach (var before in (review.OnlineTracking["files"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var after = (tracking["files"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(entry => entry["path"]?.ToString() == before["path"]?.ToString());
            if (!JsonNode.DeepEquals(before, after)) throw OnlineReviewChanged();
        }

        foreach (var before in review.OnlineBaseline)
        {
            var after = current.FirstOrDefault(entry => entry.Path == before.Path);
            if (after is null || after.Sha1 != before.Sha1 || after.Enabled != before.Enabled || after.Directory != before.Directory
                || !JsonNode.DeepEquals(before.Tracking, after.Tracking))
            {
                throw OnlineReviewChanged();
            }
        }

        var additions = current.Where(entry => !review.OnlineBaseline.Any(before => before.Path == entry.Path)).ToArray();
        var knownVersions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in current)
        {
            if (entry.Tracking?["versionId"]?.ToString() is { } version && entry.Tracking?["projectId"]?.ToString() is { } project)
                knownVersions[version] = project;
        }

        foreach (var candidate in candidates) knownVersions[candidate.VersionId] = candidate.ProjectId;
        foreach (var candidate in candidates)
        {
            var inspection = candidate.Kind == "mods" && candidate.Source is not null
                ? await ContentService.InspectSourceAsync(candidate.Kind, candidate.Source!, ct).ConfigureAwait(false) : null;
            foreach (var added in additions)
            {
                var sameProject = added.Tracking?["platform"]?.ToString() == candidate.Platform
                    && added.Tracking?["projectId"]?.ToString() == candidate.ProjectId;
                if (sameProject)
                {
                    if (added.Tracking?["versionId"]?.ToString() != candidate.VersionId || added.Sha1 != candidate.Sha1
                        || added.Enabled != (candidate.DesiredEnabled ?? true))
                    {
                        throw OnlineReviewChanged();
                    }
                }
                else if (inspection is not null && ContentService.ModIdentitiesOverlap(inspection, added))
                {
                    throw OnlineReviewChanged();
                }

                if (added.Tracking is { } source && source["platform"]?.ToString() == candidate.Platform)
                {
                    if (ConflictsWith(candidate.Upstream, source["projectId"]?.ToString(), source["versionId"]?.ToString(), knownVersions)
                        || ConflictsWith(source["upstream"], candidate.ProjectId, candidate.VersionId, knownVersions))
                    {
                        throw OnlineReviewChanged();
                    }
                }
            }
        }
    }

    private static bool ConflictsWith(JsonNode? upstream, string? project, string? version, IReadOnlyDictionary<string, string> knownVersions)
    {
        foreach (var dependency in (upstream?["dependencies"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var requiredProject = dependency["project_id"]?.ToString();
            var requiredVersion = dependency["version_id"]?.ToString();
            requiredProject ??= requiredVersion is null ? null : knownVersions.GetValueOrDefault(requiredVersion);
            var matches = requiredProject is not null ? requiredProject == project : requiredVersion is not null && requiredVersion == version;
            if (matches && (dependency["dependency_type"]?.ToString() == "incompatible"
                || (dependency["dependency_type"]?.ToString() == "required" && requiredVersion is not null && requiredVersion != version)))
            {
                return true;
            }
        }

        return false;
    }

    private static LauncherOperationException OnlineReviewChanged() => new("CONTENT_REVIEW_STALE",
        "Content or runtime changed incompatibly after review. Existing files were retained; prepare and confirm a new list.");
}