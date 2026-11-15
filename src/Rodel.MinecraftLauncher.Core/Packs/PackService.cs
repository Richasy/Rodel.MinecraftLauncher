// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

public sealed partial class PackService
{
    private readonly LauncherPaths _paths;
    private readonly HttpClient _http;
    private readonly PackArchive _archives;
    private readonly IProgress<DownloadProgress>? _progress;

    /// <summary>Shares the selected network transport and progress observer across pack metadata and verified file acquisition.</summary>
    public PackService(LauncherPaths paths, HttpClient? client = null, ICurseForgeProvider? curseForgeProvider = null, IProgress<DownloadProgress>? progress = null)
    {
        _paths = paths;
        _http = client ?? NetworkSettings.Client(paths, TimeSpan.FromMinutes(5));
        _progress = progress;
        _archives = new(paths, _http, curseForgeProvider, progress);
    }

    /// <summary>Inspects and validates a pack archive without extracting it or changing an instance.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Retain the public service instance API for callers and future instance configuration.")]
    public PackInspection Inspect(string archivePath) => PackArchive.Inspect(archivePath);
    /// <summary>Reports whether an instance has persisted pack ownership metadata, without judging launch compatibility.</summary>
    public bool IsManaged(string instanceId) => File.Exists(PackLock(new InstanceStore(_paths).Get(instanceId)));

