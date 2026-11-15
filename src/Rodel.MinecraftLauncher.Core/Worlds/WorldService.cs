// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Worlds;

/// <summary>Identifies an explicit save-management operation.</summary>
public enum WorldAction
{
    /// <summary>Copies a save directory or extracts an archive into the instance.</summary>
    Import,
    /// <summary>Publishes a complete save archive.</summary>
    Export,
    /// <summary>Creates a retained backup of a save.</summary>
    Backup,
    /// <summary>Restores a backup after retaining the current save for recovery.</summary>
    Restore,
    /// <summary>Removes a save from the active list while retaining a recoverable copy.</summary>
    Remove,
    /// <summary>Restores a retained save without overwriting an occupied folder.</summary>
    Recover,
    /// <summary>Compresses a retained recovery directory after verifying its archive.</summary>
    Compress
}

/// <summary>Manages complete Java saves without changing their NBT or discarding unknown world files.</summary>
public sealed class WorldService(LauncherPaths paths, IProgress<LauncherProgress>? progress = null)
{
    private string Game(string id) => new InstanceStore(paths).Get(id).GameDirectory;
    private string History(string id) => Path.Combine(Game(id), ".rodel", "world-history");
    private string Backups(string id) => Path.Combine(paths.BackupsDirectory, "worlds", id);
    private static string Child(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.TrimEnd(' ', '.') != name || Path.GetFileName(name) != name)
        {
            throw new ArgumentException("Select a single valid world folder name.", nameof(name));
        }

        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The world path escapes its parent directory.", nameof(name));
        return path;
    }
    /// <summary>Resolves one existing save folder without accepting paths outside the instance's saves directory.</summary>
    public string GetDirectory(string instanceId, string world)
    {
        var directory = Child(Path.Combine(Game(instanceId), "saves"), world);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected world is missing.");
        return directory;
    }
    /// <summary>Lists usable save folders with best-effort NBT metadata and individual read warnings.</summary>
    public async Task<IReadOnlyList<WorldSummary>> ListAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var result = new List<WorldSummary>();
        var root = Path.Combine(Game(instanceId), "saves");
        if (!Directory.Exists(root)) return result;
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWorld(directory)) continue;
            var row = new WorldSummary { Folder = Path.GetFileName(directory), Name = Path.GetFileName(directory), Directory = directory };
            try
            {
                var metadata = row with { };
                await WorldMetadata.ReadAsync(Path.Combine(directory, "level.dat"), metadata, cancellationToken).ConfigureAwait(false);
                row = metadata with { Name = string.IsNullOrWhiteSpace(metadata.Name) ? row.Folder : metadata.Name };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException or ArgumentException)
            {
                row.Warning = error.Message;
            }

            var icon = Path.Combine(directory, "icon.png");
            if (File.Exists(icon)) row.Icon = icon;
            result.Add(row);
        }

        return result;
    }
    /// <summary>Lists matching completed backups, preserving unreadable-record diagnostics and unknown stored fields.</summary>
    public IReadOnlyList<WorldArchiveRecord> ListBackups(string instanceId, string? world = null) => Records(Backups(instanceId), instanceId, world, "complete");
    /// <summary>Lists recoverable saves, including interrupted operations whose retained directory still exists.</summary>
    public IReadOnlyList<WorldArchiveRecord> ListRecovery(string instanceId) => Records(History(instanceId), instanceId, null, "retained");
    private static List<WorldArchiveRecord> Records(string root, string instanceId, string? world, string state)
    {
        var result = new List<WorldArchiveRecord>();
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*.json").OrderDescending(StringComparer.Ordinal))
        {
            try
            {
                var record = JsonFiles.ReadObject(file);
                var interrupted = state == "retained" && record["state"]?.ToString() == "preparing"
                    && Guid.TryParseExact(record["id"]?.ToString(), "N", out _) && Directory.Exists(Path.Combine(root, record["id"]!.ToString()));
                if (interrupted) record["warning"] = "An interrupted operation retained this world for recovery.";
                if (record["instanceId"]?.ToString() == instanceId && (record["state"]?.ToString() == state || interrupted)
                    && (world is null || record["world"]?.ToString() == world))
                {
                    result.Add(WorldArchiveRecord.Read(record));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                result.Add(new() { Id = Path.GetFileNameWithoutExtension(file), Error = error.Message });
            }
        }

        return result;
    }
    /// <summary>Executes a save operation under the game lease, rejecting active games and preserving recovery data on failure.</summary>
    public async Task<WorldOperationResult> ExecuteAsync(string instanceId, WorldAction action, string? world = null, string? source = null,
        string? destination = null, CancellationToken cancellationToken = default)
    {
        var game = Game(instanceId);
        using var lease = await ResourceLease.AcquireAsync(resource: game, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (new SessionService(paths).HasActiveGame(game))
            throw new LauncherOperationException("WORLD_GAME_RUNNING", "Stop the game before changing or archiving its worlds.");
        var target = world is null ? null : Child(Path.Combine(game, "saves"), world);
        using var worldLock = target is not null && Directory.Exists(target) ? LockWorld(target) : null;
        return action switch
        {
            WorldAction.Import => await ImportAsync(instanceId, source ?? throw new ArgumentException("Select a world source."), world, cancellationToken).ConfigureAwait(false),
            WorldAction.Export => await ExportAsync(GetDirectory(instanceId, world!), destination ?? throw new ArgumentException("Select an output ZIP."), cancellationToken).ConfigureAwait(false),
            WorldAction.Backup => await BackupAsync(instanceId, world!, cancellationToken).ConfigureAwait(false),
            WorldAction.Restore => await RestoreAsync(instanceId, world!, source ?? throw new ArgumentException("Select a world backup."), cancellationToken).ConfigureAwait(false),
            WorldAction.Remove => await RemoveAsync(instanceId, world!, worldLock, cancellationToken).ConfigureAwait(false),
            WorldAction.Recover => await RecoverAsync(instanceId, source ?? throw new ArgumentException("Select a recovery record."), world, cancellationToken).ConfigureAwait(false),
            WorldAction.Compress => await CompressAsync(instanceId, source ?? throw new ArgumentException("Select a recovery record."), cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }
    private static FileStream? LockWorld(string directory)
    {
        var file = Path.Combine(directory, "session.lock");
        if (!File.Exists(file)) return null;
        FileStream? stream = null;
        try
        {
            stream = new(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            stream.Lock(0, 1);
            return stream;
        }
        catch (IOException error)
        {
            stream?.Dispose();
            throw new LauncherOperationException("WORLD_IN_USE", "The world is locked by Minecraft or another operation.", new JsonObject { ["cause"] = error.Message });
        }
    }
    private static bool IsWorld(string directory) => File.Exists(Path.Combine(directory, "level.dat"))
        || File.Exists(Path.Combine(directory, "level.dat_old")) || Directory.Exists(Path.Combine(directory, "region"));
    private async Task<WorldImportResult> ImportAsync(string id, string source, string? name, CancellationToken ct)
    {
        source = Path.GetFullPath(source);
        var game = Game(id);
        var stage = Path.Combine(game, ".rodel", "world-staging", Guid.NewGuid().ToString("N"));
        var folder = name ?? (Directory.Exists(source) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(source)) : Path.GetFileNameWithoutExtension(source));
        var target = Child(Path.Combine(game, "saves"), folder);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("A world with this folder name already exists. Choose another name.");
        try
        {
            if (Directory.Exists(source))
            {
                if (!IsWorld(source)) throw new InvalidDataException("The directory does not contain a Java world.");
                using var sourceLock = LockWorld(source);
                if (Contains(source, stage)) throw new IOException("The import source contains the staging directory.");
                await CopyTreeAsync(source, stage, ct).ConfigureAwait(false);
            }
            else
            {
                await ExtractAsync(source, stage, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(stage, target);
            return new(folder, target, true);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }
    private async Task<WorldExportResult> ExportAsync(string source, string destination, CancellationToken ct, CompressionLevel compression = CompressionLevel.Fastest)
    {
        destination = Path.GetFullPath(destination);
        if (Contains(source, destination)) throw new IOException("Export the ZIP outside the source world.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The output path already exists; it was retained.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var directories = new List<string>();
        var snapshot = Scan(source, directories);
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long copied = 0;
        var total = snapshot.Sum(file => new FileInfo(file).Length);
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var directory in directories.Where(directory => directory != source))
                    archive.CreateEntry(Path.GetRelativePath(source, directory).Replace('\\', '/') + "/");
                foreach (var file in snapshot)
                {
                    ct.ThrowIfCancellationRequested();
                    var before = await DigestAsync(file, ct).ConfigureAwait(false);
                    fingerprints[file] = before;
                    var entry = archive.CreateEntry(Path.GetRelativePath(source, file).Replace('\\', '/'), compression);
                    using var input = File.OpenRead(file);
                    using var output = entry.Open();
                    await CopyAsync(input, output, bytes => { copied += bytes; Report(copied, total); }, ct).ConfigureAwait(false);
                    if (before != await DigestAsync(file, ct).ConfigureAwait(false))
                        throw new IOException("World content changed while exporting; the original was retained.");
                }
            }

            if (!snapshot.SequenceEqual(Scan(source), StringComparer.OrdinalIgnoreCase))
                throw new IOException("World contents changed while exporting; retry after closing applications that modify it.");
            foreach (var pair in fingerprints)
            {
                if (pair.Value != await DigestAsync(pair.Key, ct).ConfigureAwait(false))
                    throw new IOException("World content changed after being archived; no output was published.");
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            return new(destination, snapshot.Length, copied);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<WorldArchiveRecord> BackupAsync(string id, string world, CancellationToken ct)
    {
        var backupId = Guid.NewGuid().ToString("N");
        var record = new JsonObject { ["id"] = backupId, ["instanceId"] = id, ["world"] = world, ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["state"] = "preparing" };
        await DescribeRecordAsync(record, GetDirectory(id, world), ct).ConfigureAwait(false);
        var file = Child(Backups(id), backupId + ".json");
        await JsonFiles.WriteAsync(file, record, ct).ConfigureAwait(false);
        var archive = Child(Backups(id), backupId + ".zip");
        await ExportAsync(GetDirectory(id, world), archive, ct).ConfigureAwait(false);
        record["sha256"] = await DigestAsync(archive, ct).ConfigureAwait(false);
        record["state"] = "complete";
        await JsonFiles.WriteAsync(file, record, CancellationToken.None).ConfigureAwait(false);
        return WorldArchiveRecord.Read(record);
    }
    private async Task<WorldRestoreResult> RestoreAsync(string id, string world, string backupId, CancellationToken ct)
    {
        ValidateId(backupId);
        var record = JsonFiles.ReadObject(Child(Backups(id), backupId + ".json"));
        if (record["instanceId"]?.ToString() != id || record["world"]?.ToString() != world || record["state"]?.ToString() != "complete")
            throw new InvalidDataException("This backup does not belong to the selected world.");
        var archive = Child(Backups(id), backupId + ".zip");
        if (await DigestAsync(archive, ct).ConfigureAwait(false) != record["sha256"]?.ToString())
            throw new InvalidDataException("The world backup changed or is damaged.");
        var target = GetDirectory(id, world);
        var stage = Path.Combine(Game(id), ".rodel", "world-staging", Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractAsync(archive, stage, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var recovery = await RetainAsync(id, world, "restore", ct).ConfigureAwait(false);
            try { Directory.Move(stage, target); }
            catch
            {
                Directory.Move(Child(History(id), recovery["id"]!.ToString()), target);
                recovery["state"] = "rolledBack";
                await JsonFiles.WriteAsync(Child(History(id), recovery["id"]!.ToString() + ".json"), recovery, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            return new(world, backupId, recovery["id"]!.GetValue<string>());
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }
    private async Task<WorldArchiveRecord> RemoveAsync(string id, string world, FileStream? worldLock, CancellationToken ct)
    {
        var record = await RetainAsync(id, world, "remove", ct).ConfigureAwait(false);
        worldLock?.Dispose();
        await DeleteCompressedDirectoryAsync(id, record).ConfigureAwait(false);
        return WorldArchiveRecord.Read(record);
    }
    private async Task<JsonObject> RetainAsync(string id, string world, string operation, CancellationToken ct)
    {
        var source = GetDirectory(id, world);
        var recoveryId = Guid.NewGuid().ToString("N");
        var root = History(id);
        Directory.CreateDirectory(root);
        var record = new JsonObject
        {
            ["id"] = recoveryId,
            ["instanceId"] = id,
            ["world"] = world,
            ["operation"] = operation,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["state"] = "preparing"
        };
        var manifest = Child(root, recoveryId + ".json");
        await DescribeRecordAsync(record, source, ct).ConfigureAwait(false);
        await JsonFiles.WriteAsync(manifest, record, ct).ConfigureAwait(false);
        try
        {
            if (operation == "remove")
            {
                await PrepareCompressionAsync(id, record, source, ct).ConfigureAwait(false);
                await JsonFiles.WriteAsync(manifest, record, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            Directory.Move(source, Child(root, recoveryId));
            record["state"] = "retained";
            await JsonFiles.WriteAsync(manifest, record, CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch
        {
            if (Directory.Exists(source) && !Directory.Exists(Child(root, recoveryId)))
            {
                var archive = Child(root, recoveryId + ".zip");
                if (File.Exists(archive)) File.Delete(archive);
                record["state"] = "failed";
                await JsonFiles.WriteAsync(manifest, record, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }
    private async Task<WorldArchiveRecord> RecoverAsync(string id, string recoveryId, string? name, CancellationToken ct)
    {
        ValidateId(recoveryId);
        var file = Child(History(id), recoveryId + ".json");
        var record = JsonFiles.ReadObject(file);
        if (record["instanceId"]?.ToString() != id || record["state"]?.ToString() is not ("retained" or "preparing"))
            throw new InvalidDataException("This recovery record is unavailable for this instance.");
        var restoredName = name ?? record["world"]!.ToString();
        var target = Child(Path.Combine(Game(id), "saves"), restoredName);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("The original world folder is occupied; nothing was overwritten.");
        var source = Child(History(id), recoveryId);
        if (record["storage"]?.ToString() == "zip" && !Directory.Exists(source))
        {
            var archive = Child(History(id), recoveryId + ".zip");
            if (await DigestAsync(archive, ct).ConfigureAwait(false) != record["sha256"]?.ToString())
                throw new InvalidDataException("The compressed world changed or is damaged. Nothing was restored.");
            var stage = Path.Combine(Game(id), ".rodel", "world-staging", Guid.NewGuid().ToString("N"));
            try
            {
                await ExtractAsync(archive, stage, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(stage, target);
                record["state"] = "recovered";
                record["restoredFolder"] = restoredName;
                await JsonFiles.WriteAsync(file, record, CancellationToken.None).ConfigureAwait(false);
                File.Delete(archive);
                return WorldArchiveRecord.Read(record);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
        }

        using var worldLock = LockWorld(source);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(source, target);
        record["state"] = "recovered";
        record["restoredFolder"] = restoredName;
        await JsonFiles.WriteAsync(file, record, CancellationToken.None).ConfigureAwait(false);
        return WorldArchiveRecord.Read(record);
    }
    private async Task<WorldArchiveRecord> CompressAsync(string id, string recoveryId, CancellationToken ct)
    {
        ValidateId(recoveryId);
        var recordFile = Child(History(id), recoveryId + ".json");
        var record = JsonFiles.ReadObject(recordFile);
        if (record["instanceId"]?.ToString() != id || record["state"]?.ToString() is not ("retained" or "preparing"))
            throw new InvalidDataException("This recovery record is unavailable.");
        var source = Child(History(id), recoveryId);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The uncompressed recovery world is missing.");
        using (var worldLock = LockWorld(source))
        {
            await PrepareCompressionAsync(id, record, source, ct).ConfigureAwait(false);
            record["state"] = "retained";
            await JsonFiles.WriteAsync(recordFile, record, CancellationToken.None).ConfigureAwait(false);
        }

        await DeleteCompressedDirectoryAsync(id, record).ConfigureAwait(false);
        return WorldArchiveRecord.Read(record);
    }
    private async Task PrepareCompressionAsync(string id, JsonObject record, string source, CancellationToken ct)
    {
        var archive = Child(History(id), record["id"]!.ToString() + ".zip");
        if (!File.Exists(archive))
            await ExportAsync(source, archive, ct, CompressionLevel.Optimal).ConfigureAwait(false);
        await VerifyArchiveAsync(source, archive, ct).ConfigureAwait(false);
        record["sha256"] = await DigestAsync(archive, ct).ConfigureAwait(false);
        record["originalBytes"] = Scan(source).Sum(file => new FileInfo(file).Length);
        record["archiveBytes"] = new FileInfo(archive).Length;
        record["storage"] = "zip";
    }
    private static async Task VerifyArchiveAsync(string source, string archive, CancellationToken ct)
    {
        var directories = new List<string>();
        var files = Scan(source, directories);
        using var zip = ZipFile.OpenRead(archive);
        var archiveFiles = zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        var expected = files.ToDictionary(file => Path.GetRelativePath(source, file).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        if (archiveFiles.Length != expected.Count || archiveFiles.Any(entry => !expected.ContainsKey(entry.FullName)))
            throw new InvalidDataException("The compressed world does not contain the complete source file set.");
        foreach (var entry in archiveFiles)
        {
            using var stream = entry.Open();
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
            if (actual != await DigestAsync(expected[entry.FullName], ct).ConfigureAwait(false))
                throw new InvalidDataException("The compressed world does not match its source. The folder was retained.");
        }

        var archiveDirectories = zip.Entries.Where(entry => entry.FullName.EndsWith('/')).Select(entry => entry.FullName.TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (directories.Where(directory => directory != source).Any(directory => !archiveDirectories.Contains(Path.GetRelativePath(source, directory).Replace('\\', '/'))))
            throw new InvalidDataException("The compressed world is missing source directories.");
    }
    private async Task DeleteCompressedDirectoryAsync(string id, JsonObject record)
    {
        var recoveryId = record["id"]!.ToString();
        ValidateId(recoveryId);
        if (record["storage"]?.ToString() != "zip") throw new InvalidDataException("No verified compressed world is available.");
        var directory = Child(History(id), recoveryId);
        var archive = Child(History(id), recoveryId + ".zip");
        if (await DigestAsync(archive, CancellationToken.None).ConfigureAwait(false) != record["sha256"]?.ToString())
            throw new InvalidDataException("The compressed world changed; the complete folder was retained.");
        await VerifyArchiveAsync(directory, archive, CancellationToken.None).ConfigureAwait(false);
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LauncherOperationException("WORLD_COMPRESSION_CLEANUP_FAILED",
                "The compressed world is safe, but the uncompressed retained copy could not be removed.", new JsonObject { ["recoveryId"] = recoveryId, ["cause"] = error.Message });
        }
    }
    private async Task ExtractAsync(string archive, string stage, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entries = new List<(ZipArchiveEntry Entry, string Path)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (normalized.StartsWith('/') || parts.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || part.TrimEnd(' ', '.') != part) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
            {
                throw new InvalidDataException("The world ZIP contains an unsafe path or symbolic link.");
            }

            if (!names.Add(normalized.TrimEnd('/'))) throw new InvalidDataException("The world ZIP contains duplicate paths.");
            entries.Add((entry, normalized));
        }

        var roots = entries.Where(pair => pair.Path.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase)
            || pair.Path.EndsWith("level.dat_old", StringComparison.OrdinalIgnoreCase))
            .Where(pair => string.Equals(Path.GetFileName(pair.Path), "level.dat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(pair.Path), "level.dat_old", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Path[..(pair.Path.LastIndexOf('/') + 1)]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0)
        {
            roots = [.. entries.Select(pair => pair.Path.Split('/'))
                .Select(parts => (Parts: parts, Index: Array.FindIndex(parts, part => part.Equals("region", StringComparison.OrdinalIgnoreCase))))
                .Where(pair => pair.Index >= 0)
                .Select(pair => pair.Index == 0 ? string.Empty : string.Join('/', pair.Parts.Take(pair.Index)) + "/")
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        if (roots.Length != 1) throw new InvalidDataException("Select a ZIP containing one Java world with level.dat.");
        var prefix = roots[0];
        Directory.CreateDirectory(stage);
        long done = 0;
        var total = entries.Where(pair => pair.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Sum(pair => pair.Entry.Length);
        foreach (var pair in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!pair.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || pair.Path.Length <= prefix.Length) continue;
            var relative = pair.Path[prefix.Length..];
            if (relative == "session.lock") continue;
            var target = Path.GetFullPath(Path.Combine(stage, relative.Replace('/', '\\')));
            if (!Contains(stage, target)) throw new InvalidDataException("The world ZIP escapes its staging directory.");
            if (pair.Path.EndsWith('/')) { Directory.CreateDirectory(target); continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = pair.Entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await CopyAsync(input, output, bytes => { done += bytes; Report(done, total); }, ct).ConfigureAwait(false);
        }

        if (!IsWorld(stage)) throw new InvalidDataException("The archive did not produce a world.");
    }
    private static bool Contains(string root, string path) => ResourceLease.CanonicalPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(ResourceLease.CanonicalPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid world archive/recovery ID.", nameof(id));
    }
    private static async Task DescribeRecordAsync(JsonObject record, string directory, CancellationToken ct)
    {
        record["name"] = record["world"]!.DeepClone();
        try
        {
            var metadata = new WorldSummary { Folder = Path.GetFileName(directory), Name = string.Empty, Directory = directory };
            await WorldMetadata.ReadAsync(Path.Combine(directory, "level.dat"), metadata, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(metadata.Name)) record["name"] = metadata.Name;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException or ArgumentException)
        {
            record["metadataWarning"] = error.Message;
        }
    }
    private static string[] Scan(string source, List<string>? directories = null)
    {
        var files = new List<string>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string directory)
        {
            var canonical = ResourceLease.CanonicalPath(directory);
            if (!active.Add(canonical)) throw new IOException("The world contains a directory link cycle.");
            directories?.Add(directory);
            foreach (var file in Directory.EnumerateFiles(directory))
                if (!string.Equals(file, Path.Combine(source, "session.lock"), StringComparison.OrdinalIgnoreCase)) files.Add(file);
            foreach (var child in Directory.EnumerateDirectories(directory)) Visit(child);
            active.Remove(canonical);
        }

        Visit(source);
        return [.. files.Order(StringComparer.OrdinalIgnoreCase)];
    }
    private async Task CopyTreeAsync(string source, string target, CancellationToken ct)
    {
        var directories = new List<string>();
        var files = Scan(source, directories);
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = files.Sum(file => new FileInfo(file).Length);
        long done = 0;
        foreach (var directory in directories)
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var before = await DigestAsync(file, ct).ConfigureAwait(false);
            fingerprints[file] = before;
            using (var input = File.OpenRead(file))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyAsync(input, output, bytes => { done += bytes; Report(done, total); }, ct).ConfigureAwait(false);
            if (before != await DigestAsync(destination, ct).ConfigureAwait(false) || before != await DigestAsync(file, ct).ConfigureAwait(false))
                throw new IOException("World content changed while copying; the original was retained.");
        }

        if (!files.SequenceEqual(Scan(source), StringComparer.OrdinalIgnoreCase))
            throw new IOException("World files changed during import; the original was retained.");
        foreach (var pair in fingerprints)
        {
            if (pair.Value != await DigestAsync(pair.Key, ct).ConfigureAwait(false))
                throw new IOException("World content changed after copying; no world was published.");
        }
    }
    private static async Task<string> DigestAsync(string file, CancellationToken ct)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }
    private static async Task CopyAsync(Stream input, Stream output, Action<int> copied, CancellationToken ct)
    {
        var buffer = new byte[65536];
        while (true)
        {
            var count = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            copied(count);
        }
    }
    private void Report(long bytes, long total)
    {
        try { progress?.Report(new LauncherProgress { Stage = "world-copy", Bytes = bytes, TotalBytes = total }); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("World progress observer failed: {0}", error.Message); }
    }
}