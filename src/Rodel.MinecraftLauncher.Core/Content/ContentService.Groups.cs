// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentService
{
    internal static async Task<string> LockFingerprintAsync(string game, CancellationToken ct) => File.Exists(LockPath(game)) ? await HashFileAsync(LockPath(game), ct).ConfigureAwait(false) : "missing";
    private static async Task<string> HashFileAsync(string file, CancellationToken ct)
    {
        var input = File.OpenRead(file);
        await using var inputDisposal = input.ConfigureAwait(false);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
    }

    /// <summary>Changes only the selected package's pin or update-channel policy, retaining its source metadata.</summary>
    public async Task<ContentEntry> SetPolicyAsync(string game, string kind, string fileName, bool? pinned = null, string? channel = null, string? world = null, CancellationToken ct = default)
    {
        if (channel is not null and not ("release" or "beta" or "alpha"))
            throw new ArgumentException("Channel must be release, beta or alpha.");
        var root = ContentRoot(game, kind, world);
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        var file = SafeChild(root, fileName);
        if (!File.Exists(file) && !Directory.Exists(file))
        {
            var disabled = FindDisabledDirectory(game, root, kind, fileName, await GetLockAsync(game, ct).ConfigureAwait(false));
            if (disabled is not null)
                file = ResolveStoredPath(game, disabled["path"]!.GetValue<string>());
        }

        if (!File.Exists(file) && !Directory.Exists(file))
            throw new FileNotFoundException("Content is missing.", file);
        var entry = await DescribeAsync(game, kind, file, ct).ConfigureAwait(false);
        var existing = (await GetLockAsync(game, ct).ConfigureAwait(false))["files"]?.AsArray().OfType<JsonObject>().FirstOrDefault(x => x["path"]?.GetValue<string>() == entry.Path);
        if (existing?["enabled"] is { } enabled)
            entry.Enabled = enabled.GetValue<bool>();
        if (pinned is not null)
        {
            entry.Pinned = pinned.Value;
            entry.TrackingFields.Add("pinned");
        }

        if (channel is not null)
        {
            entry.Channel = channel;
            entry.TrackingFields.Add("channel");
        }

        await StoreEntryAsync(game, entry, existing?["path"]?.GetValue<string>(), ct).ConfigureAwait(false);
        return entry;
    }

    internal async Task<ContentEntry> AttachProvenanceAsync(string game, string kind, string fileName, string? world, ContentProvenance provenance, string expectedSha1, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        var file = SafeChild(ContentRoot(game, kind, world), fileName);
        var entry = await DescribeAsync(game, kind, file, ct).ConfigureAwait(false);
        if (entry.Sha1 != expectedSha1)
            throw new IOException("Content changed while identifying its source; no provenance was attached.");
        entry.Platform = provenance.Platform;
        entry.ProjectId = provenance.ProjectId;
        entry.VersionId = provenance.VersionId;
        entry.DownloadUrl = provenance.DownloadUrl;
        entry.Upstream = provenance.Upstream.DeepClone();
        entry.TrackingFields.UnionWith(["platform", "projectId", "versionId", "downloadUrl", "upstream"]);
        if (provenance.GameVersion is not null)
        {
            entry.GameVersion = provenance.GameVersion;
            entry.TrackingFields.Add("gameVersion");
        }

        if (provenance.Loader is not null)
        {
            entry.Loader = provenance.Loader;
            entry.TrackingFields.Add("loader");
        }

        await StoreEntryAsync(game, entry, null, ct).ConfigureAwait(false);
        return entry;
    }

    // Candidates are already downloaded and verified. One lock owns all file and lock changes.
    internal async Task<ContentInstallReceipt> ApplyGroupAsync(string game, IReadOnlyList<ContentPlanNode> candidates, string expectedLock, string primaryProject, CancellationToken ct,
        IReadOnlyList<ContentEntry>? localReplacements = null, Func<CancellationToken, Task>? validateOnline = null)
    {
        game = Path.GetFullPath(game);
        using var lease = await ResourceLease.AcquireAsync(resource: LockPath(game), cancellationToken: ct).ConfigureAwait(false);
        await RecoverGroupsCoreAsync(game, ct).ConfigureAwait(false);
        if (validateOnline is not null)
            await validateOnline(ct).ConfigureAwait(false);
        else if (await LockFingerprintAsync(game, ct).ConfigureAwait(false) != expectedLock)
            throw new IOException("Content changed after the plan was prepared; review a new plan before retrying.");
        var replacementInventory = localReplacements is not null && candidates.Any(candidate => candidate.Platform == "local")
            ? await ListAsync(game, "mods", ct: ct).ConfigureAwait(false) : null;
        var original = await GetLockAsync(game, ct).ConfigureAwait(false);
        var next = (JsonObject)original.DeepClone();
        var files = next["files"] as JsonArray ?? [];
        if (next["files"] is null)
            next["files"] = files;
        var id = Guid.NewGuid().ToString("N");
        var operation = Path.Combine(game, ".rodel", "content-groups", id);
        Directory.CreateDirectory(operation);
        var journalPath = Path.Combine(operation, "journal.json");
        var changes = new JsonArray();
        var installed = new JsonArray();
        var installedEntries = new List<ContentEntry>();
        ContentEntry? primaryEntry = null;
        var selectionMappings = new List<ResourcePackMapping>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var record = new JsonObject
        {
            ["operationId"] = id,
            ["state"] = "preparing",
            ["lockExisted"] = File.Exists(LockPath(game)),
            ["oldLock"] = original.DeepClone(),
            ["changes"] = changes,
            ["installed"] = installed,
            ["selectionMappings"] = JsonSerializer.SerializeToNode<IReadOnlyList<ResourcePackMapping>>(selectionMappings, ContentJsonContext.Default.IReadOnlyListResourcePackMapping)
        };
        if (File.Exists(LockPath(game)))
            File.Copy(LockPath(game), Path.Combine(operation, "lock.before"));
        await JsonFiles.WriteAsync(journalPath, record, ct).ConfigureAwait(false);
        try
        {
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var kind = candidate.Kind;
                var world = candidate.World;
                var root = ContentRoot(game, kind, world);
                var project = candidate.ProjectId;
                var platform = candidate.Platform;
                if (!IsContentFileName(kind, candidate.FileName))
                    throw new LauncherOperationException("CONTENT_NOT_CONTENT", "The reviewed candidate is not a content package; existing files were retained.");
                var previous = files.OfType<JsonObject>().Where(x => x["projectId"]?.GetValue<string>() == project && x["platform"]?.GetValue<string>() == platform && x["kind"]?.GetValue<string>() == kind && Path.GetDirectoryName(ResolveStoredPath(game, x["path"]!.GetValue<string>())) == root
                    && x["directory"]?.GetValue<bool>() != true && IsContentFileName(kind, Path.GetFileName(x["path"]!.ToString()))).ToArray();
                var reused = candidate.ReuseLocal;
                if (candidate.LocalReplacement is { } identified)
                {
                    if (candidate.VerifyReplacementModIds)
                    {
                        var incoming = await InspectPackageAsync(candidate.Source!, ct).ConfigureAwait(false);
                        var declared = GetModIdentities(identified.Metadata);
                        if (declared.Count == 0 || !GetModIdentities(incoming.Metadata).IsSupersetOf(declared)
                            || (await ListAsync(game, kind, world, ct).ConfigureAwait(false))
                                .Any(entry => entry.Path != identified.Path && ModIdentitiesOverlap(incoming, entry)))
                        {
                            throw new IOException("The selected replacement no longer matches the reviewed mod identities; local files were retained.");
                        }
                    }

                    var identifiedPath = ResolveStoredPath(game, identified.Path);
                    if (platform != "modrinth" || identified.Kind != kind || identified.Directory || identified.Sha1 is null
                        || !string.Equals(Path.GetDirectoryName(identifiedPath), root, StringComparison.OrdinalIgnoreCase)
                        || !IsContentFileName(kind, identified.FileName)
                        || !await Downloads.DownloadService.ValidAsync(identifiedPath, identified.Sha1, identified.Size, ct).ConfigureAwait(false))
                    {
                        throw new IOException("The locally identified update target changed; its files were retained.");
                    }

                    var owner = files.OfType<JsonObject>().FirstOrDefault(entry => entry["path"]?.ToString() == identified.Path);
                    if (owner?["projectId"]?.ToString() is { Length: > 0 } ownerProject
                        && (ownerProject != project || owner["platform"]?.ToString() != platform))
                    {
                        throw new IOException("The locally identified file now belongs to another source; its files were retained.");
                    }

                    if (identified.Tracking is { } tracking) MergeTrackingFields(identified, tracking);
                    if (!previous.Any(entry => entry["path"]?.ToString() == identified.Path))
                    {
                        var snapshot = identified.ToRecord();
                        snapshot.Remove("tracking");
                        previous = [.. previous, snapshot];
                    }
                }

                if (localReplacements is not null && platform is "builtin" or "local")
                {
                    var declared = GetModIdentities(candidate.Upstream);
                    var replacements = localReplacements.Where(entry => platform == "builtin"
                        ? GetModIdentity(entry.Metadata) == project : declared.Overlaps(GetModIdentities(entry.Metadata))).ToArray();
                    if (platform == "local")
                    {
                        var current = replacementInventory!.Where(entry => declared.Overlaps(GetModIdentities(entry.Metadata))).ToArray();
                        if (current.Length != replacements.Length || current.Any(entry => !replacements.Any(old => old.Path == entry.Path && old.Sha1 == entry.Sha1)))
                            throw new LauncherOperationException("CONTENT_REVIEW_STALE", "The reviewed mod identities changed; prepare a new review before replacing files.");
                    }

                    foreach (var local in replacements)
                    {
                        var localPath = ResolveStoredPath(game, local.Path);
                        if (!string.Equals(Path.GetDirectoryName(localPath), root, StringComparison.OrdinalIgnoreCase) || local.Directory)
                            throw new IOException("A reviewed mod replacement must be a file in the instance mods directory.");
                        if (platform == "local" && local.Tracking is { } tracking) MergeTrackingFields(local, tracking);
                        if (!previous.Any(entry => entry["path"]?.ToString() == local.Path))
                        {
                            var snapshot = local.ToRecord();
                            if (platform == "local") snapshot.Remove("tracking");
                            previous = [.. previous, snapshot];
                        }
                    }
                }

                if (reused is not null)
                {
                    var reusePath = ResolveStoredPath(game, reused.Path);
                    if (!string.Equals(Path.GetDirectoryName(reusePath), root, StringComparison.OrdinalIgnoreCase)
                        || !await Downloads.DownloadService.ValidAsync(reusePath, candidate.Sha1, candidate.Size, ct).ConfigureAwait(false))
                    {
                        throw new IOException($"Reviewed local content changed; original files were retained: {reusePath}");
                    }

                    var owner = files.OfType<JsonObject>().FirstOrDefault(item => item["path"]?.ToString() == reused.Path);
                    if (owner?["projectId"]?.ToString() is { } ownerProject && (ownerProject != project || owner["platform"]?.ToString() != platform))
                        throw new IOException($"Reviewed local content belongs to another source: {reusePath}");
                    if (!previous.Any(item => item["path"]?.ToString() == reused.Path))
                        previous = [.. previous, owner ?? await CreateTrackingRecordAsync(game, kind, reusePath, ct).ConfigureAwait(false)];
                }

                var enabled = candidate.DesiredEnabled ?? previous.FirstOrDefault()?["enabled"]?.GetValue<bool>() ?? true;
                var baseName = reused?.FileName ?? candidate.FileName;
                if (reused is not null && baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) baseName = baseName[..^9];
                var name = baseName + (enabled ? string.Empty : ".disabled");
                ValidateName(name);
                var target = SafeChild(root, name);
                var oldPaths = previous.Select(x => ResolveStoredPath(game, x["path"]!.GetValue<string>())).ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var suffix = 1; reserved.Contains(target) || ((File.Exists(target) || Directory.Exists(target)) && !oldPaths.Contains(target)); suffix++)
                    target = SafeChild(root, Path.GetFileNameWithoutExtension(name) + "-" + suffix + Path.GetExtension(name));
                reserved.Add(target);
                foreach (var old in previous)
                {
                    var oldPath = ResolveStoredPath(game, old["path"]!.GetValue<string>());
                    selectionMappings.AddRange(ResourceSelectionMappings(kind, oldPath, enabled ? target : null));
                    if (File.Exists(oldPath))
                    {
                        if (!await Downloads.DownloadService.ValidAsync(oldPath, old["sha1"]?.GetValue<string>(), old["size"]?.GetValue<long>(), ct).ConfigureAwait(false))
                            throw new IOException($"Locally modified content was retained: {oldPath}");
                        await AddChangeAsync(oldPath, null).ConfigureAwait(false);
                    }

                    RemoveEntry(next, old["path"]!.GetValue<string>());
                }

                record["selectionMappings"] = JsonSerializer.SerializeToNode<IReadOnlyList<ResourcePackMapping>>(selectionMappings, ContentJsonContext.Default.IReadOnlyListResourcePackMapping);
                var stage = StagingPath(target);
                await AddChangeAsync(target, stage).ConfigureAwait(false);
                await JsonFiles.WriteAsync(journalPath, record, ct).ConfigureAwait(false);
                try
                {
                    await CopyFileAsync(candidate.Source!, stage, ct).ConfigureAwait(false);
                    if (!await Downloads.DownloadService.ValidAsync(stage, candidate.Sha1, candidate.Size, ct).ConfigureAwait(false))
                        throw new IOException("The content source changed while copying; existing files were retained.");
                }
                catch
                {
                    if (File.Exists(stage))
                        File.Delete(stage);
                    throw;
                }

                var entry = await DescribeAsync(game, kind, stage, ct, Path.GetFileName(target)).ConfigureAwait(false);
                if (previous.FirstOrDefault() is { } oldEntry)
                    MergeTrackingFields(entry, oldEntry);
                ApplyCandidateTracking(entry, candidate);
                entry.Path = StoredPath(game, target);
                entry.FileName = Path.GetFileName(target);
                entry.Enabled = enabled;
                if (kind == "resourcepacks")
                {
                    entry.SelectionAliases = [.. (entry.SelectionAliases ?? []).Concat(previous.Select(x => x["fileName"]!.ToString()))
                        .Append(Path.GetFileName(target)).Distinct(StringComparer.Ordinal)];
                    entry.TrackingFields.Add("selectionAliases");
                }

                changes.OfType<JsonObject>().First(x => x["target"]!.GetValue<string>() == target)["newHash"] = await HashFileAsync(stage, ct).ConfigureAwait(false);
                files.Add((JsonNode)entry.ToRecord());
                installed.Add((JsonNode)entry.ToRecord());
                installedEntries.Add(entry);
                if (project == primaryProject) primaryEntry ??= entry;
                await JsonFiles.WriteAsync(journalPath, record, ct).ConfigureAwait(false);
            }

            record["newLock"] = next;
            record["state"] = "committing";
            await JsonFiles.WriteAsync(journalPath, record, ct).ConfigureAwait(false);
            foreach (var change in changes.OfType<JsonObject>())
            {
                ct.ThrowIfCancellationRequested();
                var target = change["target"]!.GetValue<string>();
                var oldHash = change["oldHash"]?.GetValue<string>();
                if ((File.Exists(target) ? await HashFileAsync(target, ct).ConfigureAwait(false) : null) != oldHash)
                    throw new IOException($"Content changed during commit; retained it: {target}");
                if (change["stage"]?.GetValue<string>() is { } stage)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(stage, TransferPath(target), true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }

                change["applied"] = true;
                await JsonFiles.WriteAsync(journalPath, record, ct).ConfigureAwait(false);
            }

            await JsonFiles.WriteAsync(LockPath(game), next, ct).ConfigureAwait(false);
            record["state"] = "complete";
            await JsonFiles.WriteAsync(journalPath, record, CancellationToken.None).ConfigureAwait(false);
            var selection = await CoordinateResourceSelectionAsync(game, journalPath, record, CancellationToken.None, selectionMappings).ConfigureAwait(false);
            return new ContentInstallReceipt
            {
                OperationId = id,
                Journal = journalPath,
                Installed = installedEntries,
                ResourcePackSelection = selection,
                Warning = selection?.Warning,
                Primary = primaryEntry ?? throw new InvalidDataException("The committed content group has no primary project.")
            };
        }
        catch
        {
            await RestoreGroupAsync(game, journalPath, record, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        static void ApplyCandidateTracking(ContentEntry entry, ContentPlanNode candidate)
        {
            if (candidate.Platform == "local")
            {
                // A manually chosen replacement is not the old provider release. Preserve player policy
                // and unknown tracking, while clearing source claims tied to the replaced bytes.
                var fields = new[] { "platform", "projectId", "versionId", "downloadUrl", "gameVersion", "loader", "sha512",
                    "dependency", "dependencies", "upstream", "title", "iconUrl", "provides", "requestedBy" };
                MergeTrackingFields(entry, new JsonObject(fields.Select(key => new KeyValuePair<string, JsonNode?>(key, null))), overwrite: true);
                return;
            }

            entry.Platform = candidate.Platform;
            entry.ProjectId = candidate.ProjectId;
            entry.VersionId = candidate.VersionId;
            entry.DownloadUrl = candidate.DownloadUrl;
            entry.GameVersion = candidate.GameVersion;
            entry.Loader = candidate.Loader;
            entry.Sha512 = candidate.Sha512;
            entry.Dependency = candidate.Dependency;
            entry.Dependencies = candidate.Dependencies?.DeepClone();
            entry.Upstream = candidate.Upstream.DeepClone();
            entry.TrackingFields.UnionWith(["platform", "projectId", "versionId", "downloadUrl", "gameVersion", "loader", "sha512", "dependency", "dependencies", "upstream"]);
            if (candidate.PresentationRead)
            {
                entry.Title = candidate.Title;
                entry.IconUrl = candidate.IconUrl;
                entry.TrackingFields.UnionWith(["title", "iconUrl"]);
            }

            if (candidate.Provides is not null)
            {
                entry.Provides = candidate.Provides;
                entry.TrackingFields.Add("provides");
            }

            if (candidate.RequestedBy is not null)
            {
                entry.RequestedBy = candidate.RequestedBy;
                entry.TrackingFields.Add("requestedBy");
            }

            foreach (var (key, value) in candidate.AdditionalData ?? [])
                (entry.AdditionalData ??= [])[key] = value.Clone();
        }

        async Task AddChangeAsync(string target, string? stage)
        {
            var existingChange = changes.OfType<JsonObject>().FirstOrDefault(x => x["target"]!.GetValue<string>() == target);
            if (existingChange is not null)
            {
                existingChange["stage"] = stage;
                existingChange["newHash"] = stage is null || !File.Exists(stage) ? null : await HashFileAsync(stage, ct).ConfigureAwait(false);
                return;
            }

            var backup = Path.Combine(operation, changes.Count + ".before");
            var exists = File.Exists(target);
            if (exists)
                await CopyFileAsync(target, backup, ct).ConfigureAwait(false);
            changes.Add((JsonNode)new JsonObject { ["target"] = target, ["stage"] = stage, ["backup"] = exists ? backup : null, ["oldHash"] = exists ? await HashFileAsync(backup, ct).ConfigureAwait(false) : null, ["newHash"] = stage is null || !File.Exists(stage) ? null : await HashFileAsync(stage, ct).ConfigureAwait(false) });
        }
    }

    private async Task<JsonArray> RecoverGroupsCoreAsync(string game, CancellationToken ct)
    {
        var results = new JsonArray();
        var folder = Path.Combine(game, ".rodel", "content-groups");
        if (!Directory.Exists(folder))
            return results;
        foreach (var journal in Directory.EnumerateFiles(folder, "journal.json", SearchOption.AllDirectories))
        {
            var record = JsonFiles.ReadObject(journal);
            if (record["state"]?.GetValue<string>() == "complete")
            {
                var selection = await CoordinateResourceSelectionAsync(game, journal, record, ct).ConfigureAwait(false);
                if (selection is not null)
                    results.Add(JsonSerializer.SerializeToNode(selection, ContentJsonContext.Default.GameSettingsChangeResult));
                continue;
            }

            if (record["state"]?.GetValue<string>() == "rolledBack")
                continue;
            await RestoreGroupAsync(game, journal, record, ct).ConfigureAwait(false);
            results.Add(record.DeepClone());
        }

        return results;
    }

    private static async Task RestoreGroupAsync(string game, string journal, JsonObject record, CancellationToken ct)
    {
        if (record["state"]?.GetValue<string>() != "preparing")
        {
            if (File.Exists(LockPath(game)))
            {
                var current = JsonFiles.ReadObject(LockPath(game));
                if (!JsonNode.DeepEquals(current, record["oldLock"]) && !JsonNode.DeepEquals(current, record["newLock"]))
                    throw new IOException($"Recovery retained externally changed content metadata. Journal: {journal}");
            }
        }

        foreach (var change in (record["changes"] as JsonArray ?? []).OfType<JsonObject>().Reverse())
        {
            ct.ThrowIfCancellationRequested();
            var target = change["target"]!.GetValue<string>();
            if (!Path.IsPathRooted(target) || Path.GetFileName(target).StartsWith(".rodel", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid recovery target: {target}");
            var operation = Path.GetDirectoryName(journal)!;
            if (change["backup"]?.GetValue<string>() is { } backupPath && !Path.GetFullPath(backupPath).StartsWith(Path.GetFullPath(operation) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovery backup escapes its operation directory.");
            if (change["stage"]?.GetValue<string>() is { } stagePath && !string.Equals(Path.GetDirectoryName(stagePath), Path.Combine(Path.GetDirectoryName(PhysicalPath(Path.GetDirectoryName(target)!)) ?? PhysicalPath(Path.GetDirectoryName(target)!), ".rodel-content-staging"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovery staging path does not belong to its target volume.");
            // Preparation never changes active files or the lock. External edits during
            // download/copy are therefore retained without requiring manual rollback.
            if (record["state"]?.GetValue<string>() == "preparing")
            {
                if (change["stage"]?.GetValue<string>() is { } abandoned && File.Exists(abandoned))
                    File.Delete(abandoned);
                continue;
            }

            var oldHash = change["oldHash"]?.GetValue<string>();
            var newHash = change["newHash"]?.GetValue<string>();
            var actual = File.Exists(target) ? await HashFileAsync(target, ct).ConfigureAwait(false) : null;
            if (actual != oldHash)
            {
                if (actual != newHash)
                    throw new IOException($"Recovery retained a file changed outside the operation: {target}. Journal: {journal}");
                if (change["backup"]?.GetValue<string>() is { } backup)
                {
                    if (!File.Exists(backup) || await HashFileAsync(backup, ct).ConfigureAwait(false) != oldHash)
                        throw new IOException($"Recovery backup is missing or changed: {backup}");
                    var stage = StagingPath(target);
                    await CopyFileAsync(backup, stage, ct).ConfigureAwait(false);
                    File.Move(stage, TransferPath(target), true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }

            if (change["stage"]?.GetValue<string>() is { } pending && File.Exists(pending))
                File.Delete(pending);
        }

        if (record["state"]?.GetValue<string>() != "preparing")
        {
            var original = Path.Combine(Path.GetDirectoryName(journal)!, "lock.before");
            if (record["lockExisted"]?.GetValue<bool>() == true)
            {
                var temporary = LockPath(game) + "." + Guid.NewGuid().ToString("N") + ".restore";
                await CopyFileAsync(original, temporary, ct).ConfigureAwait(false);
                if (File.Exists(LockPath(game)))
                    File.Replace(temporary, LockPath(game), null);
                else
                    File.Move(temporary, LockPath(game));
            }
            else if (File.Exists(LockPath(game)))
            {
                File.Delete(LockPath(game));
            }
        }

        record["state"] = "rolledBack";
        await JsonFiles.WriteAsync(journal, record, ct).ConfigureAwait(false);
    }
}