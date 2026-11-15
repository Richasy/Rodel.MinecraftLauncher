// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed class CacheService(LauncherPaths paths)
{
    /// <summary>Reads cache inventory and a non-destructive reclamation plan with installed ownership totals.</summary>
    public async Task<CacheStatus> StatusAsync(CancellationToken ct = default)
    {
        var status = new MaintenanceService(paths).CacheStatus();
        var plan = await PruneAsync(ct: ct).ConfigureAwait(false);
        long ownedBytes = 0;
        var ownedFiles = 0;
        foreach (var receipt in Files(Path.Combine(paths.CacheDirectory, "download-ownership")))
        {
            try
            {
                var file = JsonFiles.ReadObject(receipt)["path"]!.GetValue<string>();
                if (DownloadOwnership.Within(file, paths.InstancesDirectory) && File.Exists(file))
                {
                    ownedBytes += new FileInfo(file).Length;
                    ownedFiles++;
                }
            }
            catch (Exception ex) when (ReadableFailure(ex))
            {
            }
        }

        return new(status.Directory, status.Bytes, status.Files, status.Warnings, plan.CandidateBytes, plan, ownedBytes, ownedFiles);
    }

    private static readonly string[] DisposableDirectories = ["content-downloads", "loaders", "loader-catalogs", "java-probes"];
    private static readonly string[] DisposableMetadata = ["version_manifest_v2.json", "java-runtime-all.json"];
    /// <summary>Assesses or reclaims eligible cache files while retaining referenced, unknown, changed and busy files.</summary>
    public async Task<CachePruneResult> PruneAsync(int olderThanDays = 30, bool plan = true, CancellationToken ct = default)
    {
        if (olderThanDays < 0)
            throw new ArgumentException("Cache age cannot be negative.");
        var now = DateTime.UtcNow;
        var cutoff = olderThanDays >= (now - DateTime.MinValue).TotalDays ? DateTime.MinValue : now.AddDays(-olderThanDays);
        using var cacheLease = plan ? null : CacheActivity.TryEnter(paths, exclusive: true);
        var jobs = new JobService(paths).List();
        var unknownJobs = jobs.Any(x => x.Status == "unreadable" || x.ActivityUnknown == true);
        var activeJobs = jobs.Any(x => x.Status is "queued" or "running");
        var busy = !plan && (cacheLease is null || unknownJobs || activeJobs);
        var candidates = new List<CachePruneCandidate>();
        var retained = new List<CacheRetention>();
        var warnings = new List<CachePruneWarning>();
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unreadable = unknownJobs;
        var instances = new InstanceStore(paths).List((file, error) =>
        {
            unreadable = true;
            Warning(file, error);
        });
        foreach (var instance in instances)
        {
            if (Directory.Exists(instance.MinecraftDirectory))
                roots.Add(ResourceLease.CanonicalPath(instance.MinecraftDirectory));
            var lockPath = Path.Combine(instance.GameDirectory, ".rodel", "content-lock.json");
            if (File.Exists(lockPath))
            {
                try
                {
                    ReadValues(JsonFiles.ReadObject(lockPath));
                }
                catch (Exception ex) when (ReadableFailure(ex))
                {
                    unreadable = true;
                    Warning(lockPath, ex);
                }
            }
        }

        var installationRoots = Path.Combine(paths.CacheDirectory, "installation-roots.json");
        if (File.Exists(installationRoots))
        {
            try
            {
                foreach (var root in JsonFiles.ReadObject(installationRoots)["roots"] as JsonArray ?? [])
                {
                    if (root?.GetValue<string>() is { } value && Directory.Exists(value))
                        roots.Add(ResourceLease.CanonicalPath(value));
                }
            }
            catch (Exception ex) when (ReadableFailure(ex))
            {
                unreadable = true;
                Warning(installationRoots, ex);
            }
        }

        var receipts = new Dictionary<string, (string Receipt, JsonObject Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files(Path.Combine(paths.CacheDirectory, "download-ownership")))
        {
            try
            {
                var item = JsonFiles.ReadObject(file);
                if (item["path"]?.GetValue<string>() is { } value)
                    receipts[Path.GetFullPath(value)] = (file, item);
            }
            catch (Exception ex) when (ReadableFailure(ex))
            {
                Warning(file, ex);
            }
        }

        long reclaimed = 0;
        var removed = new List<string>();
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            var instance = instances.FirstOrDefault(x => ResourceLease.CanonicalPath(x.GameDirectory).Equals(root, StringComparison.OrdinalIgnoreCase));
            var owned = false;
            try
            {
                owned = instance is { Managed: true } && DownloadOwnership.Within(root, paths.InstancesDirectory) && File.Exists(Path.Combine(root, ".rodel-instance-owner")) && File.ReadAllText(Path.Combine(root, ".rodel-instance-owner")).Trim() == instance.Id;
            }
            catch (Exception ex) when (ReadableFailure(ex))
            {
                Warning(root, ex);
            }

            using var rootLease = !plan && !busy && owned ? await ResourceLease.AcquireAsync(resource: root, cancellationToken: ct).ConfigureAwait(false) : null;
            var activeGame = new SessionService(paths).HasActiveGame(root);
            var complete = !unreadable && !activeGame;
            string[] versionFiles;
            try
            {
                versionFiles = [.. ReferenceFiles(Path.Combine(root, "versions"))];
            }
            catch (Exception ex) when (ReadableFailure(ex))
            {
                complete = false;
                Warning(root, ex);
                versionFiles = [];
            }

            foreach (var metadata in versionFiles.Where(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(x) == Path.GetFileName(Path.GetDirectoryName(x))))
            {
                try
                {
                    var version = await new VersionResolver().ResolveAsync(root, Path.GetFileNameWithoutExtension(metadata), ct).ConfigureAwait(false);
                    foreach (var library in version["libraries"] as JsonArray ?? [])
                    {
                        if (library is null)
                            continue;
                        if (library["downloads"] is JsonObject downloads)
                        {
                            ReferenceArtifact(downloads["artifact"]);
                            if (downloads["artifact"] is not null && downloads["artifact"]?["path"] is null && library["name"]?.GetValue<string>() is { } artifactName)
                                refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "libraries"), MetadataRules.MavenPath(artifactName))));
                            foreach (var entry in downloads["classifiers"] as JsonObject ?? [])
                                ReferenceArtifact(entry.Value);
                        }
                        else if (library["name"]?.GetValue<string>() is { } name)
                        {
                            refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "libraries"), MetadataRules.MavenPath(name))));
                        }

                        if (library["name"]?.GetValue<string>() is { } nativeName && library["natives"] is JsonObject natives)
                        {
                            foreach (var native in natives)
                            {
                                foreach (var arch in new[]
                                {
                                    "32",
                                    "64"
                                }

                                )
                                {
                                    var coordinate = nativeName.Split('@', 2);
                                    var classifier = native.Value!.GetValue<string>().Replace("$" + "{arch}", arch);
                                    refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "libraries"), MetadataRules.MavenPath(coordinate[0] + ":" + classifier + (coordinate.Length > 1 ? "@" + coordinate[1] : string.Empty)))));
                                }
                            }
                        }
                    }

                    var assetId = version["assetIndex"]?["id"]?.GetValue<string>() ?? version["assets"]?.GetValue<string>() ?? "legacy";
                    var indexPath = Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "assets"), "indexes", assetId + ".json"));
                    if (version["assetIndex"] is not null || version["assets"] is not null || File.Exists(indexPath))
                    {
                        refs.Add(indexPath);
                        var index = JsonFiles.ReadObject(indexPath);
                        foreach (var entry in index["objects"] as JsonObject ?? [])
                        {
                            if (entry.Value?["hash"]?.GetValue<string>() is not { Length: 40 } hash)
                                throw new InvalidDataException("Asset index contains an invalid hash.");
                            refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "assets"), "objects", hash[..2], hash)));
                            hashes.Add(hash);
                            if (index["virtual"]?.GetValue<bool>() == true)
                                refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "assets"), "virtual", assetId, entry.Key)));
                        }
                    }

                    if (version["logging"]?["client"]?["file"]?["id"]?.GetValue<string>() is { } logId)
                        refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "assets"), "log_configs", logId)));
                    void ReferenceArtifact(JsonNode? item)
                    {
                        if (item?["path"]?.GetValue<string>() is { } relative)
                            refs.Add(Path.GetFullPath(Path.Combine(SharedRuntimeLayout.Resolve(root, "libraries"), relative)));
                    }
                }
                catch (Exception ex) when (ReadableFailure(ex))
                {
                    complete = false;
                    Warning(metadata, ex);
                }
            }

            foreach (var receipt in receipts.Values)
            {
                var file = receipt.Value["path"]!.GetValue<string>();
                if (!DownloadOwnership.Within(file, root) || !File.Exists(file))
                    continue;
                if (!DownloadOwnership.Within(file, SharedRuntimeLayout.Resolve(root, "libraries")) && !DownloadOwnership.Within(file, SharedRuntimeLayout.Resolve(root, "assets")))
                    continue;
                if (!owned || !complete || refs.Contains(file))
                {
                    Keep(file, !owned ? "external-root" : activeGame ? "active-game" : !complete ? "reference-data-unreadable" : "referenced");
                    continue;
                }

                await Consider(file, "unreferenced-installed-cache", receipt).ConfigureAwait(false);
            }
        }

        foreach (var file in DisposableMetadata.Select(name => Path.Combine(paths.CacheDirectory, name)).Where(File.Exists))
            await Consider(file, "expired-metadata").ConfigureAwait(false);
        foreach (var directory in DisposableDirectories)
        {
            foreach (var file in Files(Path.Combine(paths.CacheDirectory, directory)))
            {
                ct.ThrowIfCancellationRequested();
                if (unreadable && directory is "content-downloads" or "loaders")
                {
                    Keep(file, "reference-data-unreadable");
                    continue;
                }

                if (file.EndsWith(".rodel.partial.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (file.EndsWith(".rodel.partial", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var target = file[..^".rodel.partial".Length];
                        if (File.Exists(file + ".json") && JsonFiles.ReadObject(file + ".json")["destination"]?.GetValue<string>() is { } recordedTarget && Path.GetFullPath(recordedTarget).Equals(target, StringComparison.OrdinalIgnoreCase))
                            await Consider(file, "expired-partial").ConfigureAwait(false);
                        else
                            Keep(file, "unknown-partial");
                    }
                    catch (Exception ex) when (ReadableFailure(ex))
                    {
                        Warning(file, ex);
                    }

                    continue;
                }

                var registered = receipts.TryGetValue(ResourceLease.CanonicalPath(file), out var receipt);
                var relative = Path.GetRelativePath(Path.Combine(paths.CacheDirectory, directory), file).Split(Path.DirectorySeparatorChar);
                var recognized = directory switch
                {
                    "content-downloads" => relative.Length == 3 && relative[0] is "modrinth" or "curseforge" && relative[1].Length == 64 && relative[1].All(Uri.IsHexDigit),
                    "loaders" => relative.Length == 2 && relative[0] is "forge" or "neoforge" && (relative[1].EndsWith("-installer.jar") || relative[1].EndsWith("-installer.jar.sha1")),
                    "loader-catalogs" => relative.Length == 2 && relative[0] is "fabric" or "quilt" or "forge" or "neoforge" && relative[1].EndsWith(".json"),
                    "java-probes" => relative.Length == 1 && relative[0].Length == 69
                        && relative[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) && relative[0][..64].All(Uri.IsHexDigit),
                    _ => false
                };
                if (!registered && !recognized)
                {
                    Keep(file, "unknown-file");
                    continue;
                }

                try
                {
                    if (directory == "content-downloads" && await HasReferencedHash(file).ConfigureAwait(false))
                    {
                        Keep(file, "tracked-content");
                        continue;
                    }
                }
                catch (Exception ex) when (ReadableFailure(ex))
                {
                    Warning(file, ex);
                    continue;
                }

                await Consider(file, "expired-download-cache", registered ? receipt : null).ConfigureAwait(false);
            }
        }

        return new(plan, busy || activeJobs, unknownJobs, busy, reclaimed, candidates.Sum(x => x.Bytes), [.. candidates.Select(x => x.Path)], candidates, retained, removed, warnings);
        void Keep(string file, string reason) => retained.Add(new(file, reason));
        void Warning(string file, Exception ex) => warnings.Add(new(file, ex.Message, ex is IOException ? ex.HResult & 0xffff : null));
        void ReadValues(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length is 40 or 64 or 128 && text.All(Uri.IsHexDigit))
            {
                hashes.Add(text);
            }
            else if (node is JsonObject obj)
            {
                foreach (var pair in obj)
                    ReadValues(pair.Value);
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr)
                    ReadValues(item);
            }
        }

        async Task<bool> HasReferencedHash(string file)
        {
            if (hashes.Count == 0)
                return false;
            var input = File.OpenRead(file);
            await using var inputDisposal = input.ConfigureAwait(false);
            if (hashes.Any(x => x.Length == 40))
            {
                var sha1 = Convert.ToHexStringLower(await SHA1.HashDataAsync(input, ct).ConfigureAwait(false));
                if (hashes.Contains(sha1))
                    return true;
                input.Position = 0;
            }

            if (hashes.Any(x => x.Length == 64))
            {
                var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
                if (hashes.Contains(sha256))
                    return true;
                input.Position = 0;
            }

            return hashes.Any(x => x.Length == 128) && hashes.Contains(Convert.ToHexStringLower(await SHA512.HashDataAsync(input, ct).ConfigureAwait(false)));
        }

        async Task Consider(string file, string reason, (string Receipt, JsonObject Value)? receipt = null)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(file) || Linked(file) || File.GetLastWriteTimeUtc(file) > cutoff)
                return;
            try
            {
                using var fileLease = !plan && !busy ? await ResourceLease.AcquireAsync(resource: file, cancellationToken: ct).ConfigureAwait(false) : null;
                if (!File.Exists(file) || Linked(file) || File.GetLastWriteTimeUtc(file) > cutoff)
                    return;
                if (receipt is { } owned)
                {
                    var input = File.OpenRead(file);
                    await using var inputDisposal = input.ConfigureAwait(false);
                    if (input.Length != owned.Value["bytes"]?.GetValue<long>() || Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false)) != owned.Value["sha256"]?.GetValue<string>())
                    {
                        Keep(file, "modified-after-download");
                        return;
                    }
                }

                var bytes = new FileInfo(file).Length;
                candidates.Add(new(file, bytes, reason));
                if (plan || busy)
                    return;
                File.Delete(file);
                reclaimed += bytes;
                removed.Add(file);
                if (receipt is { } released && File.Exists(released.Receipt))
                    File.Delete(released.Receipt);
                if (reason == "expired-partial" && File.Exists(file + ".json"))
                    File.Delete(file + ".json");
            }
            catch (Exception ex) when (ReadableFailure(ex))
            {
                Warning(file, ex);
            }
        }
    }

    private static bool ReadableFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or FormatException;
    private static bool Linked(string file) => !ResourceLease.CanonicalPath(file).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<string> ReferenceFiles(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked version metadata requires retaining this root's caches: " + entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in ReferenceFiles(entry))
                    yield return file;
            }
            else
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<string> Files(string directory) => Directory.Exists(directory) && !Linked(directory) ? Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }) : [];
}