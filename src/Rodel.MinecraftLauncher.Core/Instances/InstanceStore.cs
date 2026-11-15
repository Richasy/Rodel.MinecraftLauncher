// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

public sealed partial class InstanceStore(LauncherPaths paths)
{
    private const string OwnerFile = ".rodel-instance-owner";
    public IReadOnlyList<InstanceRecord> List(Action<string, Exception>? reportUnreadable = null)
    {
        if (!Directory.Exists(paths.InstancesDirectory))
            return [];
        var instances = new List<InstanceRecord>();
        foreach (var directory in Directory.EnumerateDirectories(paths.InstancesDirectory))
        {
            var file = Path.Combine(directory, "instance.json");
            if (!File.Exists(file))
                continue;
            try
            {
                var instance = Read(file);
                _ = instance.Id;
                _ = instance.Name;
                _ = instance.GameDirectory;
                instances.Add(instance);
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
            {
                reportUnreadable?.Invoke(file, exception);
            }
        }

        return [.. instances.OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public InstanceRecord Get(string id) => Read(MetadataPath(id));
    internal async Task<InstanceRecord> SetServerOwnerAsync(string id, string? serverId, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: ct).ConfigureAwait(false);
        var metadata = Get(id).ToMetadata();
        if (serverId is null)
            metadata.Remove("serverId");
        else
            metadata["serverId"] = serverId;
        return await SaveAsync(id, metadata, ct).ConfigureAwait(false);
    }
    public async Task<InstanceRecord> CreateAsync(string name, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var id = Guid.NewGuid().ToString("N");
        var game = Path.Combine(paths.InstancesDirectory, id, "game");
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(game);
            await File.WriteAllTextAsync(Path.Combine(game, OwnerFile), id, cancellationToken).ConfigureAwait(false);
            var runtime = Path.Combine(paths.InstancesDirectory, id, "runtime");
            await SharedRuntimeLayout.InitializeAsync(paths, runtime, cancellationToken).ConfigureAwait(false);
            var instance = await SaveAsync(id, new JsonObject { ["schemaVersion"] = 2, ["id"] = id, ["name"] = name, ["gameDirectory"] = "game", ["minecraftDirectory"] = "runtime", ["versionId"] = version, ["managed"] = true }, cancellationToken).ConfigureAwait(false);
            await new LauncherActivityLog(paths).WriteAsync("instance.registered", id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            return instance;
        }
        catch
        {
            DeleteNewDirectory(Path.GetDirectoryName(game)!);
            throw;
        }
    }

    /// <summary>Registers an existing game directory with its selected runtime directory and imported settings without deleting the source.</summary>
    public async Task<InstanceRecord> ImportAsync(string name, string gameDirectory, string? version = null, string? minecraftDirectory = null, JsonObject? importedSettings = null, JsonObject? sourceSettings = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var game = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(game))
            throw new DirectoryNotFoundException(game);
        var minecraft = Path.GetFullPath(minecraftDirectory ?? game);
        if (!Directory.Exists(minecraft))
            throw new DirectoryNotFoundException(minecraft);
        if (version is null && Directory.Exists(Path.Combine(minecraft, "versions")))
        {
            var candidates = Directory.EnumerateDirectories(Path.Combine(minecraft, "versions")).Where(p => File.Exists(Path.Combine(p, Path.GetFileName(p) + ".json"))).Take(2).ToArray();
            if (candidates.Length == 1)
                version = Path.GetFileName(candidates[0]);
        }

        var id = Guid.NewGuid().ToString("N");
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        return await SaveAsync(id, new JsonObject { ["id"] = id, ["name"] = name, ["gameDirectory"] = game, ["minecraftDirectory"] = minecraft, ["versionId"] = version, ["managed"] = false, ["settings"] = importedSettings?.DeepClone(), ["sourceSettings"] = sourceSettings?.DeepClone() }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstanceRecord> RenameAsync(string id, string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: ct).ConfigureAwait(false);
        var metadata = Get(id).ToMetadata();
        metadata["name"] = name;
        return await SaveAsync(id, metadata, ct).ConfigureAwait(false);
    }

    public async Task<InstanceRecord> RelinkAsync(string id, string gameDirectory, string? minecraftDirectory = null, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: ct).ConfigureAwait(false);
        var previous = Get(id);
        if (new Rodel.MinecraftLauncher.Core.Launch.SessionService(paths).HasActiveGame(previous.GameDirectory))
            throw new InvalidOperationException("Stop the game before changing its directory association.");
        var game = Path.GetFullPath(gameDirectory);
        var minecraft = Path.GetFullPath(minecraftDirectory ?? (SamePath(previous.GameDirectory, previous.MinecraftDirectory) ? game : previous.MinecraftDirectory));
        if (!Directory.Exists(game))
            throw new DirectoryNotFoundException(game);
        if (!Directory.Exists(minecraft))
            throw new DirectoryNotFoundException(minecraft);
        var metadata = previous.ToMetadata();
        RebaseJava(metadata, previous.GameDirectory, game);
        metadata["gameDirectory"] = game;
        metadata["minecraftDirectory"] = minecraft;
        // Association does not grant ownership of files at the new location.
        metadata["managed"] = false;
        return await SaveAsync(id, metadata, ct).ConfigureAwait(false);
    }

    public Task<InstanceRecord> CopyAsync(string id, string name, CancellationToken cancellationToken = default) =>
        CopyAsync(id, name, null, cancellationToken);

    /// <summary>Copies complete game content and the selected runtime into an independent managed instance.</summary>
    public async Task<InstanceRecord> CopyAsync(string id, string name, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        var source = Get(id);
        using var gameLease = await ResourceLease.AcquireAsync(resource: PhysicalPath(source.GameDirectory), cancellationToken: cancellationToken).ConfigureAwait(false);
        using var runtimeLease = SamePath(source.GameDirectory, source.MinecraftDirectory) ? null
            : await ResourceLease.AcquireAsync(resource: PhysicalPath(source.MinecraftDirectory), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (new Launch.SessionService(paths).HasActiveGame(source.GameDirectory))
            throw new InvalidOperationException("Stop the game before copying its files.");
        var settings = new SettingsService(paths).GetEffective(source).Values.DeepClone().AsObject();
        var preview = new InstanceCopySource(source.GameDirectory, source.MinecraftDirectory,
            settings["versionId"]?.GetValue<string>() ?? source.VersionId, settings, source.ToMetadata()["sourceSettings"]);
        return await CopyImportAsync(name, preview, progress, cancellationToken, source.ToMetadata()).ConfigureAwait(false);
    }

    /// <summary>Reads a migration preview without changing the source or registering an instance.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Retain the public service instance API for callers and future instance configuration.")]
    public InstanceImportPreview PreviewImport(string directory, string? profileId = null, string? minecraftDirectory = null, string? launcherConfig = null, string? versionId = null, string? gameDirectory = null) => InstanceMigration.Preview(directory, profileId, minecraftDirectory, launcherConfig, versionId, gameDirectory);
    /// <summary>Copies a game installation into managed storage and registers it only after migration completes, retaining the source.</summary>
    public async Task<InstanceRecord> ImportMigrationAsync(string name, string directory, string? profileId = null, bool copy = false, string? minecraftDirectory = null, string? version = null, string? launcherConfig = null, string? gameDirectory = null, IProgress<LauncherProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var preview = PreviewImport(directory, profileId, minecraftDirectory, launcherConfig, version, gameDirectory);
        if (preview.NeedsGameDirectory)
            throw new InvalidOperationException("Select the game directory before importing this version.");
        if (preview.Format == "minecraft-profiles" && preview.ProfileId is null && preview.SourceSettings["profiles"] is JsonArray { Count: > 1 })
            throw new InvalidOperationException("Select a profile before importing a directory containing multiple profiles.");
        var game = preview.GameDirectory;
        var shared = preview.MinecraftDirectory;
        if (copy)
        {
            return await CopyImportAsync(name, new(preview.GameDirectory, preview.MinecraftDirectory, preview.VersionId,
                preview.Settings, preview.SourceSettings), progress, cancellationToken).ConfigureAwait(false);
        }

        return await ImportAsync(name: name, gameDirectory: game, version: version ?? preview.VersionId, cancellationToken: cancellationToken, minecraftDirectory: shared, importedSettings: preview.Settings, sourceSettings: preview.SourceSettings).ConfigureAwait(false);
    }

    private static void RebaseJava(JsonObject metadata, string oldGame, string newGame)
    {
        void Rebase(JsonObject node)
        {
            if (node["javaPath"]?.GetValue<string>() is not { Length: > 0 } java)
                return;
            var absolute = Path.GetFullPath(java, oldGame);
            node["javaPath"] = IsInside(absolute, oldGame) ? Path.GetFullPath(Path.Combine(newGame, Path.GetRelativePath(oldGame, absolute))) : absolute;
        }

        Rebase(metadata);
        if (metadata["settings"] is JsonObject settings)
            Rebase(settings);
    }

    public async Task<InstanceRecord> MoveAsync(string id, string targetDirectory, CancellationToken cancellationToken = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        var source = Get(id);
        var target = Path.GetFullPath(targetDirectory);
        if (SamePath(source.GameDirectory, target))
            return source;
        if (!source.Managed || !OwnsGame(source))
            throw new InvalidOperationException("An associated game directory cannot be moved; copy it to a managed instance first.");
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"Move target already exists: {target}");
        if (IsInside(PhysicalPath(target), PhysicalPath(source.GameDirectory)) || IsInside(PhysicalPath(source.GameDirectory), PhysicalPath(target)))
            throw new IOException("Move source and target must not contain one another.");
        using var sourceLease = await ResourceLease.AcquireAsync(resource: PhysicalPath(source.GameDirectory), cancellationToken: cancellationToken).ConfigureAwait(false);
        using var targetLease = await ResourceLease.AcquireAsync(resource: PhysicalPath(target), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (new Rodel.MinecraftLauncher.Core.Launch.SessionService(paths).HasActiveGame(source.GameDirectory))
            throw new InvalidOperationException("Stop the running game before moving its directory.");
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"Move target already exists: {target}");
        try
        {
            await CopyDirectoryAsync(source.GameDirectory, target, cancellationToken).ConfigureAwait(false);
            var json = source.ToJson();
            RebaseJava(json, source.GameDirectory, target);
            json["gameDirectory"] = target;
            if (SamePath(source.GameDirectory, source.MinecraftDirectory))
                json["minecraftDirectory"] = target;
            var moved = await SaveAsync(id, json, cancellationToken).ConfigureAwait(false);
            // Metadata is committed before old files are removed; deletion failure leaves a recoverable duplicate.
            DeleteNewDirectory(source.GameDirectory);
            return moved;
        }
        catch
        {
            if (SamePath(Get(id).GameDirectory, source.GameDirectory))
                DeleteNewDirectory(target);
            throw;
        }
    }

    /// <summary>Removes the registration and its identifiable backup archives, optionally deleting owned game files. Failures retain registration for retry.</summary>
    public async Task RemoveAsync(string id, bool deleteFiles = false, CancellationToken cancellationToken = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: MetadataPath(id), cancellationToken: cancellationToken).ConfigureAwait(false);
        var record = Get(id);
        var directory = Path.GetDirectoryName(record.MetadataPath)!;
        var removalFile = Path.Combine(directory, "removal.json");
        if (deleteFiles)
        {
            using var gameLease = await ResourceLease.AcquireAsync(resource: PhysicalPath(record.GameDirectory), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (new Rodel.MinecraftLauncher.Core.Launch.SessionService(paths).HasActiveGame(record.GameDirectory))
                throw new InvalidOperationException("Stop the running game before deleting its files.");
            var game = PhysicalPath(record.GameDirectory);
            var runtime = PhysicalPath(record.MinecraftDirectory);
            if (SamePath(game, directory) || IsInside(directory, game)
                || SamePath(game, paths.DependenciesDirectory) || IsInside(paths.DependenciesDirectory, game)
                || IsInside(game, paths.DependenciesDirectory)
                || SamePath(game, paths.BackupsDirectory) || IsInside(paths.BackupsDirectory, game)
                || IsInside(game, paths.BackupsDirectory))
            {
                throw new LauncherOperationException("INSTANCE_DELETE_SCOPE", "The game directory contains launcher metadata or shared resources and cannot be deleted as private game content.");
            }

            var resuming = File.Exists(removalFile);
            if (resuming)
            {
                var removal = JsonFiles.ReadObject(removalFile);
                if (removal["instanceId"]?.GetValue<string>() != record.Id
                    || removal["operation"]?.GetValue<string>() != "remove-owned-files"
                    || !SamePath(removal["gameDirectory"]?.GetValue<string>() ?? string.Empty, game)
                    || !SamePath(removal["minecraftDirectory"]?.GetValue<string>() ?? string.Empty, runtime))
                {
                    throw new LauncherOperationException("INSTANCE_REMOVAL_MISMATCH", "The pending removal does not match this instance. Keep its files and inspect the removal record.");
                }
            }

            if (!record.Managed || (Directory.Exists(game) ? !OwnsGame(record) : !resuming))
                throw new LauncherOperationException("INSTANCE_FILES_NOT_OWNED", "Game files can only be deleted from a directory owned by this managed instance. Remove the registration without deleting game files, or copy it to a managed instance first.");
            var privateRuntime = !SamePath(runtime, game) && IsInside(runtime, directory) && !IsInside(game, runtime);
            foreach (var other in List().Where(other => other.Id != record.Id))
            {
                var otherGame = PhysicalPath(other.GameDirectory);
                var otherRuntime = PhysicalPath(other.MinecraftDirectory);
                if (SamePath(otherGame, game) || IsInside(otherGame, game)
                    || SamePath(otherRuntime, game) || IsInside(otherRuntime, game)
                    || (privateRuntime && (SamePath(otherGame, runtime) || IsInside(otherGame, runtime)
                        || SamePath(otherRuntime, runtime) || IsInside(otherRuntime, runtime))))
                {
                    throw new LauncherOperationException("INSTANCE_FILES_IN_USE", "Another registered instance uses these private files. Remove that dependency before deleting them.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!resuming)
            {
                await JsonFiles.WriteAsync(removalFile, new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["operation"] = "remove-owned-files",
                    ["instanceId"] = record.Id,
                    ["gameDirectory"] = game,
                    ["minecraftDirectory"] = runtime,
                }, cancellationToken).ConfigureAwait(false);
            }

            await new LauncherActivityLog(paths).WriteAsync("instance.removal.started", id, code: resuming ? "RESUME" : "FILES", cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            DeleteNewDirectory(game);
            if (privateRuntime)
                DeleteNewDirectory(runtime);
            await new LauncherActivityLog(paths).WriteAsync("instance.removal.files-deleted", id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        }

        // The game lease has been released before acquiring archive leases, matching restore's lock ordering.
        // Keep registration (and any removal journal) until backup cleanup succeeds so failures remain retryable.
        await new BackupService(paths).DeleteForInstanceAsync(record.Id, deleteFiles ? CancellationToken.None : cancellationToken).ConfigureAwait(false);

        // Once all owned game files and backups have been deleted, finish removing their registration.
        // Cancellation at this boundary must not leave a dangling, non-retryable instance.
        if (!deleteFiles) cancellationToken.ThrowIfCancellationRequested();
        File.Delete(record.MetadataPath);
        try
        {
            if (File.Exists(removalFile)) File.Delete(removalFile);
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Registration removal is committed. Ancillary evidence cleanup must not
            // report a retryable instance operation after that instance no longer exists.
            await new LauncherActivityLog(paths).WriteAsync("instance.removal.cleanup-deferred", id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static InstanceRecord Read(string file) => new(JsonFiles.ReadObject(file), file);
    private string MetadataPath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('/') || id.Contains('\\'))
            throw new ArgumentException("Instance ID must be a single directory name.", nameof(id));
        var file = Path.Combine(paths.InstancesDirectory, id, "instance.json");
        return PhysicalPath(file);
    }

    private async Task<InstanceRecord> SaveAsync(string id, JsonObject json, CancellationToken token)
    {
        var file = MetadataPath(id);
        await JsonFiles.WriteAsync(file, json, token).ConfigureAwait(false);
        return new InstanceRecord(json, file);
    }

    private static bool OwnsGame(InstanceRecord record)
    {
        var marker = Path.Combine(record.GameDirectory, OwnerFile);
        return File.Exists(marker) && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0 && File.ReadAllText(marker) == record.Id;
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    private static bool IsInside(string child, string parent) => Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        var sourcePhysical = PhysicalPath(source);
        var destinationPhysical = PhysicalPath(destination);
        if (SamePath(sourcePhysical, destinationPhysical) || IsInside(destinationPhysical, sourcePhysical))
            throw new IOException("Copy destination must not be inside the source directory, including through a directory link.");
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Copy(string from, string to)
        {
            token.ThrowIfCancellationRequested();
            var physical = PhysicalPath(from);
            if (!active.Add(physical))
                throw new IOException($"Cannot materialize a directory link cycle while copying: {from}");
            try
            {
                Directory.CreateDirectory(to);
                foreach (var entry in Directory.EnumerateFileSystemEntries(from))
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.Combine(to, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        await Copy(entry, target).ConfigureAwait(false);
                    }
                    else
                    {
                        var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                        await using var inputDisposal = input.ConfigureAwait(false);
                        var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                        await using var outputDisposal = output.ConfigureAwait(false);
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                        await output.FlushAsync(token).ConfigureAwait(false);
                        output.Flush(true);
                    }
                }
            }
            finally
            {
                active.Remove(physical);
            }
        }

        await Copy(source, destination).ConfigureAwait(false);
    }

    // Enumerate explicitly so directory links are deleted themselves, never traversed.
    private static void DeleteNewDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (string.Equals(Path.GetFileName(entry), OwnerFile, StringComparison.Ordinal))
                continue;
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                DeleteNewDirectory(entry);
            else
                File.Delete(entry);
        }

        var marker = Path.Combine(path, OwnerFile);
        var owner = File.Exists(marker) ? File.ReadAllText(marker) : null;
        if (owner is not null)
            File.Delete(marker);
        try
        {
            Directory.Delete(path);
        }
        catch
        {
            if (owner is not null && Directory.Exists(path))
                File.WriteAllText(marker, owner);
            throw;
        }
    }
}