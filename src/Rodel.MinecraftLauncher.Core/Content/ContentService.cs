// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentService(LauncherPaths paths)
{
    public static bool IsModFileName(string fileName) => IsContentFileName("mods", fileName);

    internal static Task<ContentEntry> InspectPackageAsync(string path, CancellationToken ct)
        => DescribeAsync(Path.GetDirectoryName(path)!, "mods", path, ct);

    internal static async Task<JsonObject> ReadPackageDependenciesAsync(string path, ContentEntry entry, string id, CancellationToken ct)
    {
        if (entry.MetadataFormat is not ("META-INF/mods.toml" or "META-INF/neoforge.mods.toml"))
            return entry.Metadata?["depends"]?.DeepClone().AsObject() ?? [];
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry(entry.MetadataFormat)!.Open());
        var dependencies = ReadTomlDependencies(await reader.ReadToEndAsync(ct).ConfigureAwait(false)).GetValueOrDefault(id) ?? [];
        var result = new JsonObject();
        foreach (var dependency in dependencies.Where(dependency => dependency.Required)) result[dependency.Id.GetValue<string>()] = dependency.Range?.DeepClone();
        return result;
    }

    private static async Task<string> Sha1Async(string file, CancellationToken ct)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    public static string LockPath(string game) => Path.Combine(Path.GetFullPath(game), ".rodel", "content-lock.json");
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Retain the public service instance API for callers and future instance configuration.")]
    public Task<JsonObject> GetLockAsync(string game, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var file = LockPath(game);
        return Task.FromResult(File.Exists(file) ? JsonFiles.ReadObject(file) : new JsonObject { ["schemaVersion"] = 1, ["files"] = new JsonArray() });
    }

    /// <summary>Lists physical content and tracked disabled directories, retaining read warnings and tracking metadata; metadata-only display reads omit file hashes.</summary>
    public Task<IReadOnlyList<ContentEntry>> ListAsync(string game, string kind, string? world = null, CancellationToken ct = default) => ListAsync(game, kind, true, world, ct);

    /// <summary>Reads the same inventory with optional file hashing; display-only snapshots cannot authorize a subsequent change.</summary>
    public async Task<IReadOnlyList<ContentEntry>> ListAsync(string game, string kind, bool includeHashes, string? world = null, CancellationToken ct = default)
    {
        var root = ContentRoot(game, kind, world);
        var result = new List<ContentEntry>();
        var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
        var entries = Directory.Exists(root) ? Directory.EnumerateFileSystemEntries(root).Where(path => !Path.GetFileName(path).StartsWith(".rodel-", StringComparison.OrdinalIgnoreCase)).ToArray() : [];
        var descriptions = new ContentEntry[entries.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (index, token) => descriptions[index] = await DescribeForListAsync(game, kind, entries[index], token, includeHashes).ConfigureAwait(false)).ConfigureAwait(false);
        // Tracking and results remain sequential; preserve filesystem enumeration order.
        foreach (var entry in descriptions)
        {
            ct.ThrowIfCancellationRequested();
            var tracked = (contentLock["files"] as JsonArray)?.FirstOrDefault(item => item?["path"]?.GetValue<string>() == entry.Path);
            if (tracked is not null)
                entry.Tracking = tracked.DeepClone().AsObject();
            result.Add(entry);
        }

        foreach (var tracked in contentLock["files"] as JsonArray ?? [])
        {
            if (tracked is not JsonObject record || !IsDisabledDirectory(game, root, kind, record))
                continue;
            var path = ResolveStoredPath(game, record["path"]!.GetValue<string>());
            if (!Directory.Exists(path))
                continue;
            var entry = await DescribeForListAsync(game, kind, path, ct, includeHashes).ConfigureAwait(false);
            entry.FileName = record["fileName"]?.GetValue<string>() ?? Path.GetFileName(path);
            entry.Enabled = false;
            entry.DisabledDirectory = true;
            entry.ActiveDirectory = record["activeDirectory"]?.GetValue<string>();
            entry.ActiveFileName = record["activeFileName"]?.GetValue<string>();
            entry.Tracking = record.DeepClone().AsObject();
            if (!entry.ReadFailed)
            {
                if (includeHashes) entry.Sha1 ??= record["sha1"]?.GetValue<string>();
                entry.Size ??= record["size"]?.GetValue<long>();
                if (!entry.MetadataPresent && entry.Metadata is null)
                    entry.Metadata = record["metadata"]?.DeepClone();
                entry.MetadataFormat ??= record["metadataFormat"]?.GetValue<string>();
            }

            entry.ArchiveReadable ??= record["archiveReadable"]?.GetValue<bool>();
            entry.MetadataWarning ??= record["metadataWarning"]?.GetValue<string>();
            entry.ReadWarning ??= record["readWarning"]?.GetValue<string>();
            // A disabled directory previously exposed the whole extensible lock record.
            // Retain fields not replaced by this scan, including explicit nulls.
            MergeTrackingFields(entry, record);
            result.Add(entry);
        }

        return result;
    }

    /// <summary>Adds a local content file to an instance, retaining the requested kind, world scope, and source metadata.</summary>
    public async Task<ContentEntry> AddAsync(string game, string kind, string source, string? world = null, JsonObject? provenance = null, string? expectedSourceFingerprint = null, CancellationToken ct = default)
    {
        game = Path.GetFullPath(game);
        var root = ContentRoot(game, kind, world);
        source = Path.GetFullPath(source);
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("Content source is missing.", source);
        await InspectSourceAsync(kind, source, ct).ConfigureAwait(false);
        if (Directory.Exists(source))
        {
            var physicalSource = Path.TrimEndingDirectorySeparator(PhysicalPath(source));
            var physicalRoot = PhysicalPath(root);
            var stageRoot = Path.Combine(Path.GetDirectoryName(physicalRoot) ?? physicalRoot, ".rodel-content-staging");
            if (ContainsPath(physicalSource, physicalRoot) || ContainsPath(physicalSource, stageRoot))
                throw new IOException("A content directory cannot be imported into itself or contain its destination/staging directory.");
        }

        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        if (expectedSourceFingerprint is not null && expectedSourceFingerprint != await EntryDigestAsync(source, ct).ConfigureAwait(false))
            throw new LauncherOperationException("CONTENT_SOURCE_CHANGED", "The reviewed source changed; prepare a new review before importing.");
        Directory.CreateDirectory(root);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        ValidateName(name);
        var importingDisabledDirectory = Directory.Exists(source) && name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var targetRoot = importingDisabledDirectory ? DisabledRoot(game, root, kind) : root;
        var target = SafeChild(targetRoot, name);
        if (provenance is null && File.Exists(source) && File.Exists(target)
            && await HashFileAsync(source, ct).ConfigureAwait(false) == await HashFileAsync(target, ct).ConfigureAwait(false))
        {
            var reused = await DescribeAsync(game, kind, target, ct).ConfigureAwait(false);
            reused.Reused = true;
            return reused;
        }

        if ((File.Exists(target) || Directory.Exists(target)) && !(Directory.Exists(source) && Directory.Exists(target)))
            throw new LauncherOperationException("CONTENT_IMPORT_CONFLICT", "Different content already exists with this name. The original was retained; no duplicate was imported.");
        if (provenance is null && File.Exists(source))
        {
            var disabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var counterpart = SafeChild(root, disabled ? name[..^9] : name + ".disabled");
            if (File.Exists(counterpart))
            {
                if (await HashFileAsync(source, ct).ConfigureAwait(false) != await HashFileAsync(counterpart, ct).ConfigureAwait(false))
                    throw new LauncherOperationException("CONTENT_IMPORT_CONFLICT", "A differently enabled file with the same name has different contents. Choose which file to retain; no duplicate was imported.");
                var reused = await SetEnabledCoreAsync(game, kind, Path.GetFileName(counterpart), !disabled, world, ct).ConfigureAwait(false);
                reused.Reused = true;
                return reused;
            }
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            if (provenance is null && Directory.Exists(source) && Directory.Exists(target)
                && await EntryDigestAsync(source, ct).ConfigureAwait(false) == await EntryDigestAsync(target, ct).ConfigureAwait(false))
            {
                var reused = await DescribeAsync(game, kind, target, ct).ConfigureAwait(false);
                reused.Reused = true;
                return reused;
            }

            throw new LauncherOperationException("CONTENT_IMPORT_CONFLICT", "Different content already exists with this name. It was retained; resolve the conflict before importing.");
        }

        if (provenance is null && Directory.Exists(source)
            && FindDisabledDirectory(game, root, kind, name, await GetLockAsync(game, ct).ConfigureAwait(false)) is { } disabledDirectory)
        {
            var existingDirectory = ResolveStoredPath(game, disabledDirectory["path"]!.ToString());
            if (Directory.Exists(existingDirectory) && await EntryDigestAsync(source, ct).ConfigureAwait(false) == await EntryDigestAsync(existingDirectory, ct).ConfigureAwait(false))
            {
                var reused = await SetEnabledCoreAsync(game, kind, name, !importingDisabledDirectory, world, ct).ConfigureAwait(false);
                reused.Reused = true;
                return reused;
            }

            throw new LauncherOperationException("CONTENT_IMPORT_CONFLICT", "The disabled directory package has different contents. It was retained; no duplicate was imported.");
        }

        if (kind == "mods" && File.Exists(source) && await CheckModImportAsync(game, source, ct).ConfigureAwait(false) is { } existingMod)
            return existingMod;
        // Stage beside the final file so publishing is a same-volume rename, also for external worlds.
        var stage = StagingPath(target);
        var journal = Path.Combine(game, ".rodel", "content-jobs", Guid.NewGuid().ToString("N") + ".json");
        var record = new JsonObject { ["operation"] = "add", ["source"] = source, ["stage"] = stage, ["destination"] = target, ["state"] = "copying" };
        var published = false;
        await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
        try
        {
            var before = await EntryDigestAsync(source, ct).ConfigureAwait(false);
            if (expectedSourceFingerprint is not null && before != expectedSourceFingerprint)
                throw new LauncherOperationException("CONTENT_SOURCE_CHANGED", "The reviewed source changed before copying; source and destination were retained.");
            if (Directory.Exists(source))
                await CopyDirectoryAsync(source, stage, ct).ConfigureAwait(false);
            else
                await CopyFileAsync(source, stage, ct).ConfigureAwait(false);
            if (before != await EntryDigestAsync(stage, ct).ConfigureAwait(false)
                || before != await EntryDigestAsync(source, ct).ConfigureAwait(false))
            {
                throw new IOException("Content changed while being copied. The source was retained; retry after closing applications that modify it.");
            }

            var entry = await DescribeAsync(game, kind, stage, ct, Path.GetFileName(target)).ConfigureAwait(false);
            entry.Path = StoredPath(game, target);
            entry.Enabled = entry.IsContent && !target.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            if (importingDisabledDirectory)
            {
                entry.ActiveDirectory = StoredPath(game, root);
                entry.ActiveFileName = Path.GetFileName(target)[..^9];
            }

            if (provenance is not null)
            {
                MergeTrackingFields(entry, provenance, overwrite: true);
            }

            record["state"] = "publishing";
            record["entry"] = entry.ToRecord();
            await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(stage))
                Directory.Move(stage, TransferPath(target));
            else
                File.Move(stage, TransferPath(target));
            published = true;
            await StoreEntryAsync(game, entry, null, ct).ConfigureAwait(false);
            record["state"] = "complete";
            await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
            return entry;
        }
        catch (Exception error)
        {
            if (!published)
            {
                try
                {
                    DeleteImportStage(stage, target);
                    record["state"] = error is OperationCanceledException ? "canceled" : "failed";
                    record["error"] = error.Message;
                    await JsonFiles.WriteAsync(journal, record, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new AggregateException("Content import failed and staging cleanup could not complete. The source was retained.", error, cleanup);
                }
            }

            throw;
        }
    }

    /// <summary>Changes one package's enabled state while retaining its tracking and recoverable transfer evidence.</summary>
    public async Task<ContentEntry> SetEnabledAsync(string game, string kind, string fileName, bool enabled, string? world = null, CancellationToken ct = default)
    {
        game = Path.GetFullPath(game);
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        return await SetEnabledCoreAsync(game, kind, fileName, enabled, world, ct).ConfigureAwait(false);
    }

    internal async Task<ContentEntry> SetEnabledCheckedAsync(string game, string fileName, bool enabled, string expectedSha1, CancellationToken ct)
    {
        game = Path.GetFullPath(game);
        using var lease = await ResourceLease.AcquireAsync(LockPath(game), ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        var file = SafeChild(ContentRoot(game, "mods", null), fileName);
        if (!File.Exists(file) || !string.Equals(await Sha1Async(file, ct).ConfigureAwait(false), expectedSha1, StringComparison.OrdinalIgnoreCase))
            throw new LauncherOperationException("REPAIR_MOD_CHANGED", "The reviewed mod changed before dispatch; the current file was preserved.");
        return await SetEnabledCoreAsync(game, "mods", fileName, enabled, null, ct).ConfigureAwait(false);
    }

    private async Task<ContentEntry> SetEnabledCoreAsync(string game, string kind, string fileName, bool enabled, string? world, CancellationToken ct)
    {
        var root = ContentRoot(game, kind, world);
        var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
        var disabledRecord = FindDisabledDirectory(game, root, kind, fileName, contentLock);
        var source = enabled && disabledRecord is not null ? ResolveStoredPath(game, disabledRecord["path"]!.GetValue<string>()) : SafeChild(root, fileName);
        if (!File.Exists(source) && !Directory.Exists(source) && disabledRecord is not null)
            source = ResolveStoredPath(game, disabledRecord["path"]!.GetValue<string>());
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("Content is missing.", source);
        var directory = Directory.Exists(source);
        var sourceName = Path.GetFileName(source);
        var entry = await DescribeAsync(game, kind, source, ct).ConfigureAwait(false);
        if (!entry.IsContent)
            throw new LauncherOperationException("CONTENT_NOT_CONTENT", "This entry is configuration, backup or other supporting data, not a package to enable or disable.");
        if (kind == "mods" && enabled) await CheckModEnableAsync(game, entry, ct).ConfigureAwait(false);
        var disabled = sourceName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var activeName = disabledRecord?["activeFileName"]?.GetValue<string>() ?? (disabled ? sourceName[..^9] : sourceName);
        var target = directory ? SafeChild(enabled ? root : DisabledRoot(game, root, kind), enabled ? activeName : activeName + ".disabled") : SafeChild(root, enabled && disabled ? sourceName[..^9] : !enabled && !disabled ? sourceName + ".disabled" : sourceName);
        entry.Path = StoredPath(game, target);
        entry.FileName = Path.GetFileName(target);
        entry.Enabled = enabled;
        var oldTracked = (contentLock["files"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(x => x["path"]?.ToString() == StoredPath(game, source));
        if (kind == "resourcepacks")
        {
            entry.SelectionAliases = [.. (oldTracked?["selectionAliases"] as JsonArray ?? []).Select(item => item!.GetValue<string>())
                .Concat([sourceName, activeName, Path.GetFileName(target)]).Distinct(StringComparer.Ordinal)];
            entry.TrackingFields.Add("selectionAliases");
        }

        var selectionMappings = ResourceSelectionMappings(kind, source, enabled ? target : null);
        if (directory)
        {
            entry.ActiveDirectory = StoredPath(game, root);
            entry.ActiveFileName = activeName;
        }

        var journal = Path.Combine(game, ".rodel", "content-jobs", Guid.NewGuid().ToString("N") + ".json");
        if (source != target)
        {
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException($"Enable/disable would overwrite existing content: {target}");
            await JsonFiles.WriteAsync(journal, new JsonObject { ["operation"] = "enable", ["state"] = "publishing", ["source"] = source, ["destination"] = target, ["oldPath"] = StoredPath(game, source), ["entry"] = entry.ToRecord(), ["selectionMappings"] = JsonSerializer.SerializeToNode(selectionMappings, ContentJsonContext.Default.IReadOnlyListResourcePackMapping) }, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var recovery = await MoveEntryAsync(source, target, journal, ct).ConfigureAwait(false);
            if (recovery is not null)
                entry.TransferRecoveryPath = recovery;
        }

        await StoreEntryAsync(game, entry, StoredPath(game, source), ct).ConfigureAwait(false);
        if (source != target)
        {
            var record = JsonFiles.ReadObject(journal);
            record["state"] = "complete";
            await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
            var selection = await CoordinateResourceSelectionAsync(game, journal, record, CancellationToken.None, selectionMappings).ConfigureAwait(false);
            entry.ResourcePackSelection = selection;
            if (selection?.Warning is { } warning)
                entry.Warning = warning;
        }

        return entry;
    }

    /// <summary>Moves a selected content entry into recoverable storage and updates its tracking and resource selection.</summary>
    public Task<ContentRemovalResult> RemoveAsync(string game, string kind, string fileName, string? world = null, CancellationToken ct = default)
        => RemoveCoreAsync(game, kind, fileName, world, null, ct);

    internal Task<ContentRemovalResult> RemoveCheckedAsync(string game, string fileName, string expectedSha1, CancellationToken ct)
        => RemoveCoreAsync(game, "mods", fileName, null, expectedSha1, ct);

    private async Task<ContentRemovalResult> RemoveCoreAsync(string game, string kind, string fileName, string? world, string? expectedSha1, CancellationToken ct)
    {
        game = Path.GetFullPath(game);
        var root = ContentRoot(game, kind, world);
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        var source = SafeChild(root, fileName);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            var disabledRecord = FindDisabledDirectory(game, root, kind, fileName, await GetLockAsync(game, ct).ConfigureAwait(false));
            if (disabledRecord is not null)
                source = ResolveStoredPath(game, disabledRecord["path"]!.GetValue<string>());
        }

        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("Content is missing.", source);
        // Keep recycle on the content volume: user-selected world folders can live on another drive.
        if (expectedSha1 is not null && (!File.Exists(source) || !string.Equals(await Sha1Async(source, ct).ConfigureAwait(false), expectedSha1, StringComparison.OrdinalIgnoreCase)))
            throw new LauncherOperationException("REPAIR_ROLLBACK_CONFLICT", "The added dependency changed before rollback; its current bytes were preserved.");
        var recovery = ControlRoot(game, root, kind, "recycle");
        Directory.CreateDirectory(recovery);
        var target = SafeChild(recovery, Guid.NewGuid().ToString("N") + "-" + fileName);
        var journal = Path.Combine(game, ".rodel", "content-jobs", Guid.NewGuid().ToString("N") + ".json");
        var selectionMappings = ResourceSelectionMappings(kind, source, null);
        await JsonFiles.WriteAsync(journal, new JsonObject { ["operation"] = "remove", ["state"] = "publishing", ["source"] = source, ["destination"] = target, ["oldPath"] = StoredPath(game, source), ["selectionMappings"] = JsonSerializer.SerializeToNode(selectionMappings, ContentJsonContext.Default.IReadOnlyListResourcePackMapping) }, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var transferRecovery = await MoveEntryAsync(source, target, journal, ct).ConfigureAwait(false);
        var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
        RemoveEntry(contentLock, StoredPath(game, source));
        await JsonFiles.WriteAsync(LockPath(game), contentLock, ct).ConfigureAwait(false);
        var completed = JsonFiles.ReadObject(journal);
        completed["state"] = "complete";
        completed["recoveryPath"] = target;
        await JsonFiles.WriteAsync(journal, completed, ct).ConfigureAwait(false);
        var selectionResult = await CoordinateResourceSelectionAsync(game, journal, completed, CancellationToken.None, selectionMappings).ConfigureAwait(false);
        return new ContentRemovalResult
        {
            FileName = fileName,
            RecoveryPath = target,
            TransferRecoveryPath = transferRecovery,
            ResourcePackSelection = selectionResult,
            Warning = selectionResult?.Warning
        };
    }

    public async Task<JsonArray> RecoverAsync(string game, CancellationToken ct = default)
    {
        game = Path.GetFullPath(game);
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        var jobs = Path.Combine(game, ".rodel", "content-jobs");
        var recovered = await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        if (!Directory.Exists(jobs))
            return recovered;
        foreach (var journal in Directory.EnumerateFiles(jobs, "*.json"))
        {
            var record = JsonFiles.ReadObject(journal);
            if (record["state"]?.ToString() == "complete")
            {
                var selection = await CoordinateResourceSelectionAsync(game, journal, record, ct).ConfigureAwait(false);
                if (selection is not null)
                    recovered.Add(JsonSerializer.SerializeToNode(selection, ContentJsonContext.Default.GameSettingsChangeResult));
                continue;
            }

            if (record["state"]?.GetValue<string>() != "publishing")
            {
                if (record["state"]?.ToString() == "copying" && record["operation"]?.ToString() == "add"
                    && record["stage"]?.ToString() is { } abandonedStage && record["destination"]?.ToString() is { } abandonedTarget)
                {
                    DeleteImportStage(abandonedStage, abandonedTarget);
                    record["state"] = "canceled";
                    record["recovered"] = true;
                    await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
                    recovered.Add(record.DeepClone());
                }

                continue;
            }

            var destination = record["destination"]?.GetValue<string>();
            if (destination is null || (!File.Exists(destination) && !Directory.Exists(destination)))
                continue;
            if (record["transfer"]?.GetValue<string>() == "copy-publish" && record["source"]?.GetValue<string>() is { } source && (Directory.Exists(source) || File.Exists(source)))
            {
                if (await EntryDigestAsync(source, ct).ConfigureAwait(false) != record["sourceDigest"]?.GetValue<string>())
                    continue;
                var backup = record["sourceBackup"]!.GetValue<string>();
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (Directory.Exists(source))
                    Directory.Move(TransferPath(source), backup);
                else
                    File.Move(TransferPath(source), backup);
            }

            if (record["operation"]?.GetValue<string>() == "remove")
            {
                var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
                RemoveEntry(contentLock, record["oldPath"]!.GetValue<string>());
                await JsonFiles.WriteAsync(LockPath(game), contentLock, ct).ConfigureAwait(false);
            }
            else if (record["entry"] is JsonObject entry)
            {
                await StoreEntryAsync(game, entry, record["oldPath"]?.GetValue<string>(), ct).ConfigureAwait(false);
            }
            else
            {
                continue;
            }

            record["state"] = "complete";
            record["recovered"] = true;
            await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
            var selectionResult = await CoordinateResourceSelectionAsync(game, journal, record, ct).ConfigureAwait(false);
            if (selectionResult is not null)
                recovered.Add(JsonSerializer.SerializeToNode(selectionResult, ContentJsonContext.Default.GameSettingsChangeResult));
            recovered.Add((JsonNode)record.DeepClone());
        }

        return recovered;
    }

    internal static string DirectoryFor(string game, string kind, string? world) => ContentRoot(game, kind, world);
    private static string ControlRoot(string game, string activeRoot, string kind, string category)
    {
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(activeRoot).TrimEnd('\\', '/').ToUpperInvariant())))[..16];
        return Path.Combine(Path.GetFullPath(game), ".rodel", category, kind.ToLowerInvariant(), scope);
    }

    private static string DisabledRoot(string game, string root, string kind) => ControlRoot(game, root, kind, "disabled");
    private static string ResolveStoredPath(string game, string stored) => Path.GetFullPath(Path.IsPathRooted(stored) ? stored : Path.Combine(game, stored));
    private static bool IsDisabledDirectory(string game, string root, string kind, JsonObject entry)
    {
        if (entry["enabled"]?.GetValue<bool>() != false || entry["directory"]?.GetValue<bool>() != true || !string.Equals(entry["kind"]?.GetValue<string>(), kind, StringComparison.OrdinalIgnoreCase) || entry["activeDirectory"]?.GetValue<string>() is not { } active || entry["path"]?.GetValue<string>() is not { } path)
            return false;
        return string.Equals(ResolveStoredPath(game, active), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(ResolveStoredPath(game, path)), DisabledRoot(game, root, kind), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject? FindDisabledDirectory(string game, string root, string kind, string name, JsonObject contentLock) => (contentLock["files"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(entry => IsDisabledDirectory(game, root, kind, entry) && (entry["fileName"]?.GetValue<string>() == name || entry["activeFileName"]?.GetValue<string>() == name));
    private static string StagingPath(string target)
    {
        var destinationParent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(destinationParent);
        // Directory packs containing pack.mcmeta must not be staged inside the active scan directory either.
        var parent = Path.GetDirectoryName(PhysicalPath(destinationParent)) ?? PhysicalPath(destinationParent);
        return Path.Combine(parent, ".rodel-content-staging", Guid.NewGuid().ToString("N") + ".partial");
    }

    private static bool ContainsPath(string root, string path) =>
        string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void DeleteImportStage(string stage, string target)
    {
        var physicalParent = PhysicalPath(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var expected = Path.Combine(Path.GetDirectoryName(physicalParent) ?? physicalParent, ".rodel-content-staging");
        var name = Path.GetFileName(stage);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(stage)), expected, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".partial", StringComparison.Ordinal) || !Guid.TryParseExact(name[..^8], "N", out _))
        {
            throw new InvalidDataException("Content import recovery rejected an unowned staging path.");
        }

        if (!File.Exists(stage) && !Directory.Exists(stage)) return;
        if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Content import recovery retained a redirected staging entry.");
        if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        else File.Delete(stage);
    }

    private static string TransferPath(string path) => Path.Combine(PhysicalPath(Path.GetDirectoryName(path)!), Path.GetFileName(path));
    private static string PhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var component in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                current = info.ResolveLinkTarget(true)?.FullName ?? current;
            }
        }

        return Path.GetFullPath(current);
    }

    private static async Task<string?> MoveEntryAsync(string source, string target, string journal, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            if (Directory.Exists(source))
                Directory.Move(TransferPath(source), TransferPath(target));
            else
                File.Move(TransferPath(source), TransferPath(target));
            return null;
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) == 17 || !string.Equals(Path.GetPathRoot(TransferPath(source)), Path.GetPathRoot(TransferPath(target)), StringComparison.OrdinalIgnoreCase))
        {
            var digest = await EntryDigestAsync(source, ct).ConfigureAwait(false);
            var stage = StagingPath(target);
            if (Directory.Exists(source))
                await CopyDirectoryAsync(source, stage, ct).ConfigureAwait(false);
            else
                await CopyFileAsync(source, stage, ct).ConfigureAwait(false);
            if (await EntryDigestAsync(stage, ct).ConfigureAwait(false) != digest || await EntryDigestAsync(source, ct).ConfigureAwait(false) != digest)
                throw new IOException("Content changed during the cross-volume copy; the original and staged copy were preserved.");
            var sourceParent = Path.GetDirectoryName(TransferPath(source))!;
            var backup = Path.Combine(Path.GetDirectoryName(sourceParent) ?? sourceParent, ".rodel-content-transfers", Guid.NewGuid().ToString("N"));
            var record = JsonFiles.ReadObject(journal);
            record["transfer"] = "copy-publish";
            record["stage"] = stage;
            record["sourceDigest"] = digest;
            record["sourceBackup"] = backup;
            if (record["entry"] is JsonObject entry)
                entry["transferRecoveryPath"] = backup;
            await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(stage))
                Directory.Move(stage, TransferPath(target));
            else
                File.Move(stage, TransferPath(target));
            // Keep the source as a recoverable copy on its original volume, outside the active pack scan root.
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (Directory.Exists(source))
                Directory.Move(TransferPath(source), backup);
            else
                File.Move(TransferPath(source), backup);
            return backup;
        }
    }

    private static async Task<string> EntryDigestAsync(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            var file = File.OpenRead(path);
            await using var fileDisposal = file.ConfigureAwait(false);
            return Convert.ToHexString(await SHA1.HashDataAsync(file, ct).ConfigureAwait(false));
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        async Task Walk(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Cannot snapshot a redirected entry for cross-volume transfer: {entry}");
                var relative = Path.GetRelativePath(path, entry).Replace('\\', '/');
                digest.AppendData(Encoding.UTF8.GetBytes((Directory.Exists(entry) ? "D:" : "F:") + relative + "\0"));
                if (Directory.Exists(entry))
                {
                    await Walk(entry).ConfigureAwait(false);
                }
                else
                {
                    var file = File.OpenRead(entry);
                    await using var fileDisposal = file.ConfigureAwait(false);
                    digest.AppendData(await SHA1.HashDataAsync(file, ct).ConfigureAwait(false));
                }
            }
        }

        await Walk(path).ConfigureAwait(false);
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private async Task<JsonObject> StoreEntryAsync(string game, JsonObject entry, string? oldPath, CancellationToken ct)
    {
        var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
        var files = contentLock["files"] as JsonArray ?? [];
        var path = entry["path"]!.GetValue<string>();
        var merged = files.FirstOrDefault(item => item?["path"]?.GetValue<string>() == (oldPath ?? path)) is not JsonObject existing ? [] : (JsonObject)existing.DeepClone();
        foreach (var (key, value) in entry)
            merged[key] = value?.DeepClone();
        RemoveEntry(contentLock, oldPath ?? path);
        if (contentLock["files"] is null)
            contentLock["files"] = files;
        files.Add((JsonNode)merged);
        await JsonFiles.WriteAsync(LockPath(game), contentLock, ct).ConfigureAwait(false);
        return (JsonObject)merged.DeepClone();
    }

    private async Task StoreEntryAsync(string game, ContentEntry entry, string? oldPath, CancellationToken ct)
    {
        var contentLock = await GetLockAsync(game, ct).ConfigureAwait(false);
        var files = contentLock["files"] as JsonArray ?? [];
        var previous = files.OfType<JsonObject>().FirstOrDefault(item => item["path"]?.GetValue<string>() == (oldPath ?? entry.Path));
        if (previous is not null)
            MergeTrackingFields(entry, previous);
        RemoveEntry(contentLock, oldPath ?? entry.Path);
        if (contentLock["files"] is null)
            contentLock["files"] = files;
        files.Add((JsonNode)entry.ToRecord());
        await JsonFiles.WriteAsync(LockPath(game), contentLock, ct).ConfigureAwait(false);
    }

    // Tracking is an extensible persisted document. Keep unknown fields at this boundary;
    // source inspection and operation state remain the authoritative typed values.
    private static void MergeTrackingFields(ContentEntry entry, JsonObject record, bool overwrite = false)
    {
        var present = entry.ToRecord();
        foreach (var (key, value) in record)
        {
            if ((!overwrite && present.ContainsKey(key)) || key is "path" or "fileName" or "kind" or "enabled" or "sha1" or "isMod" or "isContent" or "directory")
                continue;
            switch (key)
            {
                case "platform": entry.Platform = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "projectId": entry.ProjectId = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "versionId": entry.VersionId = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "downloadUrl": entry.DownloadUrl = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "gameVersion": entry.GameVersion = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "loader": entry.Loader = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "pinned": entry.Pinned = value?.GetValue<bool>(); entry.TrackingFields.Add(key); break;
                case "channel": entry.Channel = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "upstream": entry.Upstream = value?.DeepClone(); entry.TrackingFields.Add(key); break;
                case "dependencies": entry.Dependencies = value?.DeepClone(); entry.TrackingFields.Add(key); break;
                case "dependency": entry.Dependency = value?.GetValue<bool>(); entry.TrackingFields.Add(key); break;
                case "sha512": entry.Sha512 = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "title": entry.Title = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "iconUrl": entry.IconUrl = value?.GetValue<string>(); entry.TrackingFields.Add(key); break;
                case "selectionAliases": entry.SelectionAliases = value?.AsArray().Select(item => item!.GetValue<string>()).ToArray(); entry.TrackingFields.Add(key); break;
                case "provides": entry.Provides = value?.AsArray().Select(item => item!.GetValue<string>()).ToArray(); entry.TrackingFields.Add(key); break;
                case "requestedBy": entry.RequestedBy = value?.AsArray().Select(item => item!.GetValue<string>()).ToArray(); entry.TrackingFields.Add(key); break;
                case "size": entry.Size = value?.GetValue<long>(); break;
                case "formatRecognized": entry.FormatRecognized = value?.GetValue<bool>() == true; break;
                case "metadata": entry.Metadata = value?.DeepClone(); entry.MetadataPresent = true; break;
                case "metadataFormat": entry.MetadataFormat = value?.GetValue<string>(); break;
                case "metadataWarning": entry.MetadataWarning = value?.GetValue<string>(); break;
                case "readWarning": entry.ReadWarning = value?.GetValue<string>(); break;
                case "archiveReadable": entry.ArchiveReadable = value?.GetValue<bool>(); break;
                case "activeDirectory": entry.ActiveDirectory = value?.GetValue<string>(); break;
                case "activeFileName": entry.ActiveFileName = value?.GetValue<string>(); break;
                case "tracking": entry.Tracking = value?.DeepClone().AsObject(); break;
                case "reused": entry.Reused = value?.GetValue<bool>(); break;
                case "transferRecoveryPath": entry.TransferRecoveryPath = value?.GetValue<string>(); break;
                case "resourcePackSelection":
                    entry.ResourcePackSelection = value?.Deserialize(ContentJsonContext.Default.GameSettingsChangeResult);
                    break;
                case "warning": entry.Warning = value?.GetValue<string>(); break;
                default:
                    (entry.AdditionalData ??= [])[key] = JsonSerializer.SerializeToElement(value, ContentJsonContext.Default.JsonNode);
                    break;
            }
        }
    }

    private static void RemoveEntry(JsonObject contentLock, string path)
    {
        if (contentLock["files"] is not JsonArray files)
            return;
        for (var i = files.Count - 1; i >= 0; i--)
        {
            if (files[i]?["path"]?.GetValue<string>() == path)
                files.RemoveAt(i);
        }
    }

    internal static Task<ContentEntry> InspectListedModAsync(string game, string fileName, CancellationToken ct) =>
        DescribeForListAsync(game, "mods", SafeChild(ContentRoot(game, "mods", null), fileName), ct);

    private static async Task<ContentEntry> DescribeForListAsync(string game, string kind, string path, CancellationToken ct, bool includeHashes = true)
    {
        try
        {
            return await DescribeAsync(game, kind, path, ct, hashContent: includeHashes).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ct.ThrowIfCancellationRequested();
            var entry = CreateDescription(game, kind, path);
            entry.FormatRecognized = false;
            entry.ArchiveReadable = false;
            entry.ReadFailed = true;
            entry.ReadWarning = error.Message;
            entry.MetadataWarning = error.Message;
            return entry;
        }
    }

    private static ContentEntry CreateDescription(string game, string kind, string path, string? logicalName = null)
    {
        kind = kind.ToLowerInvariant();
        var folder = Directory.Exists(path);
        var name = logicalName ?? Path.GetFileName(path);
        var isContent = folder ? IsContentDirectory(kind, path) : IsContentFileName(kind, name);
        return new ContentEntry
        {
            Path = StoredPath(game, path),
            FileName = name,
            Kind = kind,
            Enabled = isContent && (folder || !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)),
            IsContent = isContent,
            FormatRecognized = folder && isContent,
            Directory = folder,
            IsMod = kind == "mods" ? isContent : null
        };
    }

    private static async Task<JsonObject> CreateTrackingRecordAsync(string game, string kind, string path, CancellationToken ct, string? logicalName = null) =>
        (await DescribeAsync(game, kind, path, ct, logicalName).ConfigureAwait(false)).ToRecord();

    private static async Task<ContentEntry> DescribeAsync(string game, string kind, string path, CancellationToken ct, string? logicalName = null, bool hashContent = true)
    {
        kind = kind.ToLowerInvariant();
        var result = CreateDescription(game, kind, path, logicalName);
        var folder = result.Directory;
        // Keep a normal handle: overlapped handles failed NativeAOT repeated ZIP scans; concurrency is bounded by ListAsync.
        var stream = (folder ? null : new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.SequentialScan }));
        await using var streamDisposal = (stream ?? Stream.Null).ConfigureAwait(false);
        if (!folder)
        {
            if (hashContent) result.Sha1 = Convert.ToHexString(await SHA1.HashDataAsync(stream!, ct).ConfigureAwait(false)).ToLowerInvariant();
            result.Size = stream!.Length;
            stream.Position = 0;
        }

        if (!result.IsContent) return result;
        try
        {
            if (folder)
            {
                if (File.Exists(Path.Combine(path, "pack.mcmeta")))
                    result.Metadata = JsonFiles.ReadObject(Path.Combine(path, "pack.mcmeta"));
            }
            else
            {
                using var zip = new ZipArchive(stream!, ZipArchiveMode.Read, leaveOpen: true);
                result.ArchiveReadable = true;
                var recognized = HasArchiveLayout(kind, zip);
                result.FormatRecognized = recognized;
                if (!recognized) result.MetadataWarning = "Expected package metadata or directory layout was not found; the existing archive remains manageable.";
                foreach (var metadataName in kind == "mods" ?
                [
                    "fabric.mod.json",
                    "quilt.mod.json",
                    "mcmod.info",
                    "litemod.json"
                ]
                : kind is "resourcepacks" or "datapacks" ? ["pack.mcmeta"] : Array.Empty<string>()
                )
                {
                    var entry = zip.GetEntry(metadataName);
                    if (entry is null)
                        continue;
                    if (entry.Length > 1024 * 1024)
                    {
                        result.MetadataWarning = "Metadata exceeds the index budget; original content remains usable.";
                        break;
                    }

                    var input = entry.Open();
                    await using var inputDisposal = input.ConfigureAwait(false);
                    result.Metadata = metadataName == "fabric.mod.json"
                        ? await FabricMetadataJson.ReadAsync(input, ct).ConfigureAwait(false)
                        : await JsonNode.ParseAsync(input, cancellationToken: ct).ConfigureAwait(false);
                    result.MetadataPresent = true;
                    result.MetadataFormat = metadataName;
                    break;
                }

                if (kind == "mods" && result.Metadata is null)
                {
                    foreach (var metadataName in new[]
                    {
                        "META-INF/neoforge.mods.toml",
                        "META-INF/mods.toml"
                    }

                    )
                    {
                        var entry = zip.GetEntry(metadataName);
                        if (entry is null)
                            continue;
                        if (entry.Length > 1024 * 1024)
                        {
                            result.MetadataWarning = "Metadata exceeds the index budget; original content remains usable.";
                            break;
                        }

                        using var reader = new StreamReader(entry.Open());
                        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                        var metadata = new JsonObject();
                        var declaredMods = new JsonArray();
                        foreach (System.Text.RegularExpressions.Match table in ModTablesRegex().Matches(text))
                        {
                            var declared = new JsonObject();
                            foreach (var field in new[] { "modId", "displayName", "version" })
                            {
                                if (ReadTomlField(table.Groups[1].Value, field) is { } value)
                                    declared[field] = value;
                            }

                            declaredMods.Add((JsonNode)declared);
                        }

                        metadata["mods"] = declaredMods;
                        foreach (var field in new[]
                        {
                            "modId",
                            "displayName",
                            "version",
                            "logoFile",
                            "modLoader",
                            "loaderVersion"
                        }

                        )
                        {
                            if (ReadTomlField(text, field) is { } value)
                                metadata[field] = value;
                        }

                        result.Metadata = metadata;
                        result.MetadataFormat = metadataName;
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            if (!folder && result.ArchiveReadable is null) result.ArchiveReadable = false;
            result.MetadataWarning = ex.Message;
        }

        return result;
    }

    private static string ContentRoot(string game, string kind, string? world)
    {
        game = Path.GetFullPath(game);
        var folder = kind.ToLowerInvariant() switch
        {
            "mods" => "mods",
            "resourcepacks" => "resourcepacks",
            "shaderpacks" => "shaderpacks",
            "datapacks" => "datapacks",
            _ => throw new ArgumentException($"Unknown content kind: {kind}")
        };
        if (folder != "datapacks")
            return SafeChild(game, folder);
        if (string.IsNullOrWhiteSpace(world))
            throw new ArgumentException("A world name or directory is required for datapacks.");
        var worldRoot = Path.IsPathRooted(world) ? Path.GetFullPath(world) : SafeChild(Path.Combine(game, "saves"), world);
        if (!Directory.Exists(worldRoot))
            throw new DirectoryNotFoundException($"Datapack world directory is missing: {worldRoot}");
        return SafeChild(worldRoot, "datapacks");
    }

    private static string StoredPath(string game, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(game), Path.GetFullPath(path));
        return relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) ? Path.GetFullPath(path) : relative.Replace('\\', '/');
    }

    private static string SafeChild(string root, string name)
    {
        ValidateName(name);
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Content path escapes destination: {name}");
        return path;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Invalid content file name: {name}");
    }

    private static async Task CopyFileAsync(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var input = File.OpenRead(source);
        await using var inputDisposal = input.ConfigureAwait(false);
        var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var outputDisposal = output.ConfigureAwait(false);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
        output.Flush(true);
    }

    private static async Task CopyDirectoryAsync(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Import a regular directory or file instead of redirected content: {path}");
            var child = SafeChild(target, Path.GetFileName(path));
            if (Directory.Exists(path))
                await CopyDirectoryAsync(path, child, ct).ConfigureAwait(false);
            else
                await CopyFileAsync(path, child, ct).ConfigureAwait(false);
        }
    }
}