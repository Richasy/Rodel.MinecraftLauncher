// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Backups;

/// <summary>Creates, inspects and restores verified instance archives while retaining rollback data.</summary>
public sealed class BackupService(LauncherPaths paths)
{
    /// <summary>Lists backup manifests and individual archive read failures without validating or hiding unfamiliar records.</summary>
    public IReadOnlyList<BackupManifest> List(string? instanceId = null)
    {
        var result = new List<BackupManifest>();
        if (!Directory.Exists(paths.BackupsDirectory))
            return result;
        foreach (var file in Directory.EnumerateFiles(paths.BackupsDirectory, "*.zip"))
        {
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var manifest = ReadManifest(zip);
                if (instanceId is null || manifest["instanceId"]?.GetValue<string>() == instanceId)
                    result.Add(BackupManifest.Read(manifest));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                result.Add(new() { Id = Path.GetFileNameWithoutExtension(file), Error = e.Message });
            }
        }

        return result;
    }

    /// <summary>Validates archive paths, lengths and digests before returning the complete extensible manifest.</summary>
    public BackupManifest Inspect(string backupId)
    {
        using var archive = ZipFile.OpenRead(BackupPath(backupId));
        return BackupManifest.Read(Validate(archive, CancellationToken.None));
    }

    /// <summary>Creates a backup under the instance lease, preserving optional scope and redownloadable-content exclusions.</summary>
    public async Task<BackupManifest> CreateAsync(string instanceId, string scope = "all", bool excludeRedownloadable = true, CancellationToken cancellationToken = default)
    {
        var instances = new InstanceStore(paths);
        using var instanceLease = await ResourceLease.AcquireAsync(resource: instances.Get(instanceId).MetadataPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var instance = instances.Get(instanceId);
        using var lease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await CreateCoreAsync(instance, scope, excludeRedownloadable, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates and overlays a backup onto an instance, retaining a safety archive and the original directory for rollback.</summary>
    public async Task<BackupRestoreResult> RestoreAsync(string backupId, string? instanceId = null, CancellationToken cancellationToken = default)
    {
        // Resolve the target before locking the archive: instance removal holds the metadata lease while cleaning archives.
        var targetId = instanceId;
        if (targetId is null)
        {
            using var source = ZipFile.OpenRead(BackupPath(backupId));
            targetId = ReadManifest(source)["instanceId"]!.GetValue<string>();
        }

        var instances = new InstanceStore(paths);
        using var instanceLease = await ResourceLease.AcquireAsync(resource: instances.Get(targetId).MetadataPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var archiveLease = await ResourceLease.AcquireAsync(resource: BackupPath(backupId), cancellationToken: cancellationToken).ConfigureAwait(false);
        using var archive = ZipFile.OpenRead(BackupPath(backupId));
        var manifest = Validate(archive, cancellationToken);
        if (instanceId is null && manifest["instanceId"]!.GetValue<string>() != targetId)
            throw new InvalidDataException("The backup target changed while waiting for the instance lease. Retry the restore.");
        var instance = instances.Get(targetId);
        var game = Path.TrimEndingDirectorySeparator(ResourceLease.CanonicalPath(instance.GameDirectory));
        using var lease = await ResourceLease.AcquireAsync(resource: game, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (IsRunning(game))
            throw new InvalidOperationException("Stop the running game before restoring its files.");
        RejectLinks(game);
        if (Directory.GetParent(game) is not { } parent)
            throw new IOException("Cannot replace a filesystem root.");
        var operation = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent.FullName, ".rodel-restore-" + operation);
        var previous = Path.Combine(parent.FullName, ".rodel-before-restore-" + operation);
        var recoveryFile = Path.Combine(paths.BackupsDirectory, operation + ".restore.json");
        var safety = await CreateCoreAsync(instance, "all", false, cancellationToken).ConfigureAwait(false);
        var recovery = new JsonObject
        {
            ["operationId"] = operation,
            ["backupId"] = backupId,
            ["safetyBackupId"] = safety.Id,
            ["gameDirectory"] = game,
            ["stagingDirectory"] = staging,
            ["rollbackDirectory"] = previous,
            ["phase"] = "preparing"
        };
        var materializedLinks = new List<BackupMaterializedLink>();
        try
        {
            Directory.CreateDirectory(staging);
            await CopyTreeAsync(game, staging, materializedLinks, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
            foreach (var file in manifest["files"]!.AsArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = file!["path"]!.GetValue<string>();
                var target = SafeTarget(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var input = archive.GetEntry("files/" + relative)!.Open();
                await using var inputDisposal = input.ConfigureAwait(false);
                var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                await using var outputDisposal = output.ConfigureAwait(false);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (IsRunning(game))
                throw new InvalidOperationException("A game started while restore was being prepared; stop it and retry.");
            recovery["phase"] = "ready";
            await JsonFiles.WriteAsync(recoveryFile, recovery, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(game, previous);
            try
            {
                Directory.Move(staging, game);
            }
            catch
            {
                Directory.Move(previous, game);
                throw;
            }

            recovery["phase"] = "committed";
            await JsonFiles.WriteAsync(recoveryFile, recovery, CancellationToken.None).ConfigureAwait(false);
            return new(operation, backupId, instance.Id, safety.Id!, previous, recoveryFile, "overlay",
                manifest["files"]!.AsArray().Count, materializedLinks, materializedLinks.Count > 0
                    ? "Linked game content was copied into independent directories/files during restore. Shared targets are unchanged; rollback restores the original links."
                    : null);
        }
        catch
        {
            // Cancellation never removes the original. A committed switch retains the previous directory.
            if (Directory.Exists(staging))
                DeleteTree(staging);
            throw;
        }
    }

    /// <summary>Deletes one generated backup archive under its resource lease.</summary>
    public async Task DeleteAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var file = BackupPath(backupId);
        using var lease = await ResourceLease.AcquireAsync(resource: file, cancellationToken: cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(file);
    }

    // Called under the instance metadata lease, which also excludes backup creation and restore into this instance.
    // Use the actual archive path and manifest ownership, never a filename inferred from an instance or manifest ID.
    internal async Task DeleteForInstanceAsync(string instanceId, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.BackupsDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(paths.BackupsDirectory, "*.zip"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = await ResourceLease.AcquireAsync(resource: file, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!File.Exists(file)) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            bool owned;
            try
            {
                using var archive = ZipFile.OpenRead(file);
                owned = ReadManifest(archive)["instanceId"] is JsonValue owner
                    && owner.TryGetValue<string>(out var id) && id == instanceId;
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                // An unreadable archive has no trustworthy owner. Preserve it rather than guess whom it belongs to.
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (owned) File.Delete(file);
        }
    }

    /// <summary>Restores the retained directory from a committed restore operation without discarding the displaced directory.</summary>
    public async Task<BackupRestoreState> RollbackAsync(string operationId, CancellationToken cancellationToken = default)
    {
        _ = BackupPath(operationId);
        var recoveryFile = Path.Combine(paths.BackupsDirectory, operationId + ".restore.json");
        var state = JsonFiles.ReadObject(recoveryFile);
        var game = Path.GetFullPath(state["gameDirectory"]!.GetValue<string>());
        var previous = Path.GetFullPath(state["rollbackDirectory"]!.GetValue<string>());
        var parent = Directory.GetParent(game)?.FullName ?? throw new InvalidDataException("Invalid recovery directory.");
        if (!string.Equals(previous, Path.Combine(parent, ".rodel-before-restore-" + operationId), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery directory does not belong to this operation.");
        using var lease = await ResourceLease.AcquireAsync(resource: game, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (IsRunning(game))
            throw new InvalidOperationException("Stop the running game before rolling back its files.");
        RejectLinks(game);
        RejectLinks(previous);
        if (!Directory.Exists(previous))
            throw new DirectoryNotFoundException("No pre-restore directory remains for this operation.");
        var replaced = Path.Combine(parent, ".rodel-after-restore-" + operationId);
        if (Directory.Exists(replaced))
            throw new IOException("This operation has already been rolled back.");
        cancellationToken.ThrowIfCancellationRequested();
        var hadCurrent = Directory.Exists(game);
        if (hadCurrent)
            Directory.Move(game, replaced);
        try
        {
            Directory.Move(previous, game);
        }
        catch
        {
            if (hadCurrent)
                Directory.Move(replaced, game);
            throw;
        }

        state["phase"] = "rolledBack";
        state["replacedDirectory"] = replaced;
        await JsonFiles.WriteAsync(recoveryFile, state, CancellationToken.None).ConfigureAwait(false);
        return BackupRestoreState.Read(state);
    }

    private async Task<BackupManifest> CreateCoreAsync(InstanceRecord instance, string scope, bool exclude, CancellationToken token)
    {
        if (scope is not ("all" or "worlds" or "config"))
            throw new ArgumentException("Backup scope must be all, worlds, or config.");
        var expandedLinks = new List<BackupExpandedLink>();
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(paths.BackupsDirectory);
        var file = BackupPath(id);
        var temporary = file + ".tmp";
        var files = new List<BackupFile>();
        var consistent = !IsRunning(instance.GameDirectory);
        var manifest = new BackupManifest
        {
            Format = 1,
            Id = id,
            InstanceId = instance.Id,
            Instance = instance.ToJson(),
            Scope = scope,
            ExcludeRedownloadable = exclude,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Consistent = consistent,
            RestoreMode = "overlay",
            Files = files,
            ExpandedLinks = expandedLinks
        };
        if (!consistent)
            manifest.Warning = "Game is running; files may represent different moments and this is not a consistent world snapshot.";
        try
        {
            {
                var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true);
                await using (stream.ConfigureAwait(false))
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                    foreach (var source in EnumerateFiles(instance.GameDirectory, instance.GameDirectory, ResourceLease.CanonicalPath(paths.BackupsDirectory), scope, exclude, expandedLinks, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                    {
                        token.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(instance.GameDirectory, source).Replace('\\', '/');
                        var entry = archive.CreateEntry("files/" + relative, CompressionLevel.Fastest);
                        var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, true);
                        await using var inputDisposal = input.ConfigureAwait(false);
                        var output = entry.Open();
                        await using var outputDisposal = output.ConfigureAwait(false);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var buffer = new byte[65536];
                        long length = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                        {
                            hash.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                            length += read;
                        }

                        files.Add(new() { Path = relative, Length = length, Sha256 = Convert.ToHexString(hash.GetHashAndReset()) });
                    }

                    if (IsRunning(instance.GameDirectory))
                    {
                        manifest.Consistent = false;
                        manifest.Warning = "Game is running; this is not a consistent world snapshot.";
                    }

                    var manifestEntry = archive.CreateEntry("manifest.json");
                    var writer = manifestEntry.Open();
                    await using var writerDisposal = writer.ConfigureAwait(false);
                    var text = new StreamWriter(writer);
                    await using var textDisposal = text.ConfigureAwait(false);
                    await text.WriteAsync(JsonSerializer.Serialize(manifest, BackupJsonContext.Default.BackupManifest).AsMemory(), token).ConfigureAwait(false);
                }
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, file);
            return manifest;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private string BackupPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Backup ID must be a generated identifier.");
        var root = ResourceLease.CanonicalPath(paths.BackupsDirectory);
        var file = Path.Combine(root, id + ".zip");
        if (!string.Equals(ResourceLease.CanonicalPath(file), file, StringComparison.OrdinalIgnoreCase))
            throw new IOException("An individual backup archive cannot redirect to another file.");
        return file;
    }

    private static JsonObject ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup has no manifest.");
        using var stream = entry.Open();
        return JsonNode.Parse(stream) as JsonObject ?? throw new InvalidDataException("Backup manifest must be an object.");
    }

    private static JsonObject Validate(ZipArchive zip, CancellationToken token)
    {
        var manifest = ReadManifest(zip);
        if (manifest["format"]?.GetValue<int>() != 1 || manifest["files"] is not JsonArray files)
            throw new InvalidDataException("Unsupported or incomplete backup manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "manifest.json"
        };
        foreach (var item in files)
        {
            token.ThrowIfCancellationRequested();
            var path = item?["path"]?.GetValue<string>() ?? throw new InvalidDataException("Missing backup file path.");
            _ = SafeTarget(Path.GetTempPath(), path);
            var name = "files/" + path;
            if (!seen.Add(name))
                throw new InvalidDataException("Duplicate backup path.");
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Missing backup entry: {path}");
            if (entry.Length != item!["length"]!.GetValue<long>())
                throw new InvalidDataException($"Length mismatch: {path}");
            using var stream = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            int read;
            while ((read = stream.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }

            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), item["sha256"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA256 mismatch: {path}");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!names.Add(entry.FullName) || !seen.Contains(entry.FullName))
                throw new InvalidDataException($"Unexpected or duplicate archive entry: {entry.FullName}");
        }

        return manifest;
    }

    private static string SafeTarget(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') || relative.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException($"Unsafe backup path: {relative}");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup path escapes destination.");
        return target;
    }

    private bool IsRunning(string game) => new Rodel.MinecraftLauncher.Core.Launch.SessionService(paths).HasActiveGame(game);
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Backup operations do not follow linked paths: {current}");
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string directory, string skippedRoot, string scope, bool exclude, List<BackupExpandedLink> expandedLinks, HashSet<string> ancestors)
    {
        var physical = ResourceLease.CanonicalPath(directory);
        if (!ancestors.Add(physical))
            throw new IOException($"Linked directory cycle prevents a complete backup: {directory} -> {physical}");
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                // Excluded shared caches must be skipped before inspecting or resolving their links.
                if (!Included(relative, scope, exclude))
                    continue;
                var target = ResourceLease.CanonicalPath(entry);
                if (string.Equals(target, skippedRoot, StringComparison.OrdinalIgnoreCase) || target.StartsWith(skippedRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    expandedLinks.Add(new() { Path = relative, Target = target });
                if (Directory.Exists(entry))
                {
                    foreach (var child in EnumerateFiles(root, entry, skippedRoot, scope, exclude, expandedLinks, ancestors))
                        yield return child;
                }
                else
                {
                    yield return entry;
                }
            }
        }
        finally
        {
            ancestors.Remove(physical);
        }
    }

    private static bool Included(string relative, string scope, bool exclude)
    {
        var first = relative.Split('/')[0];
        if (relative.Equals(".rodel-instance-owner", StringComparison.OrdinalIgnoreCase))
            return false;
        if (scope == "worlds")
            return first.Equals("saves", StringComparison.OrdinalIgnoreCase);
        if (scope == "config")
        {
            return first.Equals("config", StringComparison.OrdinalIgnoreCase) || new[]
            {
                "options.txt",
                "optionsof.txt",
                "servers.dat"
            }.Contains(relative, StringComparer.OrdinalIgnoreCase);
        }

        return !exclude || !new[]
        {
            "libraries",
            "assets",
            "versions",
            "natives"
        }.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task CopyTreeAsync(string source, string target, List<BackupMaterializedLink> links, HashSet<string> ancestors, CancellationToken token)
    {
        var physical = ResourceLease.CanonicalPath(source);
        if (!ancestors.Add(physical))
            throw new IOException($"Linked directory cycle prevents restore staging: {source} -> {physical}");
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(target, Path.GetFileName(entry));
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    links.Add(new(entry, ResourceLease.CanonicalPath(entry)));
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destination);
                    await CopyTreeAsync(entry, destination, links, ancestors, token).ConfigureAwait(false);
                }
                else
                {
                    var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    await using var inputDisposal = input.ConfigureAwait(false);
                    var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    await using var outputDisposal = output.ConfigureAwait(false);
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ancestors.Remove(physical);
        }
    }

    private static void DeleteTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Directory.Exists(entry))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(entry);
                else
                    DeleteTree(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(directory);
    }
}