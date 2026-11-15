// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Installation;

/// <summary>Reports verified installation work, retaining the physical destination separately from its player-readable identity.</summary>
/// <param name="DisplayName">The logical resource name when the cache destination is content-addressed; otherwise omitted.</param>
public sealed record InstallationProgress(string Stage, string Path, long Bytes, long? TotalBytes, bool Reused, int CompletedFiles = 0, int? TotalFiles = null, int SharedReusedFiles = 0, string? DisplayName = null)
{
    /// <summary>Gets completion of the containing preparation sequence when this installation is one of its phases.</summary>
    public double? OverallPercent { get; init; }
}
public sealed class InstallationService
{
    private readonly LauncherPaths _paths;
    private readonly HttpClient _client;
    private readonly Uri _manifestUri;
    private readonly DownloadService _downloads;
    public InstallationService(LauncherPaths paths, HttpClient? httpClient = null, Uri? manifestUri = null)
    {
        _paths = paths;
        _client = httpClient ?? Rodel.MinecraftLauncher.Core.Downloads.NetworkSettings.Client(paths, TimeSpan.FromMinutes(10));
        _manifestUri = manifestUri ?? new Uri("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
        _downloads = new(paths, _client);
    }

    /// <summary>Combines the shared six-hour memory/disk release catalog with local versions; an explicit refresh bypasses its age policy.</summary>
    public async Task<VersionCatalog> GetVersionsAsync(bool offline = false, bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (offline && refresh)
            throw new ArgumentException("An offline catalog cannot be forcibly refreshed.");
        var (manifest, cached) = await VersionManifestCache.ReadAsync(_paths, _client, _manifestUri, offline, refresh, cancellationToken).ConfigureAwait(false);

        var local = new List<LocalVersionSummary>();
        var catalogPath = Path.Combine(_paths.CacheDirectory, "installation-roots.json");
        if (File.Exists(catalogPath) && JsonFiles.ReadObject(catalogPath)["roots"] is JsonArray roots)
        {
            foreach (var root in roots)
            {
                var directory = root?.GetValue<string>();
                if (directory is null || !Directory.Exists(Path.Combine(directory, "versions")))
                    continue;
                foreach (var versionDirectory in Directory.EnumerateDirectories(Path.Combine(directory, "versions")))
                {
                    var id = Path.GetFileName(versionDirectory);
                    if (File.Exists(Path.Combine(versionDirectory, id + ".json")))
                        local.Add(new(id, directory));
                }
            }
        }

        return new()
        {
            Cached = cached,
            Offline = offline,
            LocalVersions = [.. local],
            Versions = manifest["versions"] is JsonArray versions ? (JsonArray)versions.DeepClone() : null,
            Latest = manifest["latest"]?.DeepClone(),
            AdditionalMetadata = StoredReportFields.Remaining(new JsonObject(manifest.Where(pair => pair.Key is not ("cached" or "offline" or "localVersions")
                    && (pair.Key != "versions" || pair.Value is not JsonArray) && (pair.Key != "latest" || pair.Value is null))
                .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))))
        };
    }

    /// <summary>Prepares verified game files and reports actual work independently of optional activity logging.</summary>
    public async Task<InstallationResult> InstallAsync(string minecraftDirectory, string versionId, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var log = new LauncherActivityLog(_paths);
        var instanceId = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(minecraftDirectory)));
        await log.WriteAsync("installation.started", instanceId, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        try
        {
            var result = await InstallCoreAsync(minecraftDirectory, versionId, progress, cancellationToken).ConfigureAwait(false);
            if (!await log.WriteAsync("installation.completed", instanceId, count: result.CompletedFiles, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false))
                result.ActivityLogWarning = "ACTIVITY_LOG_UNAVAILABLE";
            await log.WriteAsync("installation.shared-reuse", instanceId, count: result.SharedReusedFiles, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            if (result.CacheReadFailures is > 0 and var failures)
                await log.WriteAsync("installation.cache-read-fallback", instanceId, count: failures, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            await log.WriteAsync(exception is OperationCanceledException ? "installation.canceled" : "installation.failed", instanceId,
                code: LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString(), nativeError: exception.HResult, httpStatus: exception is HttpRequestException network ? (int?)network.StatusCode : null, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<InstallationResult> InstallCoreAsync(string minecraftDirectory, string versionId, IProgress<InstallationProgress>? progress, CancellationToken cancellationToken)
    {
        minecraftDirectory = PhysicalPath(minecraftDirectory);
        using var cacheUse = await CacheActivity.EnterAsync(_paths, cancellationToken).ConfigureAwait(false);
        ValidateId(versionId);
        using var lease = await ResourceLease.AcquireAsync(resource: minecraftDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        await SharedRuntimeLayout.EnsureWritableAsync(_paths, minecraftDirectory, cancellationToken).ConfigureAwait(false);
        await SharedRuntimeLayout.EnsureClientSlotAsync(_paths, minecraftDirectory, versionId, cancellationToken).ConfigureAwait(false);
        var catalogPath = Path.Combine(_paths.CacheDirectory, "installation-roots.json");
        using (var catalogLease = await ResourceLease.AcquireAsync(resource: catalogPath, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var catalog = File.Exists(catalogPath) ? JsonFiles.ReadObject(catalogPath) : new JsonObject
            {
                ["roots"] = new JsonArray()
            };
            var roots = catalog["roots"] as JsonArray ?? [];
            if (!roots.Any(item => string.Equals(item?.GetValue<string>(), minecraftDirectory, StringComparison.OrdinalIgnoreCase)))
                roots.Add((JsonNode?)JsonValue.Create(minecraftDirectory));
            if (catalog["roots"] is null)
                catalog["roots"] = roots;
            await JsonFiles.WriteAsync(catalogPath, catalog, cancellationToken).ConfigureAwait(false);
        }

        var downloaded = 0;
        var reused = 0;
        var completed = 0;
        var sharedReused = 0;
        int? totalFiles = null;
        var progressGate = new object();
        var sharedCache = new SharedInstallationCache(_paths, minecraftDirectory);
        void Report(string stage, string path, long bytes, long? total, bool cached, string? displayName = null)
        {
            lock (progressGate)
                progress?.Report(new(stage, path, bytes, total, cached, Volatile.Read(ref completed), totalFiles, Volatile.Read(ref sharedReused), displayName));
        }

        var journal = SafePath(minecraftDirectory, $".rodel/install/{versionId}.json");
        await JsonFiles.WriteAsync(journal, new JsonObject { ["version"] = versionId, ["state"] = "preparing", ["strategy"] = "verified-file-replay" }, cancellationToken).ConfigureAwait(false);
        async Task Fetch(string stage, string path, JsonNode descriptor, CancellationToken token, string? fallbackUrl = null, string? displayName = null)
        {
            path = PhysicalPath(path);
            Report(stage, path, 0, descriptor["size"]?.GetValue<long>(), false, displayName);
            var url = fallbackUrl ?? descriptor["url"]?.GetValue<string>();
            var sha1 = descriptor["sha1"]?.GetValue<string>();
            var size = descriptor["size"]?.GetValue<long>();
            if (string.IsNullOrWhiteSpace(url))
            {
                if (!await DownloadService.ValidAsync(path, sha1, size, token).ConfigureAwait(false))
                    throw new FileNotFoundException($"Local {stage} file is missing or does not match its metadata; no download URL is provided. Supply this file manually: {path}", path);
                Interlocked.Increment(ref reused);
                Interlocked.Increment(ref completed);
                Report(stage, path, new FileInfo(path).Length, size, true, displayName);
                return;
            }

            if (!await DownloadService.ValidAsync(path, sha1, size, token).ConfigureAwait(false)
                && await sharedCache.TryReuseAsync(path, sha1, size, token).ConfigureAwait(false))
            {
                Interlocked.Increment(ref sharedReused);
                Interlocked.Increment(ref reused);
                Interlocked.Increment(ref completed);
                Report(stage, path, new FileInfo(path).Length, size, true, displayName);
                return;
            }

            var bridge = progress is null ? null : new InlineProgress<DownloadProgress>(item => Report(stage, item.Path, item.Bytes, item.TotalBytes, item.Reused, displayName));
            if (await _downloads.DownloadAsync(uri: new Uri(url), destination: path, sha1: sha1, size: size, progress: bridge, cancellationToken: token).ConfigureAwait(false))
                Interlocked.Increment(ref downloaded);
            else
                Interlocked.Increment(ref reused);
            Interlocked.Increment(ref completed);
            Report(stage, path, new FileInfo(path).Length, size, false, displayName);
        }

        VersionCatalog? manifest = null;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task PrepareMetadata(string id)
        {
            ValidateId(id);
            if (!visited.Add(id))
                throw new InvalidDataException($"Version inheritance cycle at {id}");
            var path = SafePath(minecraftDirectory, $"versions/{id}/{id}.json");
            if (!File.Exists(path))
            {
                manifest ??= await GetVersionsAsync(offline: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                var entry = manifest.Versions?.FirstOrDefault(item => item?["id"]?.GetValue<string>() == id);
                if (entry is null)
                {
                    manifest = await GetVersionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    entry = manifest.Versions?.FirstOrDefault(item => item?["id"]?.GetValue<string>() == id);
                }

                if (entry is null) throw new InvalidDataException($"Version '{id}' is unavailable in the cached or remote manifest; supply its local version JSON.");
                await Fetch("metadata", path, entry, cancellationToken).ConfigureAwait(false);
            }

            var metadata = JsonFiles.ReadObject(path);
            if (metadata["inheritsFrom"]?.GetValue<string>() is { Length: > 0 } parent)
                await PrepareMetadata(parent).ConfigureAwait(false);
            visited.Remove(id);
        }

        await PrepareMetadata(versionId).ConfigureAwait(false);
        var version = await new VersionResolver().ResolveAsync(minecraftDirectory, versionId, cancellationToken).ConfigureAwait(false);
        var planned = new List<(string Stage, string Path, JsonNode Descriptor, string? Url)>();
        Task PlanFetch(string stage, string path, JsonNode descriptor, string? url = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            planned.Add((stage, path, descriptor, url));
            return Task.CompletedTask;
        }

        var jarId = version["_clientJarVersion"]?.GetValue<string>() ?? versionId;
        ValidateId(jarId);
        await SharedRuntimeLayout.EnsureClientSlotAsync(_paths, minecraftDirectory, jarId, cancellationToken).ConfigureAwait(false);
        var ownerMetadataPath = SafePath(minecraftDirectory, $"versions/{jarId}/{jarId}.json");
        var ownerMetadata = File.Exists(ownerMetadataPath) ? JsonFiles.ReadObject(ownerMetadataPath) : null;
        // Explicit jar aliases can reference hand-built client jars; the parent's SHA1 describes the parent jar only.
        var client = ownerMetadata?["downloads"]?["client"];
        await PlanFetch("client", SafePath(minecraftDirectory, $"versions/{jarId}/{jarId}.jar"), client ?? new JsonObject()).ConfigureAwait(false);
        foreach (var library in version["libraries"] as JsonArray ?? [])
        {
            if (library is null || !MetadataRules.AllowsAnyJavaArchitecture(library["rules"]))
                continue;
            var downloads = library["downloads"];
            if (downloads?["artifact"] is { } artifact)
            {
                await PlanFetch("library", SafePath(minecraftDirectory, "libraries/" + artifact["path"]!.GetValue<string>()), artifact).ConfigureAwait(false);
            }
            else if (downloads is null && library["name"]?.GetValue<string>() is { } coordinate)
            {
                var relative = MetadataRules.MavenPath(coordinate);
                var path = SafePath(minecraftDirectory, "libraries/" + relative);
                var baseUrl = library["url"]?.GetValue<string>() ?? "https://libraries.minecraft.net/";
                await PlanFetch("library", path, library, baseUrl.TrimEnd('/') + "/" + relative).ConfigureAwait(false);
            }

            if (library["natives"]?["windows"]?.GetValue<string>() is { } classifier)
            {
                var templated = classifier.Contains("${arch}", StringComparison.Ordinal);
                var classifiers = templated ? new[]
                {
                    classifier.Replace("${arch}", "32", StringComparison.Ordinal),
                    classifier.Replace("${arch}", "64", StringComparison.Ordinal)
                }

                :
                [
                    classifier
                ];
                var prepared = false;
                foreach (var candidate in classifiers)
                {
                    if (downloads?["classifiers"]?[candidate] is { } native)
                    {
                        await PlanFetch("native", SafePath(minecraftDirectory, "libraries/" + native["path"]!.GetValue<string>()), native).ConfigureAwait(false);
                        prepared = true;
                    }
                    else if (downloads is null && library["name"]?.GetValue<string>() is { } nativeCoordinate)
                    {
                        var parts = nativeCoordinate.Split('@', 2);
                        var coordinateWithClassifier = parts[0] + ":" + candidate + (parts.Length > 1 ? "@" + parts[1] : string.Empty);
                        var relative = MetadataRules.MavenPath(coordinateWithClassifier);
                        var path = SafePath(minecraftDirectory, "libraries/" + relative);
                        // For an arch template, metadata must provide the download or the file must already exist.
                        if (templated)
                        {
                            if (File.Exists(path))
                            {
                                await PlanFetch("native", path, new JsonObject()).ConfigureAwait(false);
                                prepared = true;
                            }
                        }
                        else
                        {
                            var baseUrl = library["url"]?.GetValue<string>() ?? "https://libraries.minecraft.net/";
                            await PlanFetch("native", path, new JsonObject(), baseUrl.TrimEnd('/') + "/" + relative).ConfigureAwait(false);
                            prepared = true;
                        }
                    }
                }

                if (templated && !prepared)
                    throw new FileNotFoundException($"Native architecture template '{classifier}' has no listed download or existing local archive. Supply the selected Java's native archive manually.");
            }
        }

        JsonObject? index = null;
        string? assetIndexId = null;
        if (version["assetIndex"] is { } assetIndex)
        {
            assetIndexId = assetIndex["id"]!.GetValue<string>();
            ValidateId(assetIndexId);
            var indexPath = SafePath(minecraftDirectory, $"assets/indexes/{assetIndexId}.json");
            await Fetch("assetIndex", indexPath, assetIndex, cancellationToken).ConfigureAwait(false);
            index = JsonFiles.ReadObject(indexPath);
        }

        if (version["logging"]?["client"]?["file"] is { } logging)
        {
            var id = logging["id"]!.GetValue<string>();
            ValidateId(id);
            await PlanFetch("logging", SafePath(minecraftDirectory, "assets/log_configs/" + id), logging).ConfigureAwait(false);
        }

        totalFiles = completed + planned.Count + (index?["objects"] as JsonObject ?? []).Count(entry => entry.Value is not null);
        Report("planned", minecraftDirectory, 0, null, false);
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = NetworkSettings.Concurrency(_paths, 8), CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(planned, parallelOptions, async (file, token) =>
            await Fetch(file.Stage, file.Path, file.Descriptor, token, file.Url).ConfigureAwait(false)).ConfigureAwait(false);
        if (index is not null)
        {
            var id = assetIndexId!;
            await Parallel.ForEachAsync((index["objects"] as JsonObject ?? []).ToArray(), parallelOptions, async (entry, token) =>
            {
                var (name, item) = entry;
                token.ThrowIfCancellationRequested();
                if (item is null)
                    return;
                var hash = item["hash"]!.GetValue<string>();
                if (hash.Length != 40 || !hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"Invalid asset hash: {name}");
                var relative = hash[..2] + "/" + hash;
                await Fetch("asset", SafePath(minecraftDirectory, "assets/objects/" + relative), new JsonObject { ["sha1"] = hash, ["size"] = item["size"]?.DeepClone() }, token, "https://resources.download.minecraft.net/" + relative, name).ConfigureAwait(false);
                // Legacy clients consume a virtual tree, and map_to_resources clients consume resources/.
                foreach (var prefix in new[]
                {
                    index["virtual"]?.GetValue<bool>() == true ? $"assets/virtual/{id}/" : null,
                    index["map_to_resources"]?.GetValue<bool>() == true ? "resources/" : null
                }

                )
                {
                    if (prefix is null)
                        continue;
                    var target = SafePath(minecraftDirectory, prefix + name);
                    await CopyAssetAsync(SafePath(minecraftDirectory, "assets/objects/" + relative), target, hash, token).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        progress?.Report(new("publishing", minecraftDirectory, 0, null, false, completed, completed, sharedReused));
        await SharedRuntimeLayout.SealAsync(_paths, minecraftDirectory, cancellationToken).ConfigureAwait(false);
        await JsonFiles.WriteAsync(journal, new JsonObject { ["version"] = versionId, ["state"] = "complete", ["downloaded"] = downloaded, ["reused"] = reused, ["nativeArchivesPrepared"] = true, ["nativeExtraction"] = "launch-preparation" }, cancellationToken).ConfigureAwait(false);
        return new InstallationResult
        {
            Id = versionId,
            MinecraftDirectory = minecraftDirectory,
            Downloaded = downloaded,
            Reused = reused,
            NativeArchivesPrepared = true,
            CompletedFiles = completed,
            TotalFiles = totalFiles,
            SharedReusedFiles = sharedReused,
            CacheReadFailures = sharedCache.UnavailableCandidates
        };
    }

    private async Task CopyAssetAsync(string source, string target, string hash, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: PhysicalPath(target), cancellationToken: ct).ConfigureAwait(false);
        if (await DownloadService.ValidAsync(target, hash, null, ct).ConfigureAwait(false))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            {
                var input = File.OpenRead(source);
                await using (input.ConfigureAwait(false))
                {
                    var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await using (output.ConfigureAwait(false))
                    {
                        await input.CopyToAsync(output, ct).ConfigureAwait(false);
                        await output.FlushAsync(ct).ConfigureAwait(false);
                        output.Flush(true);
                    }
                }
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
            await DownloadOwnership.RecordAsync(_paths, target, ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Invalid metadata ID: {id}");
    }

    private static string SafePath(string root, string relative)
    {
        (root, relative) = SharedRuntimeLayout.Route(root, relative);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Metadata path escapes Minecraft directory: {relative}");
        // A player-created libraries/assets junction is an intentional shared storage location.
        return PhysicalPath(full);
    }

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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}