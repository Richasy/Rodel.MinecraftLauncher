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
    /// <summary>Freezes exact online candidates and the current inventory before confirmation.</summary>
    public async Task<ContentReview> ReviewContentInstallAsync(string instanceId, string projectId, ContentWorkflowOptions options, CancellationToken ct = default)
    {
        EnsureInstanceIdle(instanceId, allowOnlineContent: true);
        var instance = Instances.Get(instanceId);
        var fingerprint = await ContentInventoryFingerprintAsync(instanceId, ct).ConfigureAwait(false);
        var baseline = await ReadOnlineInventoryAsync(instance.GameDirectory, ct).ConfigureAwait(false);
        var tracking = await Content.GetLockAsync(instance.GameDirectory, ct).ConfigureAwait(false);
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        var plan = (ContentInstallPlan)await InstallContentAsync(instanceId, projectId, options with { PlanOnly = true }, ct).ConfigureAwait(false);
        foreach (var node in plan.Nodes)
        {
            var project = await ContentPlatform.GetProjectAsync("modrinth", node.ProjectId, ct).ConfigureAwait(false);
            node.Title = project["title"]?.GetValue<string>();
            node.IconUrl = project["icon_url"]?.GetValue<string>();
            node.DesiredEnabled = true;
        }

        if (fingerprint != await ContentInventoryFingerprintAsync(instanceId, ct).ConfigureAwait(false)
            || !JsonNode.DeepEquals(tracking, await Content.GetLockAsync(instance.GameDirectory, ct).ConfigureAwait(false)))
        {
            throw OnlineReviewChanged();
        }

        var review = new ContentReview
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            Mode = "online",
            GameDirectory = instance.GameDirectory,
            Context = context,
            Fingerprint = fingerprint,
            OnlineBaseline = baseline,
            OnlineTracking = tracking,
            Plan = plan,
        };
        await JsonFiles.WriteAsync(ContentReviewPath(review.Id), JsonSerializer.SerializeToNode(review, ContentJsonContext.Review)!, ct).ConfigureAwait(false);
        return review;
    }

    /// <summary>Inspects selected local sources and freezes their exact digests without copying them.</summary>
    public async Task<ContentReview> ReviewContentFilesAsync(string instanceId, IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string>? kinds = null, CancellationToken ct = default)
    {
        if (sources.Count == 0) throw new ArgumentException("Select at least one content source.", nameof(sources));
        EnsureInstanceIdle(instanceId);
        var instance = Instances.Get(instanceId);
        var fingerprint = await ContentInventoryFingerprintAsync(instanceId, ct).ConfigureAwait(false);
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        var items = new List<ContentReviewItem>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new Dictionary<string, ContentReviewItem>(StringComparer.Ordinal);
        var lockFingerprint = await ContentService.LockFingerprintAsync(instance.GameDirectory, ct).ConfigureAwait(false);
        var installedMods = await Content.ListAsync(instance.GameDirectory, "mods", ct: ct).ConfigureAwait(false);
        foreach (var input in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.GetFullPath(input);
            var detected = await ContentService.DetectSourceAsync(source, ct).ConfigureAwait(false);
            var candidates = detected.Candidates;
            var selected = kinds?.GetValueOrDefault(source) ?? (candidates.Count == 1 ? candidates[0].Kind : null);
            var item = new ContentReviewItem { Source = source, FileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(source)), Candidates = candidates };
            if (selected is null)
            {
                item.Issue = candidates.Count == 0 ? "unrecognized-package" : "choose-content-type";
            }
            else
            {
                var inspection = await ContentService.InspectSourceAsync(selected, source, ct).ConfigureAwait(false);
                var name = item.FileName;
                var root = ContentService.DirectoryFor(instance.GameDirectory, selected, null);
                var target = Path.Combine(root, name);
                var sourceHash = await ContentService.SourceFingerprintAsync(source, ct).ConfigureAwait(false);
                item.Kind = selected; item.Inspection = inspection;
                item.SourceFingerprint = sourceHash;
                item.Action = "import";
                if (!targets.Add(selected + "\0" + name)) item.Issue = "duplicate-target-name";
                if (File.Exists(target) || Directory.Exists(target))
                {
                    var same = sourceHash == await ContentService.SourceFingerprintAsync(target, ct).ConfigureAwait(false);
                    item.Action = same ? "reuse" : "conflict";
                    if (!same) item.Issue = "different-content-same-name";
                }

                var disabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var counterpart = Path.Combine(root, disabled ? name[..^9] : name + ".disabled");
                if (File.Exists(counterpart) || Directory.Exists(counterpart))
                {
                    var same = sourceHash == await ContentService.SourceFingerprintAsync(counterpart, ct).ConfigureAwait(false);
                    item.Action = same ? "reuse-state" : "conflict";
                    if (!same) item.Issue = "different-content-same-name";
                }

                if (selected == "mods")
                {
                    var declared = ContentService.GetModIdentities(inspection.Metadata);
                    foreach (var identity in declared)
                    {
                        if (identities.TryGetValue(identity, out var previous))
                        {
                            previous.Issue = "duplicate-mod-id";
                            item.Issue = "duplicate-mod-id";
                        }
                        else
                        {
                            identities.Add(identity, item);
                        }
                    }

                    var replacements = installedMods.Where(entry => ContentService.ModIdentitiesOverlap(inspection, entry)).ToArray();
                    if (replacements.Length > 0)
                    {
                        item.Replacements = replacements;
                        if (replacements.Length == 1 && replacements[0].Sha1 == inspection.Sha1)
                            item.Action = "reuse";
                        else
                            item.Action = "replace";
                        if (item.Issue == "different-content-same-name"
                            && replacements.Any(entry => string.Equals(entry.FileName, name, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(entry.FileName, Path.GetFileName(counterpart), StringComparison.OrdinalIgnoreCase)))
                        {
                            item.Issue = null;
                        }

                        if (replacements.Any(entry => !declared.IsSupersetOf(ContentService.GetModIdentities(entry.Metadata))))
                            item.Issue ??= "partial-mod-id-replacement";
                    }
                }
            }

            items.Add(item);
        }

        var review = new ContentReview
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            Mode = "local",
            GameDirectory = instance.GameDirectory,
            Context = context,
            Fingerprint = fingerprint,
            LockFingerprint = lockFingerprint,
            Items = items,
            CanApply = items.All(item => item.Issue is null),
        };
        await JsonFiles.WriteAsync(ContentReviewPath(review.Id), JsonSerializer.SerializeToNode(review, ContentJsonContext.Review)!, ct).ConfigureAwait(false);
        return review;
    }

    /// <summary>Executes a frozen review only after the caller obtains final confirmation.</summary>
    public async Task<ContentResult> ApplyContentReviewAsync(string instanceId, string reviewId, CancellationToken ct = default)
    {
        var review = await GetContentReviewAsync(instanceId, reviewId, ct).ConfigureAwait(false);
        EnsureInstanceIdle(instanceId, allowOnlineContent: review.Mode == "online" && review.OnlineBaseline is not null);
        if (review.Mode == "online" && review.OnlineBaseline is not null)
        {
            if (review.Plan is not { } onlinePlan)
                throw new LauncherOperationException("CONTENT_REVIEW_UNRESOLVED", "There are no selected applicable changes in this review.");
            await ValidateOnlineReviewAsync(review, onlinePlan.Nodes, ct).ConfigureAwait(false);
            return await ContentPlatform.ApplyPlanAsync(onlinePlan, DownloadEvents, ct,
                (candidates, token) => ValidateOnlineReviewAsync(review, candidates, token)).ConfigureAwait(false);
        }

        if (review.Stale == true)
            throw new LauncherOperationException("CONTENT_REVIEW_STALE", "Content or runtime changed after review. Prepare and confirm a new list.");
        var instance = Instances.Get(instanceId);
        if (review.Mode == "builtin")
            return await ApplyBundledModsAsync(review, ct).ConfigureAwait(false);
        if (review.Mode == "online")
        {
            if (review.Plan is not { } plan)
                throw new LauncherOperationException("CONTENT_REVIEW_UNRESOLVED", "There are no selected applicable changes in this review.");
            return await ContentPlatform.ApplyPlanAsync(plan, DownloadEvents, ct).ConfigureAwait(false);
        }

        if (review.CanApply != true)
            throw new LauncherOperationException("CONTENT_REVIEW_UNRESOLVED", "Resolve the package types and file conflicts before confirming.");
        var items = review.Items!;
        foreach (var item in items)
        {
            var source = item.Source!;
            await ContentService.InspectSourceAsync(item.Kind!, source, ct).ConfigureAwait(false);
            if (item.SourceFingerprint != await ContentService.SourceFingerprintAsync(source, ct).ConfigureAwait(false))
                throw new LauncherOperationException("CONTENT_SOURCE_CHANGED", "A selected source changed after review. Nothing was copied.");
        }

        var results = new List<ContentItemOutcome>();
        var replacements = items.Where(item => item.Action == "replace").ToArray();
        ContentInstallReceipt? replacementReceipt = null;
        foreach (var item in items.OrderByDescending(item => item.Action == "replace"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ContentResult added;
                if (item.Action == "replace")
                {
                    replacementReceipt ??= await Content.ApplyLocalModReplacementsAsync(instance.GameDirectory, replacements,
                        review.LockFingerprint ?? throw new InvalidDataException("The local replacement review has no tracking baseline."), ct).ConfigureAwait(false);
                    added = replacementReceipt;
                }
                else
                {
                    added = await Content.AddAsync(game: instance.GameDirectory, kind: item.Kind!, source: item.Source!, ct: ct, expectedSourceFingerprint: item.SourceFingerprint!).ConfigureAwait(false);
                }

                results.Add(new ContentItemSuccess { Source = item.Source, Kind = item.Kind, Data = added });
                Report(new LauncherProgress { Stage = "content-import", CompletedFiles = results.Count, TotalFiles = items.Count });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                results.Add(ContentFailureOutcome(error));
                throw new LauncherOperationException("CONTENT_BATCH_PARTIAL", "Import stopped; completed files were retained. Inspect the per-item result before retrying.",
                    JsonSerializer.SerializeToNode<ContentResult>(new ContentBatchResult { Items = results, SucceededCount = results.Count - 1, FailedCount = 1 }, ContentJsonContext.Result));
            }
        }

        return new ContentBatchResult { Items = results, SucceededCount = results.Count, FailedCount = 0 };
    }

    /// <summary>Reads the exact saved review and reports drift without applying or resolving new candidates.</summary>
    public async Task<ContentReview> GetContentReviewAsync(string instanceId, string reviewId, CancellationToken ct = default)
    {
        var instance = Instances.Get(instanceId);
        var review = JsonFiles.ReadSnapshot(ContentReviewPath(reviewId), stream => JsonSerializer.Deserialize(stream, ContentJsonContext.Review))
            ?? throw new InvalidDataException("The content review is empty.");
        if (review.InstanceId != instanceId || review.GameDirectory != instance.GameDirectory)
            throw new LauncherOperationException("CONTENT_REVIEW_MISMATCH", "The review belongs to another instance.");
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        review.Stale = JsonSerializer.Serialize(review.Context, ContentJsonContext.Default.ContentCompatibility) != JsonSerializer.Serialize(context, ContentJsonContext.Default.ContentCompatibility)
            || review.Fingerprint != await ContentInventoryFingerprintAsync(instanceId, ct).ConfigureAwait(false)
            || (review.LockFingerprint is not null && review.LockFingerprint != await ContentService.LockFingerprintAsync(instance.GameDirectory, ct).ConfigureAwait(false));
        return review;
    }

    private string ContentReviewPath(string id) => Guid.TryParseExact(id, "N", out _)
        ? Path.Combine(paths.Root, "content-reviews", id + ".json") : throw new ArgumentException("Invalid content review ID.", nameof(id));

    private async Task<string> ContentInventoryFingerprintAsync(string instanceId, CancellationToken ct)
    {
        var game = Instances.Get(instanceId).GameDirectory;
        var rows = new List<string>();
        foreach (var kind in new[] { "mods", "resourcepacks", "shaderpacks" })
        {
            foreach (var item in await Content.ListAsync(game, kind, ct: ct).ConfigureAwait(false))
            {
                if (item.IsContent)
                {
                    var digest = item.Directory == true
                        ? await ContentService.SourceFingerprintAsync(Path.GetFullPath(Path.Combine(game, item.Path)), ct).ConfigureAwait(false)
                        : item.Sha1;
                    rows.Add($"{kind}\0{item.Path}\0{JsonValue.Create(item.Enabled)}\0{digest}");
                }
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Order(StringComparer.Ordinal)))));
    }
}