    /// <summary>Reads the retained pack declaration and missing-file results; this does not revalidate files or prove runtime readiness.</summary>
    public PackState GetState(string instanceId)
    {
        var instance = new InstanceStore(_paths).Get(instanceId);
        return Result(instance, JsonFiles.ReadObject(PackLock(instance)).Deserialize(PackJsonContext.Default.PackContentState)!);
    }
    /// <summary>Imports a pack archive into a new instance with explicit optional-file choices and recoverable missing-file results.</summary>
    public async Task<PackState> ImportAsync(string archivePath, string? name = null, bool download = true, string? manualDirectory = null, PackOptionalSelection? optionalSelection = null, OnlinePackSelection? onlineSource = null, CancellationToken cancellationToken = default)
    {
        var inspected = Inspect(archivePath);
        var store = new InstanceStore(_paths);
        var instance = await store.CreateAsync(name ?? inspected.Name?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(archivePath), inspected.MinecraftVersion, cancellationToken).ConfigureAwait(false);
        try
        {
            using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
            var prepared = await _archives.PrepareAsync(archivePath, instance.GameDirectory, download, manualDirectory, cancellationToken, optionalSelection?.Resolve()).ConfigureAwait(false);
            if (onlineSource is not null)
                prepared.OnlineSource = onlineSource;
            using var contentLease = await ResourceLease.AcquireAsync(resource: ContentLock(instance), cancellationToken: cancellationToken).ConfigureAwait(false);
            await WritePackAsync(instance, prepared, cancellationToken).ConfigureAwait(false);
            await WriteContentAsync(instance, prepared.Files, null, cancellationToken).ConfigureAwait(false);
            return Result(instance, prepared);
        }
        catch
        {
            await store.RemoveAsync(instance.Id, deleteFiles: true, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Builds a pack change review for the selected instance and archive before applying content changes.</summary>
    public async Task<PackReview> ReviewAsync(string instanceId, string archivePath, bool download = true, string? manualDirectory = null, PackOptionalSelection? optionalSelection = null, bool missingOnly = false, OnlinePackSelection? onlineSource = null, CancellationToken cancellationToken = default)
    {
        var instance = new InstanceStore(_paths).Get(instanceId);
        var id = Guid.NewGuid().ToString("N");
        var operation = OperationDirectory(id);
        var incomingDirectory = Path.Combine(operation, "incoming");
        Directory.CreateDirectory(operation);
        try
        {
            var baseline = ReadLock(instance);
            if (missingOnly && !JsonNode.DeepEquals(Inspect(archivePath).Manifest, baseline["manifest"]))
                throw new InvalidDataException("Completion archive differs from the imported manifest. Use pack review/update for a new pack version.");
            var oldSelection = baseline["optionalSelection"]?.Deserialize(PackJsonContext.Default.PackOptionalState);
            var choices = optionalSelection?.Resolve(oldSelection) ?? oldSelection ?? new PackOptionalState();
            var incoming = await _archives.PrepareAsync(archivePath, incomingDirectory, download, manualDirectory, cancellationToken, choices, instance.GameDirectory, !missingOnly,
                missingOnly ? (baseline["missing"] as JsonArray ?? []).Select(item => new PackFileDeclaration(item!["metadata"]!.AsObject())).ToArray() : null).ConfigureAwait(false);
            if (onlineSource is not null)
                incoming.OnlineSource = onlineSource;
            else if (missingOnly && baseline["onlineSource"] is not null)
                incoming.OnlineSource = baseline["onlineSource"]!.Deserialize(PackJsonContext.Default.OnlinePackSelection);
            if (missingOnly)
            {
                var merged = Records(baseline["files"] as JsonArray ?? []);
                foreach (var record in Records(incoming.Files))
                    merged[record.Key] = record.Value;
                incoming.Files = [.. merged.Values];
                var original = baseline.Deserialize(PackJsonContext.Default.PackContentState)!;
                incoming.Skipped = incoming.Format == "hmcl"
                    ? [.. original.Skipped.Concat(incoming.Skipped).DistinctBy(item => item.Declaration.Path, StringComparer.OrdinalIgnoreCase)]
                    : original.Skipped;
                incoming.OptionalChoices = original.OptionalChoices;
            }

            using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
            var old = ReadLock(instance);
            if (!JsonNode.DeepEquals(baseline, old))
                throw new InvalidOperationException("Pack ownership changed while preparing this review. Review the update again.");
            var oldFiles = Records(old["files"] as JsonArray ?? []);
            var newFiles = Records(incoming.Files);
            var disabled = incoming.Skipped.Where(item => item.SkipReason == "locally-disabled")
                .ToDictionary(item => item.Declaration.Path!, item => item.Declaration.Sha1!, StringComparer.OrdinalIgnoreCase);
            var actions = new List<PackFileChange>();
            var hasMissing = incoming.Missing.Count > 0;
            foreach (var relative in oldFiles.Keys.Union(newFiles.Keys, StringComparer.OrdinalIgnoreCase).Union(disabled.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                var target = PackFiles.Safe(instance.GameDirectory, relative);
                var current = await PackFiles.Sha1Async(target, cancellationToken).ConfigureAwait(false);
                var before = oldFiles.GetValueOrDefault(relative)?.Sha1;
                var next = newFiles.GetValueOrDefault(relative)?.Sha1 ?? disabled.GetValueOrDefault(relative);
                var protectedPath = relative.StartsWith("saves/", StringComparison.OrdinalIgnoreCase);
                string action, reason;
                if (protectedPath)
                {
                    action = "preserve";
                    reason = "Player saves are never replaced by a pack update.";
                }
                else if (disabled.ContainsKey(relative))
                {
                    action = "preserve";
                    reason = "Retain the locally disabled HMCL mod; do not download or re-enable it.";
                }
                else if (next == current)
                {
                    action = "unchanged";
                    reason = "Current content already matches incoming pack.";
                }
                else if (next is null && hasMissing)
                {
                    action = "preserve";
                    reason = "Incomplete incoming pack cannot establish safe upstream removals.";
                }
                else if (incoming.Format == "hmcl" && current is null && next is not null)
                {
                    action = "add";
                    reason = "Restore the HMCL distribution's missing managed file.";
                }
                else if (current == before && before is not null)
                {
                    action = next is null ? "delete" : "replace";
                    reason = "Current file still matches the previously installed upstream hash.";
                }
                else if (current is null && before is null && next is not null)
                {
                    action = "add";
                    reason = "New upstream file has no local collision.";
                }
                else if (next == before && before is not null)
                {
                    action = "preserve";
                    reason = "Upstream is unchanged; retain the user's local edit or removal.";
                }
                else
                {
                    action = "conflict";
                    reason = "User content differs from the owned upstream baseline; keep it and expose incoming content beside the report.";
                }

                if (missingOnly && action == "conflict" && newFiles.TryGetValue(relative, out var blocked) && !oldFiles.ContainsKey(relative))
                {
                    incoming.Missing.Add(new() { Path = relative, State = "local-conflict", Reason = "A local file already occupies this missing pack path. Preserve or move it explicitly before completing; incoming bytes are available in the review report.", Metadata = blocked.Metadata!.AsObject() });
                    incoming.Complete = false;
                }

                actions.Add(new(relative, action, current, before, next, reason));
            }

            var review = new PackReview
            {
                OperationId = id,
                InstanceId = instance.Id,
                GameDirectory = instance.GameDirectory,
                Phase = "reviewed",
                MissingOnly = missingOnly,
                HasChanges = actions.Any(item => item.Action is "add" or "replace" or "delete" or "conflict")
                    || !JsonNode.DeepEquals(old["manifest"], incoming.Manifest)
                    || !JsonNode.DeepEquals(old["optionalSelection"], JsonSerializer.SerializeToNode(incoming.OptionalSelection, PackJsonContext.Default.PackOptionalState))
                    || (old["sourceDetached"]?.GetValue<bool>() == true
                        && (incoming.Format == "hmcl" || incoming.OnlineSource is not null)),
                Incoming = incoming,
                PreviousLock = old,
                Actions = actions,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                ReportPath = Path.Combine(operation, "review.json"),
                IncomingDirectory = incomingDirectory,
                ChangeSummary = await PackChangeSummaryBuilder.BuildAsync(instance.GameDirectory, incomingDirectory, actions, cancellationToken).ConfigureAwait(false)
            };
            await WriteReviewAsync(review, cancellationToken).ConfigureAwait(false);
            return review;
        }
        catch
        {
            if (Directory.Exists(operation))
                Directory.Delete(operation, recursive: true);
            throw;
        }
    }

    /// <summary>Updates an existing instance from a pack archive while retaining operation metadata needed for recovery.</summary>
    public async Task<PackReview> UpdateAsync(string instanceId, string archivePath, bool download = true, string? manualDirectory = null, PackOptionalSelection? optionalSelection = null, OnlinePackSelection? onlineSource = null, CancellationToken cancellationToken = default)
    {
        var review = await ReviewAsync(instanceId: instanceId, archivePath: archivePath, download: download, manualDirectory: manualDirectory, cancellationToken: cancellationToken, optionalSelection: optionalSelection, onlineSource: onlineSource).ConfigureAwait(false);
        return await ApplyAsync(review.OperationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Completes only missing declarations from the original manifest, retaining optional choices and player overrides.</summary>
    public async Task<PackReview> CompleteAsync(string instanceId, string? archivePath = null, bool download = true, string? manualDirectory = null, CancellationToken cancellationToken = default)
    {
        var instance = new InstanceStore(_paths).Get(instanceId);
        var pack = ReadLock(instance);
        archivePath ??= pack["archivePath"]?.GetValue<string>() ?? throw new InvalidOperationException("Supply the original pack archive to complete missing files.");
        if (!File.Exists(archivePath) && pack["format"]?.ToString() == "hmcl")
            archivePath = await MaterializeHmclManifestAsync(pack["manifest"]!.AsObject(), cancellationToken).ConfigureAwait(false);
        var review = await ReviewAsync(instanceId: instanceId, archivePath: archivePath, download: download, manualDirectory: manualDirectory, cancellationToken: cancellationToken, missingOnly: true).ConfigureAwait(false);
        return await ApplyAsync(review.OperationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies the staged review once, preserving local changes and separately reporting available files and release agreement.</summary>
    public async Task<PackReview> ApplyAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var operation = OperationDirectory(operationId);
        var file = Path.Combine(operation, "review.json");
        using var operationLease = await ResourceLease.AcquireAsync(resource: file, cancellationToken: cancellationToken).ConfigureAwait(false);
        var review = JsonFiles.ReadObject(file).Deserialize(PackJsonContext.Default.PackReview)! with { ReportPath = file };
        if (review.Phase != "reviewed")
            throw new InvalidOperationException("Only a reviewed pack update can be applied once.");
        if (review.Incoming.Format == "hmcl" && !review.MissingOnly && !review.Incoming.Complete)
        {
            throw new InvalidOperationException("The HMCL release has missing or unverified files. Review it again after completing the distribution; the current client is unchanged.");
        }

        var instance = new InstanceStore(_paths).Get(review.InstanceId);
        if (!string.Equals(instance.GameDirectory, review.GameDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Instance moved after review; review the update again.");
        // BackupService coordinates the game lease internally. Acquire our mutation lease only after the backup completes.
        var safety = await new BackupService(_paths).CreateAsync(instance.Id, "all", false, cancellationToken).ConfigureAwait(false);
        if (safety.Consistent != true)
            throw new InvalidOperationException("Stop the game before applying a pack update.");
        using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var contentLease = await ResourceLease.AcquireAsync(resource: ContentLock(instance), cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureStopped(instance);
        if (!JsonNode.DeepEquals(ReadLock(instance), review.PreviousLock))
            throw new InvalidOperationException("Pack ownership changed after review; review this archive again.");
        var oldRecords = Records(review.PreviousLock["files"] as JsonArray ?? []);
        var incoming = review.Incoming;
        var incomingRecords = Records(incoming.Files);
        var nextRecords = new Dictionary<string, PackFileRecord>(oldRecords, StringComparer.OrdinalIgnoreCase);
        var changedPaths = new List<string>();
        var contentTouched = new List<string>();
        var conflicts = new List<PackFileChange>();
        var differences = new List<PackFileDifference>();
        var journal = new List<PackJournalEntry>();
        review.SafetyBackupId = safety.Id;
        review.Phase = "applying";
        review.Journal = journal;
        var previousDirectory = Path.Combine(operation, "previous");
        Directory.CreateDirectory(previousDirectory);
        await SnapshotMetadataAsync(instance, previousDirectory, cancellationToken).ConfigureAwait(false);
        await WriteReviewAsync(review, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var action in review.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = action.Path;
                var target = PackFiles.Safe(instance.GameDirectory, relative);
                var current = await PackFiles.Sha1Async(target, cancellationToken).ConfigureAwait(false);
                var intended = action.Action;
                if (current != action.CurrentSha1)
                    intended = "conflict";
                if (intended is "add" or "replace" or "delete")
                {
                    var source = PackFiles.Safe(Path.Combine(operation, "incoming"), relative);
                    var newHash = action.IncomingSha1;
                    if (intended != "delete" && await PackFiles.Sha1Async(source, cancellationToken).ConfigureAwait(false) != newHash)
                        throw new InvalidDataException($"Staged incoming file changed since review: {relative}");
                    if (current is not null)
                        await PackFiles.PublishAsync(target, PackFiles.Safe(previousDirectory, relative), cancellationToken).ConfigureAwait(false);
                    var entry = new PackJournalEntry(relative, current, newHash);
                    journal.Add(entry);
                    await WriteReviewAsync(review, cancellationToken).ConfigureAwait(false);
                    if (intended == "delete")
                    {
                        File.Delete(target);
                        nextRecords.Remove(relative);
                    }
                    else
                    {
                        await PackFiles.PublishAsync(source, target, cancellationToken).ConfigureAwait(false);
                        nextRecords[relative] = incomingRecords[relative];
                    }

                    entry.State = "committed";
                    changedPaths.Add(relative);
                    contentTouched.Add(relative);
                    await WriteReviewAsync(review, CancellationToken.None).ConfigureAwait(false);
                }
                else if (intended == "unchanged" && incomingRecords.TryGetValue(relative, out var next))
                {
                    nextRecords[relative] = next;
                    contentTouched.Add(relative);
                }
                else if (intended == "unchanged")
                {
                    nextRecords.Remove(relative);
                    contentTouched.Add(relative);
                }
                else if (intended == "conflict")
                {
                    var report = action with { CurrentSha1 = current };
                    var source = PackFiles.Safe(Path.Combine(operation, "incoming"), relative);
                    if (File.Exists(source))
                    {
                        var side = PackFiles.Safe(Path.Combine(operation, "conflicts"), relative);
                        await PackFiles.PublishAsync(source, side, cancellationToken).ConfigureAwait(false);
                        report = report with { IncomingFile = side };
                    }

                    conflicts.Add(report);
                }

                var actual = intended is "add" or "replace" ? action.IncomingSha1 : intended == "delete" ? null : current;
                var expected = action.IncomingSha1;
                if (actual != expected && !relative.StartsWith("saves/", StringComparison.OrdinalIgnoreCase))
                    differences.Add(new(relative, intended, expected, actual));
            }

            var consistency = new PackConsistency(incoming.Complete, incoming.Complete && differences.Count == 0, differences);
            var newLock = incoming with { Files = [.. nextRecords.Values], OperationId = operationId, Consistency = consistency };
            await WritePackAsync(instance, newLock, cancellationToken).ConfigureAwait(false);
            await WriteContentAsync(instance, newLock.Files, contentTouched, cancellationToken).ConfigureAwait(false);
            await UpdateVersionAsync(instance, incoming.Dependencies.GetValueOrDefault("minecraft")?.GetValue<string>(), cancellationToken).ConfigureAwait(false);
            review.Phase = "applied";
            review.Conflicts = conflicts;
            review.ChangedPaths = changedPaths;
            review.Complete = incoming.Complete;
            review.Consistency = consistency;
            await WriteReviewAsync(review, CancellationToken.None).ConfigureAwait(false);
            return review;
        }
        catch
        {
            await RollbackCoreAsync(instance, review, operation, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Restores a pack transaction's previous files and metadata while retaining edits made after its application.</summary>
    public async Task<PackReview> RollbackAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var operation = OperationDirectory(operationId);
        using var operationLease = await ResourceLease.AcquireAsync(resource: Path.Combine(operation, "review.json"), cancellationToken: cancellationToken).ConfigureAwait(false);
        var review = JsonFiles.ReadObject(Path.Combine(operation, "review.json")).Deserialize(PackJsonContext.Default.PackReview)! with { ReportPath = Path.Combine(operation, "review.json") };
        if (review.Phase is not ("applied" or "applying"))
            throw new InvalidOperationException("This pack update has no applied transaction to roll back.");
        var instance = new InstanceStore(_paths).Get(review.InstanceId);
        using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var contentLease = await ResourceLease.AcquireAsync(resource: ContentLock(instance), cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureStopped(instance);
        if (review.Phase == "applied" && ReadLock(instance)["operationId"]?.ToString() != operationId)
            throw new InvalidOperationException("A later pack operation owns this client. Roll back the current operation first; older metadata must not replace a newer release.");
        return await RollbackCoreAsync(instance, review, operation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists this client's persisted pack transactions and independently reports unreadable records.</summary>
    public PackOperationList GetOperations(string instanceId)
    {
        var instance = new InstanceStore(_paths).Get(instanceId);
        var current = ReadLock(instance)["operationId"]?.ToString();
        var directory = Path.Combine(_paths.Root, "packs", "updates");
        var operations = new List<PackOperationSummary>();
        var warnings = new List<PackOperationWarning>();
        foreach (var path in Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : [])
        {
            var file = Path.Combine(path, "review.json");
            if (!File.Exists(file)) continue;
            try
            {
                var review = JsonFiles.ReadObject(file);
                if (review["instanceId"]?.ToString() != instanceId) continue;
                operations.Add(new(review["operationId"]?.GetValue<string>(), review["phase"]?.GetValue<string>(),
                    review["createdAt"]?.GetValue<string>(), review["incoming"]?["versionId"]?.DeepClone(),
                    review["operationId"]?.ToString() == current, file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                warnings.Add(new(Path.GetFileName(path), exception.Message));
            }
        }

        return new(operations, warnings);
    }

    /// <summary>Stops source-driven pack checks while retaining content, ownership baselines and original provenance for explicit manual updates.</summary>
    public async Task<PackState> DetachSourceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var instance = new InstanceStore(_paths).Get(instanceId);
        using var lease = await ResourceLease.AcquireAsync(resource: ContentLock(instance), cancellationToken: cancellationToken).ConfigureAwait(false);
        var pack = ReadLock(instance);
        if (pack["manifest"] is null)
            throw new InvalidOperationException("This client has no retained pack source.");
        pack["sourceDetached"] = true;
        await JsonFiles.WriteAsync(PackLock(instance), pack, cancellationToken).ConfigureAwait(false);
        return Result(instance, pack.Deserialize(PackJsonContext.Default.PackContentState)!);
    }

    private static async Task<PackReview> RollbackCoreAsync(InstanceRecord instance, PackReview review, string operation, CancellationToken token)
    {
        var conflicts = new List<PackRollbackConflict>();
        var previous = Path.Combine(operation, "previous");
        foreach (var entry in (review.Journal ?? []).AsEnumerable().Reverse())
        {
            var relative = entry.Path;
            var target = PackFiles.Safe(instance.GameDirectory, relative);
            var current = await PackFiles.Sha1Async(target, token).ConfigureAwait(false);
            var before = entry.BeforeSha1;
            var after = entry.AfterSha1;
            if (current == before)
                continue;
            if (current != after)
            {
                conflicts.Add(new(relative, "File changed after update; rollback retains the player's edit.", PackFiles.Safe(previous, relative)));
                continue;
            }

            if (before is null)
                File.Delete(target);
            else
                await PackFiles.PublishAsync(PackFiles.Safe(previous, relative), target, token).ConfigureAwait(false);
        }

        await RestoreMetadataAsync(instance, previous, review.Actions, token).ConfigureAwait(false);
        await RestoreVersionAsync(instance, previous, token).ConfigureAwait(false);
        review.Phase = "rolledBack";
        review.AppliedConsistency = review.Consistency;
        review.Consistency = null;
        review.RollbackConflicts = conflicts;
        await WriteReviewAsync(review, CancellationToken.None).ConfigureAwait(false);
        return review;
    }

    private static PackState Result(InstanceRecord instance, PackContentState prepared) => new()
    {
        Instance = instance.Describe(),
        Pack = prepared,
        Complete = prepared.Complete,
        Missing = prepared.Missing,
        Dependencies = prepared.Dependencies,
        Consistency = prepared.Consistency,
        Recovery = prepared.Complete ? null : new("pack complete " + instance.Id)
    };
    private string OperationDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Pack operation ID must be a generated identifier.");
        var directory = Path.Combine(_paths.Root, "packs", "updates", id);
        PackFiles.NoLinks(directory);
        return directory;
    }

    private static string PackLock(InstanceRecord instance) => PackFiles.Safe(instance.GameDirectory, ".rodel/pack-lock.json", true);
    private static string ContentLock(InstanceRecord instance) => PackFiles.Safe(instance.GameDirectory, ".rodel/content-lock.json", true);
    private static JsonObject ReadLock(InstanceRecord instance) => File.Exists(PackLock(instance)) ? JsonFiles.ReadObject(PackLock(instance)) : new JsonObject
    {
        ["schemaVersion"] = 1,
        ["files"] = new JsonArray()
    };
    private static Dictionary<string, PackFileRecord> Records(JsonArray files)
    {
        var result = new Dictionary<string, PackFileRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in files)
        {
            var value = item!.Deserialize(PackJsonContext.Default.PackFileRecord)!;
            result.Add(value.Path, value);
        }

        return result;
    }

    private static Dictionary<string, PackFileRecord> Records(IEnumerable<PackFileRecord> files) =>
        files.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);

    private static JsonArray Array(IEnumerable<PackFileRecord> objects)
    {
        var array = new JsonArray();
        foreach (var item in objects)
            array.Add(JsonSerializer.SerializeToNode(item, PackJsonContext.Default.PackFileRecord));
        return array;
    }

    private static Task WritePackAsync(InstanceRecord instance, PackContentState pack, CancellationToken token) =>
        JsonFiles.WriteAsync(PackLock(instance), JsonSerializer.SerializeToNode(pack, PackJsonContext.Default.PackContentState)!, token);

    private static Task WriteReviewAsync(PackReview review, CancellationToken token) =>
        JsonFiles.WriteAsync(review.ReportPath, JsonSerializer.SerializeToNode(review, PackJsonContext.Default.PackReview)!, token);

    private static async Task WriteContentAsync(InstanceRecord instance, IReadOnlyList<PackFileRecord> owned, IReadOnlyList<string>? changed, CancellationToken token)
    {
        var file = ContentLock(instance);
        var document = File.Exists(file) ? JsonFiles.ReadObject(file) : new JsonObject
        {
            ["schemaVersion"] = 1,
            ["files"] = new JsonArray()
        };
        var records = document["files"] as JsonArray ?? [];
        var paths = new HashSet<string>(changed ?? [], StringComparer.OrdinalIgnoreCase);
        if (changed is null)
        {
            foreach (var value in owned)
                paths.Add(value.Path);
        }

        var updated = new JsonArray();
        foreach (var value in records)
        {
            if (!paths.Contains(value?["path"]?.GetValue<string>() ?? string.Empty))
                updated.Add(value?.DeepClone());
        }

        foreach (var value in owned)
        {
            if (paths.Contains(value.Path) && PackFiles.Kind(value.Path) is not null)
                updated.Add(JsonSerializer.SerializeToNode(value, PackJsonContext.Default.PackFileRecord));
        }

        document["files"] = updated;
        await JsonFiles.WriteAsync(file, document, token).ConfigureAwait(false);
    }

    private static async Task SnapshotMetadataAsync(InstanceRecord instance, string previous, CancellationToken token)
    {
        foreach (var relative in new[]
        {
            ".rodel/pack-lock.json",
            ".rodel/content-lock.json"
        }

        )
        {
            var source = PackFiles.Safe(instance.GameDirectory, relative, true);
            if (File.Exists(source))
                await PackFiles.PublishAsync(source, PackFiles.Safe(previous, relative, true), token).ConfigureAwait(false);
        }

        await JsonFiles.WriteAsync(PackFiles.Safe(previous, ".rodel/transaction-metadata.json", true), new JsonObject { ["pack"] = File.Exists(PackLock(instance)), ["content"] = File.Exists(ContentLock(instance)) }, token).ConfigureAwait(false);
        await PackFiles.PublishAsync(instance.MetadataPath, PackFiles.Safe(previous, ".rodel/instance-before.json", true), token).ConfigureAwait(false);
    }

    private static async Task UpdateVersionAsync(InstanceRecord instance, string? minecraftVersion, CancellationToken token)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: instance.MetadataPath, cancellationToken: token).ConfigureAwait(false);
        var metadata = JsonFiles.ReadObject(instance.MetadataPath);
        if (minecraftVersion is not null)
            metadata["versionId"] = minecraftVersion;
        (metadata["settings"] as JsonObject)?.Remove("versionId");
        (metadata["settings"] as JsonObject)?.Remove("loader");
        await JsonFiles.WriteAsync(instance.MetadataPath, metadata, token).ConfigureAwait(false);
    }

    private static async Task RestoreVersionAsync(InstanceRecord instance, string previous, CancellationToken token)
    {
        var original = JsonFiles.ReadObject(PackFiles.Safe(previous, ".rodel/instance-before.json", true));
        using var lease = await ResourceLease.AcquireAsync(resource: instance.MetadataPath, cancellationToken: token).ConfigureAwait(false);
        var metadata = JsonFiles.ReadObject(instance.MetadataPath);
        if (original.ContainsKey("versionId"))
            metadata["versionId"] = original["versionId"]?.DeepClone();
        else
            metadata.Remove("versionId");
        var oldSettings = original["settings"] as JsonObject;
        var settings = metadata["settings"] as JsonObject;
        foreach (var key in new[]
        {
            "versionId",
            "loader"
        }

        )
        {
            if (oldSettings?.ContainsKey(key) == true)
            {
                if (settings is null)
                {
                    settings = [];
                    metadata["settings"] = settings;
                }

                settings[key] = oldSettings[key]?.DeepClone();
            }
            else
            {
                settings?.Remove(key);
            }
        }

        await JsonFiles.WriteAsync(instance.MetadataPath, metadata, token).ConfigureAwait(false);
    }

    private static async Task RestoreMetadataAsync(InstanceRecord instance, string previous, IReadOnlyList<PackFileChange> actions, CancellationToken token)
    {
        var presence = JsonFiles.ReadObject(PackFiles.Safe(previous, ".rodel/transaction-metadata.json", true));
        foreach (var pair in new[]
        {
            ("pack", ".rodel/pack-lock.json"),
            ("content", ".rodel/content-lock.json")
        }

        )
        {
            var target = PackFiles.Safe(instance.GameDirectory, pair.Item2, true);
            if (pair.Item1 == "content")
            {
                var restored = presence["content"]!.GetValue<bool>() ? JsonFiles.ReadObject(PackFiles.Safe(previous, pair.Item2, true)) : new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["files"] = new JsonArray()
                };
                var current = File.Exists(target) ? JsonFiles.ReadObject(target) : restored;
                var affected = new HashSet<string>(actions.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
                var files = new JsonArray();
                foreach (var item in current["files"] as JsonArray ?? [])
                {
                    if (!affected.Contains(item?["path"]?.GetValue<string>() ?? string.Empty))
                        files.Add(item?.DeepClone());
                }

                foreach (var item in restored["files"] as JsonArray ?? [])
                {
                    if (affected.Contains(item?["path"]?.GetValue<string>() ?? string.Empty))
                    {
                        var record = (JsonObject)item!.DeepClone();
                        record["sha1"] = await PackFiles.Sha1Async(PackFiles.Safe(instance.GameDirectory, record["path"]!.GetValue<string>()), token).ConfigureAwait(false);
                        files.Add((JsonNode)record);
                    }
                }

                current["files"] = files;
                await JsonFiles.WriteAsync(target, current, token).ConfigureAwait(false);
            }
            else if (presence[pair.Item1]!.GetValue<bool>())
            {
                await PackFiles.PublishAsync(PackFiles.Safe(previous, pair.Item2, true), target, token).ConfigureAwait(false);
            }
            else
            {
                File.Delete(target);
            }
        }
    }

    private void EnsureStopped(InstanceRecord instance)
    {
        if (new Rodel.MinecraftLauncher.Core.Launch.SessionService(_paths).HasActiveGame(instance.GameDirectory))
            throw new InvalidOperationException("Stop the game before changing a pack.");
    }
}