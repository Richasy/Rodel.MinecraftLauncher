// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Prepares one review for all missing Fabric dependencies. Downloads only to cache; never changes installed mods.</summary>
    public Task<DependencyRepairReview> PlanDependencyRepairAsync(string instanceId, string sessionId, IReadOnlyDictionary<string, string>? versionSelections = null, CancellationToken cancellationToken = default)
        => PlanDependencyRepairAsync(instanceId, sessionId, versionSelections, ContentPlatform, cancellationToken);

    internal async Task<DependencyRepairReview> PlanDependencyRepairAsync(string instanceId, string sessionId, IReadOnlyDictionary<string, string>? versionSelections, ContentPlatformService online, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        using var cacheUse = await CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false);
        EnsureInstanceIdle(instanceId);
        var instance = Instances.Get(instanceId);
        var session = Sessions.Get(sessionId);
        if (session.InstanceId != instanceId
            || !string.Equals(session.GameDirectory, instance.GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherOperationException("DEPENDENCY_SESSION_MISMATCH", "The diagnostic session belongs to a different instance.");
        }

        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        if (context.Loader != "fabric" || context.MinecraftVersion is not { Length: > 0 } minecraft)
            throw new LauncherOperationException("DEPENDENCY_CONTEXT_UNSUPPORTED", "Automatic dependency repair currently requires a known Fabric game version. Local content was retained.");
        var local = await AnalyzeContentAsync(instanceId, ct).ConfigureAwait(false);
        var fingerprint = DependencyInventoryFingerprint(local.Files);
        var baseline = await ContentService.LockFingerprintAsync(instance.GameDirectory, ct).ConfigureAwait(false);
        var diagnosis = await Maintenance.DiagnoseSessionAsync(sessionId, ct).ConfigureAwait(false);
        var existing = local.Mods;
        var requirements = new Queue<(string Id, string Requester, JsonNode? Range)>();
        foreach (var item in diagnosis.DependencyEvidence.OfType<FabricDependencyEvidence>())
        {
            if (item.Kind is "missing-required-dependency" or "required-dependency" && item.DependencyId is { } id)
            {
                var requester = existing.FirstOrDefault(mod => mod.Id == item.RequestedBy);
                var dependency = requester?.Depends.FirstOrDefault(value => value.Id.ToString() == id && value.Required);
                if (dependency is not null)
                    requirements.Enqueue((id, item.RequestedBy, dependency.Range?.DeepClone()));
                else if (!string.IsNullOrWhiteSpace(item.Requirement))
                    requirements.Enqueue((id, item.RequestedBy, JsonValue.Create(item.Requirement)));
            }
        }

        foreach (var item in local.Findings)
        {
            if (item.Kind == "missing-declared-dependency" && item.Dependency?.Id.ToString() is { } id)
                requirements.Enqueue((id, item.Requester ?? string.Empty, item.Dependency.Range?.DeepClone()));
        }

        var provided = existing.GroupBy(mod => mod.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var requiredRanges = new Dictionary<string, List<JsonNode?>>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, ContentPlanNode>(StringComparer.Ordinal);
        var edges = new List<ContentPlanEdge>();
        var unresolved = new List<ContentPlanFinding>();
        var checkedRequirements = new HashSet<string>(StringComparer.Ordinal);
        var evidence = new List<(string Id, string Requester, JsonNode? Range)>();
        var reviewId = Guid.NewGuid().ToString("N");
        var warnings = new List<ContentPlanFinding>();
        foreach (var duplicate in existing.Where(mod => mod.NestedPath is null).GroupBy(mod => mod.Id).Where(group => group.Count() > 1))
            Problem((duplicate.Key, string.Empty, null), "duplicate-local-mod-id");
        while (requirements.TryDequeue(out var requirement))
        {
            ct.ThrowIfCancellationRequested();
            if (!checkedRequirements.Add(requirement.Id + "\0" + requirement.Requester + "\0" + requirement.Range?.ToJsonString()))
                continue;
            evidence.Add(requirement);
            if (checkedRequirements.Count > 128)
            {
                Problem(requirement, "dependency-graph-limit");
                break;
            }

            if (provided.TryGetValue(requirement.Id, out var present))
            {
                if (!requiredRanges.TryGetValue(requirement.Id, out var ranges)) requiredRanges[requirement.Id] = ranges = [];
                ranges.Add(requirement.Range);
                var mandatory = present.Where(mod => mod.NestedPath is null).ToArray();
                var installedCandidates = mandatory.Length > 0 ? mandatory : [.. present];
                if (!installedCandidates.Any(mod => ranges.All(range => DependencyRangeMatches(mod.Version?.ToString() ?? string.Empty, range) == true)))
                    Problem(requirement, "installed-version-conflict-or-unsupported-range");
                continue;
            }

            var search = await online.SearchAsync(platform: "modrinth", query: requirement.Id, minecraftVersion: minecraft, loader: "fabric", limit: 10, ct: ct).ConfigureAwait(false);
            var hits = (search["hits"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
            // Mod IDs are not platform slugs. A readable fallback only discovers candidates; archive declarations below remain authoritative.
            var readable = DependencyIdVersionSuffixRegex().Replace(requirement.Id, string.Empty).Replace('_', ' ').Replace('-', ' ');
            if (hits.Length == 0 && readable != requirement.Id)
            {
                var fallback = await online.SearchAsync(platform: "modrinth", query: readable, minecraftVersion: minecraft, loader: "fabric", limit: 10, ct: ct).ConfigureAwait(false);
                hits = [.. (fallback["hits"] as JsonArray ?? []).OfType<JsonObject>()];
            }

            var exact = hits.Where(hit => hit["slug"]?.ToString() == requirement.Id).ToArray();
            var candidates = exact.Length > 0 ? exact : [.. hits.Take(5)];
            var matches = new List<(ContentInstallPlan Plan, ContentAnalysis Index)>();
            foreach (var hit in candidates)
            {
                if (hit["project_type"]?.ToString() != "mod" || hit["project_id"]?.ToString() is not { } project)
                    continue;
                var versions = await online.GetVersionsAsync("modrinth", project, minecraft, "fabric", ct).ConfigureAwait(false);
                var requestedVersion = versionSelections?.GetValueOrDefault(project);
                var version = versions.OfType<JsonObject>().FirstOrDefault(value =>
                    (requestedVersion is null || value["id"]?.ToString() == requestedVersion)
                    && DependencyRangeMatches(value["version_number"]?.ToString() ?? string.Empty, requirement.Range) == true);
                if (version is null) continue;
                // Resolve actual archive declarations below, retaining satisfying installed libraries instead of upgrading a provider's whole graph.
                var plan = await online.PlanInstallAsync(platform: "modrinth", projectId: project, versionId: version["id"]!.ToString(), game: instance.GameDirectory, minecraftVersion: minecraft, loader: "fabric", ct: ct, options: new() { VersionSelections = versionSelections, IncludeDependencies = false }).ConfigureAwait(false);
                if (!plan.CanApply
                    || plan.Warnings.Any(value =>
                        value.Reason is "explicit-version-game-metadata-mismatch" or "explicit-version-loader-metadata-mismatch"
                            or "explicit-version-selection" or "explicit-selection-overrides-retained-consumer"))
                {
                    Problem(requirement, "upstream-plan-conflict");
                    continue;
                }

                if (nodes.Count + plan.Nodes.Count > 128)
                {
                    Problem(requirement, "upstream-graph-limit");
                    continue;
                }

                if (plan.Nodes.Any(file => file.Sha1 is null || file.Sha512 is null))
                {
                    Problem(requirement, "missing-download-integrity");
                    continue;
                }

                var downloaded = await online.PreparePlanDownloadsAsync(plan, null, ct).ConfigureAwait(false);
                var stage = Path.Combine(paths.CacheDirectory, "dependency-reviews", reviewId, project, Guid.NewGuid().ToString("N"));
                var mods = Path.Combine(stage, "mods");
                Directory.CreateDirectory(mods);
                foreach (var file in downloaded)
                {
                    if (file.Kind != "mods")
                        throw new InvalidDataException("A mod dependency graph contains a different content type.");
                    File.Copy(file.Source!, Path.Combine(mods, file.FileName), overwrite: false);
                }

                var index = await Content.AnalyzeAsync(stage, ct).ConfigureAwait(false);
                var declared = index.Mods.FirstOrDefault(mod => mod.Id == requirement.Id);
                if (declared is not null && DependencyRangeMatches(declared.Version?.ToString() ?? string.Empty, requirement.Range) == true)
                    matches.Add((plan, index));
            }

            if (matches.Count != 1)
            {
                Problem(requirement, matches.Count == 0 ? "no-verified-candidate-or-unsupported-range" : "ambiguous-projects");
                continue;
            }

            var selected = matches[0];
            foreach (var file in selected.Plan.Nodes)
            {
                var project = file.ProjectId;
                if (nodes.TryGetValue(project, out var chosen) && chosen.VersionId != file.VersionId)
                {
                    Problem(requirement, "different-fixed-versions");
                    continue;
                }

                if (file.Change == "replace")
                {
                    Problem(requirement, "would-replace-existing-content");
                    continue;
                }

                if (!local.Files.Any(item => item.Enabled == true
                    && item.Sha1 == file.Sha1 && file.Sha1 is not null))
                {
                    var node = file with { DesiredEnabled = true };
                    node.Provides = [.. selected.Index.Mods.Where(mod => mod.FileName == file.FileName).Select(mod => mod.Id)];
                    nodes[project] = node;
                }
            }

            foreach (var (project, node) in nodes)
            {
                var metadata = await online.GetProjectAsync("modrinth", project, ct).ConfigureAwait(false);
                node.Title = metadata["title"]?.GetValue<string>() ?? project;
                node.IconUrl = metadata["icon_url"]?.GetValue<string>();
                var ids = node.Provides!.ToHashSet(StringComparer.Ordinal);
                node.RequestedBy = [.. evidence
                    .Where(item => ids.Contains(item.Id) && !ids.Contains(item.Requester))
                    .Select(item => item.Requester).Distinct(StringComparer.Ordinal)
                    .Select(id => existing.FirstOrDefault(mod => mod.Id == id)?.Name?.ToString() ?? id)];
            }

            edges.AddRange(selected.Plan.Edges);
            warnings.AddRange(selected.Plan.Warnings);
            foreach (var mod in selected.Index.Mods)
            {
                var id = mod.Id;
                var file = selected.Plan.Nodes.FirstOrDefault(item => item.FileName == mod.FileName);
                if (mod.NestedPath is null && existing.Any(item => item.Id == id)
                    && !local.Files.Any(item => item.Enabled == true
                        && file?.Sha1 is not null && item.Sha1 == file.Sha1))
                {
                    Problem((id, requirement.Id, requirement.Range), "would-duplicate-existing-mod-id");
                }

                if (!provided.TryGetValue(id, out var old)) provided[id] = old = [];
                if (mod.NestedPath is null && old.Any(item => item.NestedPath is null && item.FileName != mod.FileName))
                {
                    Problem((id, requirement.Id, requirement.Range), "duplicate-top-level-mod-id");
                }
                else
                {
                    old.Add(mod);
                }
            }

            foreach (var mod in selected.Index.Mods)
            {
                foreach (var dependency in mod.Depends)
                {
                    if (dependency.Required && dependency.Id.ToString() is { } id
                        && id is not ("minecraft" or "java" or "fabricloader"))
                    {
                        requirements.Enqueue((id, mod.Id, dependency.Range?.DeepClone()));
                    }
                }
            }
        }

        var combined = new ContentInstallPlan
        {
            Platform = "modrinth",
            GameDirectory = instance.GameDirectory,
            PrimaryProject = nodes.Keys.FirstOrDefault() ?? string.Empty,
            LockFingerprint = baseline,
            Nodes = [.. nodes.Values],
            Edges = edges,
            Warnings = warnings,
            Conflicts = [.. unresolved],
            CanApply = unresolved.Count == 0 && nodes.Count > 0,
        };
        var review = new DependencyRepairReview
        {
            Id = reviewId,
            InstanceId = instanceId,
            SessionId = sessionId,
            Context = context,
            InventoryFingerprint = fingerprint,
            Plan = combined,
            Unresolved = unresolved,
            InstalledFilesChanged = false,
        };
        await JsonFiles.WriteAsync(DependencyReviewPath(reviewId), JsonSerializer.SerializeToNode(review, ContentJsonContext.DependencyReview)!, ct).ConfigureAwait(false);
        return review;

        void Problem((string Id, string Requester, JsonNode? Range) requirement, string reason) =>
            unresolved.Add(new()
            {
                DependencyId = requirement.Id,
                RequestedBy = requirement.Requester,
                Range = requirement.Range?.DeepClone(),
                Reason = reason,
            });
    }

    /// <summary>Applies exactly one previously reviewed batch, after the caller has obtained final user confirmation.</summary>
    public async Task<ContentInstallReceipt> ApplyDependencyRepairAsync(string instanceId, string reviewId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        EnsureInstanceIdle(instanceId);
        var instance = Instances.Get(instanceId);
        var review = JsonFiles.ReadSnapshot(DependencyReviewPath(reviewId), stream => JsonSerializer.Deserialize(stream, ContentJsonContext.DependencyReview))
            ?? throw new InvalidDataException("The dependency review is empty.");
        var plan = review.Plan;
        if (review.InstanceId != instanceId || plan.GameDirectory != instance.GameDirectory)
            throw new LauncherOperationException("DEPENDENCY_REVIEW_MISMATCH", "The review belongs to another instance.");
        var files = await Content.ListAsync(instance.GameDirectory, "mods", ct: ct).ConfigureAwait(false);
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        if (DependencyInventoryFingerprint(files) != review.InventoryFingerprint
            || JsonSerializer.Serialize(context, ContentJsonContext.Default.ContentCompatibility) != JsonSerializer.Serialize(review.Context, ContentJsonContext.Default.ContentCompatibility))
        {
            throw new LauncherOperationException("DEPENDENCY_REVIEW_STALE", "The instance changed after review. Generate a new plan before confirming; local files were retained.");
        }

        return await ContentPlatform.ApplyPlanAsync(plan, DownloadEvents, ct).ConfigureAwait(false);
    }

    private string DependencyReviewPath(string id) =>
        Guid.TryParseExact(id, "N", out _) ? Path.Combine(paths.Root, "dependency-reviews", id + ".json")
        : throw new ArgumentException("Invalid dependency review ID.", nameof(id));

    private static string DependencyInventoryFingerprint(IReadOnlyList<ContentEntry> files) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"{file.Path}\0{JsonValue.Create(file.Enabled)}\0{file.Sha1}")))));

    private static bool? DependencyRangeMatches(string version, JsonNode? range)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        if (range is JsonArray alternatives)
        {
            var results = alternatives.Select(item => DependencyRangeMatches(version, item)).ToArray();
            return results.Contains(true) ? true : results.Contains(null) ? null : false;
        }

        if (range is not JsonValue value || !value.TryGetValue<string>(out var text)) return null;
        if (text == "*" || text == version) return true;
        static Version? Numeric(string input) => Version.TryParse(input.Split('+')[0], out var parsed)
            ? new(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision)) : null;
        if (Numeric(version) is not { } actual) return null;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var op = token.StartsWith(">=", StringComparison.Ordinal) || token.StartsWith("<=", StringComparison.Ordinal) ? token[..2]
                : token.StartsWith('>') || token.StartsWith('<') || token.StartsWith('=') || token.StartsWith('~') ? token[..1] : string.Empty;
            if (Numeric(token[op.Length..]) is not { } target) return null;
            var comparison = actual.CompareTo(target);
            if (!(op switch
            {
                ">=" => comparison >= 0,
                "<=" => comparison <= 0,
                ">" => comparison > 0,
                "<" => comparison < 0,
                // Fabric '~' retains the major/minor components and enforces its lower bound.
                "~" => comparison >= 0 && actual.Major == target.Major && actual.Minor == target.Minor,
                _ => comparison == 0,
            }))
            {
                return false;
            }
        }

        return string.IsNullOrWhiteSpace(text) ? null : true;
    }

    [System.Text.RegularExpressions.GeneratedRegex("(?:[_-]v?\\d+)$")]
    private static partial System.Text.RegularExpressions.Regex DependencyIdVersionSuffixRegex();
}