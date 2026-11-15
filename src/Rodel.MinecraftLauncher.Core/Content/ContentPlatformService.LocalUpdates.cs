// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentPlatformService
{
    private static bool HasOnlineSource(ContentEntry item) =>
        item.Tracking?["platform"]?.ToString() is { Length: > 0 }
        && item.Tracking["projectId"]?.ToString() is { Length: > 0 };

    private sealed record LocalUpdateSource(ContentProvenance? Provenance, IReadOnlyList<ContentSourceCandidate> Candidates);

    private async Task<LocalUpdateSource> IdentifyUpdateSourceAsync(ContentEntry item, ContentInstallOptions options, CancellationToken ct)
    {
        if (HasOnlineSource(item) || item.Directory || item.Kind != "mods" || item.Sha1 is null || item.Tracking?["pinned"]?.GetValue<bool>() == true)
            return new(null, []);
        var identified = await LookupIdentityAsync(item.Sha1, "sha1", options.SourceSelections?.GetValueOrDefault(item.FileName), null,
            options.MinecraftVersion, options.Loader, false, ct).ConfigureAwait(false);
        if (identified.Provenance is { } provenance) return new(provenance, []);
        var queries = ContentService.GetModIdentities(item.Metadata).ToList();
        if (item.Metadata is JsonObject metadata)
        {
            var declarations = new List<JsonObject> { metadata };
            if (metadata["mods"] is JsonArray mods) declarations.AddRange(mods.OfType<JsonObject>());
            if (metadata["quilt_loader"] is JsonObject quilt && quilt["metadata"] is JsonObject details) declarations.Add(details);
            foreach (var declaration in declarations)
            {
                var name = declaration["name"] ?? declaration["displayName"];
                if (name is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) queries.Add(text);
            }
        }

        var candidates = new Dictionary<string, ContentSourceCandidate>(StringComparer.Ordinal);
        foreach (var query in queries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var search = await SearchAsync("modrinth", query, limit: 10, minecraftVersion: options.MinecraftVersion,
                loader: options.Loader, ct: ct).ConfigureAwait(false);
            foreach (var hit in (search["hits"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (hit["project_type"]?.ToString() != "mod" || hit["project_id"]?.ToString() is not { Length: > 0 } project)
                    continue;
                candidates.TryAdd(project, new()
                {
                    ProjectId = project,
                    Title = hit["title"]?.ToString() ?? project,
                    Author = hit["author"]?.ToString(),
                    Slug = hit["slug"]?.ToString(),
                    IconUrl = hit["icon_url"]?.ToString()
                });
            }
        }

        return new(null, [.. candidates.Values]);
    }

    private async Task<ContentResult> PlanSelectedLocalUpdateAsync(string game, ContentEntry local, LocalUpdateSource discovery,
        string? world, ContentInstallOptions options, CancellationToken ct)
    {
        var project = options.SourceSelections?.GetValueOrDefault(local.FileName);
        if (project is null)
        {
            return new ContentUpdateSkipped
            {
                Reason = discovery.Candidates.Count > 0 ? "source-selection-required" : "untracked",
                FileName = local.FileName,
                LocalRetained = true,
                SourceCandidates = discovery.Candidates
            };
        }

        if (!discovery.Candidates.Any(candidate => candidate.ProjectId == project))
            throw new InvalidDataException("The selected source is no longer among the local mod's candidates. Search again before updating.");
        if (options.MinecraftVersion is null && options.VersionSelections?.ContainsKey(project) != true)
            return new ContentUpdateSkipped { Reason = "update-context-required", FileName = local.FileName, LocalRetained = true, SourceCandidates = discovery.Candidates };
        var plan = await PlanInstallAsync("modrinth", project, options.VersionSelections?.GetValueOrDefault(project), game,
            local.Kind, world, options.MinecraftVersion, options.Loader, options, ct).ConfigureAwait(false);
        if (!plan.CanApply) return plan;
        var node = plan.Nodes.Single(candidate => candidate.ProjectId == project);
        // Search titles and slugs are suggestions. Inspect the actual root archive before presenting a replacement.
        var rootPlan = new ContentInstallPlan
        {
            Platform = plan.Platform,
            PrimaryProject = project,
            GameDirectory = game,
            LockFingerprint = plan.LockFingerprint,
            Nodes = [node],
            Edges = [],
            Conflicts = [],
            Warnings = [],
            CanApply = true
        };
        var downloaded = (await PreparePlanDownloadsAsync(rootPlan, null, ct).ConfigureAwait(false)).Single();
        var inspected = await ContentService.InspectPackageAsync(downloaded.Source!, ct).ConfigureAwait(false);
        var declared = ContentService.GetModIdentities(local.Metadata);
        if (declared.Count == 0 || !ContentService.GetModIdentities(inspected.Metadata).IsSupersetOf(declared))
            throw new InvalidDataException("The selected source's archive does not provide the local mod IDs. The local file was retained.");
        var current = await _content.ListAsync(game, local.Kind, world, ct).ConfigureAwait(false);
        if (current.Any(entry => entry.Path != local.Path && ContentService.ModIdentitiesOverlap(inspected, entry)))
            throw new InvalidDataException("The selected archive overlaps another installed mod. Review the conflicting files before replacing it.");
        plan.UpdateContext = new() { Source = "current-instance", MinecraftVersion = options.MinecraftVersion, Loader = options.Loader };
        IncludeIdentifiedLocal(plan, project, local, verifyModIds: true);
        return plan;
    }

    private static JsonObject? UpdateTracking(ContentEntry item, ContentProvenance? identified)
    {
        if (identified is null) return item.Tracking;
        var tracking = item.Tracking?.DeepClone().AsObject() ?? [];
        var provenance = JsonSerializer.SerializeToNode(identified, ContentJsonContext.Default.ContentProvenance)!.AsObject();
        foreach (var pair in provenance) tracking[pair.Key] = pair.Value?.DeepClone();
        tracking["sha1"] = item.Sha1;
        return tracking;
    }

    private static void IncludeIdentifiedLocal(ContentInstallPlan plan, string projectId, ContentEntry local, bool verifyModIds = false)
    {
        var node = plan.Nodes.Single(candidate => candidate.ProjectId == projectId);
        node.LocalReplacement = local;
        node.VerifyReplacementModIds = verifyModIds;
        node.DesiredEnabled = local.Enabled;
        node.Change = "replace";
        if (!node.ExistingFiles.Any(entry => entry["path"]?.ToString() == local.Path))
            node.ExistingFiles = [.. node.ExistingFiles, local.ToRecord()];
    }
}