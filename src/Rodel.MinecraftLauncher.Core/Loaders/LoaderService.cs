// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rodel.MinecraftLauncher.Core.Loaders;

public sealed record LoaderProgress(string Stage, string Message, int? ProcessorIndex = null, int? ProcessorCount = null)
{
    /// <summary>Gets measured phase completion for the complete loader installation, including its base game.</summary>
    public double? OverallPercent { get; init; }
}

/// <summary>Installs loader metadata and libraries, executing Forge processors in separate Java processes.</summary>
public sealed partial class LoaderService
{
    private readonly LauncherPaths _paths;
    private readonly HttpClient _client;
    private readonly DownloadService _downloads;
    private readonly string[] _libraryMirrors;
    public LoaderService(LauncherPaths paths, HttpClient? httpClient = null, IEnumerable<string>? libraryMirrors = null)
    {
        _paths = paths;
        _client = httpClient ?? NetworkSettings.Client(paths, TimeSpan.FromMinutes(10));
        _downloads = new(paths, _client);
        _libraryMirrors = [.. (libraryMirrors ?? []).Select(value => new Uri(value, UriKind.Absolute).AbsoluteUri.TrimEnd('/') + "/")];
    }

    /// <summary>Reads loader version candidates for the selected Minecraft version, honoring explicit offline catalog use.</summary>
    public async Task<LoaderCatalog> ListAsync(string loader, string minecraftVersion, bool offline = false, CancellationToken cancellationToken = default)
    {
        loader = Normalize(loader);
        ValidateId(minecraftVersion);
        var cachePath = SafePath(_paths.CacheDirectory, $"loader-catalogs/{loader}/{minecraftVersion}.json");
        JsonObject? cached = null;
        string? cacheWarning = null;
        if (File.Exists(cachePath))
        {
            try
            {
                cached = JsonFiles.ReadObject(cachePath);
                if (Text(cached, "loader") != loader || Text(cached, "minecraftVersion") != minecraftVersion || cached["versions"] is not JsonArray versions)
                    throw new InvalidDataException("Cached catalog does not match the requested loader and Minecraft version.");
                foreach (var entry in versions)
                {
                    if (entry is not JsonObject item || Text(item, "version") is not { } version || Text(item, "minecraftVersion") != minecraftVersion)
                        throw new InvalidDataException("Cached catalog contains an unreadable or mismatched version entry.");
                    ValidateId(version);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException or ArgumentException)
            {
                cached = null;
                cacheWarning = $"Cached loader catalog is unreadable ({cachePath}): {ex.Message}";
            }
        }

        if (offline)
        {
            if (cached is null)
                throw CatalogUnavailable("No readable cached loader catalog is available offline.", cacheWarning);
            return Describe(cached, "cache", null);
        }

        JsonObject catalog;
        try
        {
            catalog = await FetchCatalogAsync(loader, minecraftVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or System.Xml.XmlException or InvalidOperationException or FormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cached is null)
                throw CatalogUnavailable(ex.Message, cacheWarning);
            return Describe(cached, "cache-fallback", "The loader catalog request failed; cached versions are being used: " + ex.Message);
        }

        catalog["cachedAt"] = DateTimeOffset.UtcNow.ToString("O");
        try
        {
            await JsonFiles.WriteAsync(cachePath, catalog, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cacheWarning = "Fresh loader versions were fetched, but their cache could not be saved: " + ex.Message;
        }

        return Describe(catalog, "network", cacheWarning);
        LoaderCatalog Describe(JsonObject data, string source, string? warning)
        {
            return new()
            {
                Loader = loader,
                MinecraftVersion = minecraftVersion,
                Versions = [.. data["versions"]!.AsArray().Select(item => (JsonObject)item!.DeepClone())],
                Source = source,
                Offline = offline,
                CachePath = cachePath,
                CatalogUrl = CatalogUrl(loader, minecraftVersion),
                Warning = warning,
                AdditionalMetadata = StoredReportFields.Remaining(new JsonObject(data.Where(pair => pair.Key is not ("loader" or "minecraftVersion" or "versions" or "source" or "offline" or "cachePath" or "catalogUrl" or "warning"))
                    .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))))
            };
        }

        Rodel.MinecraftLauncher.Core.Application.LauncherOperationException CatalogUnavailable(string failure, string? cacheFailure) => new("LOADER_CATALOG_UNAVAILABLE", failure, new JsonObject { ["loader"] = loader, ["minecraftVersion"] = minecraftVersion, ["offline"] = offline, ["catalogUrl"] = CatalogUrl(loader, minecraftVersion), ["cachePath"] = cachePath, ["cacheWarning"] = cacheFailure, ["recovery"] = "Retry the catalog request, or install an explicitly selected loader version without consulting this catalog." });
    }

    private static string CatalogUrl(string loader, string minecraftVersion) => loader switch
    {
        "fabric" => $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(minecraftVersion)}",
        "quilt" => $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(minecraftVersion)}",
        "forge" => "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
        _ => $"https://maven.neoforged.net/releases/net/neoforged/{(minecraftVersion == "1.20.1" ? "forge" : "neoforge")}/maven-metadata.xml"
    };
    private async Task<JsonObject> FetchCatalogAsync(string loader, string minecraftVersion, CancellationToken cancellationToken)
    {
        loader = Normalize(loader);
        ValidateId(minecraftVersion);
        var entries = new List<JsonObject>();
        if (loader is "fabric" or "quilt")
        {
            var host = loader == "fabric" ? "https://meta.fabricmc.net/v2" : "https://meta.quiltmc.org/v3";
            var response = await GetJsonAsync($"{host}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}", cancellationToken).ConfigureAwait(false);
            if (response is not JsonArray array)
                throw new InvalidDataException("Loader catalog must be an array.");
            foreach (var entry in array)
            {
                if (entry?["loader"] is not JsonObject metadata || Text(metadata, "version") is not { } version)
                    continue;
                var item = (JsonObject)metadata.DeepClone();
                item["minecraftVersion"] = minecraftVersion;
                item["stable"] ??= !version.Contains('-');
                item["profileUrl"] = $"{host}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(version)}/profile/json";
                entries.Add(item);
            }
        }
        else if (loader == "forge")
        {
            var xml = await GetTextAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", cancellationToken).ConfigureAwait(false);
            var document = XDocument.Parse(xml);
            foreach (var element in document.Descendants("version"))
            {
                var fullVersion = element.Value;
                if (!fullVersion.StartsWith(minecraftVersion + "-", StringComparison.Ordinal))
                    continue;
                var version = fullVersion[(minecraftVersion.Length + 1)..];
                entries.Add(new JsonObject { ["version"] = version, ["mavenVersion"] = fullVersion, ["minecraftVersion"] = minecraftVersion, ["stable"] = !version.Contains("beta", StringComparison.OrdinalIgnoreCase), ["installerUrl"] = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{fullVersion}/forge-{fullVersion}-installer.jar" });
            }
        }
        else
        {
            var artifact = minecraftVersion == "1.20.1" ? "forge" : "neoforge";
            var xml = await GetTextAsync(CatalogUrl(loader, minecraftVersion), cancellationToken).ConfigureAwait(false);
            var document = XDocument.Parse(xml);
            foreach (var item in document.Descendants("version"))
            {
                var fullVersion = item.Value;
                if (artifact == "forge" && !fullVersion.StartsWith(minecraftVersion + "-", StringComparison.Ordinal))
                    continue;
                var gameVersion = artifact == "forge" ? "1.20.1" : NeoForgeGameVersion(fullVersion);
                if (gameVersion != minecraftVersion)
                    continue;
                var version = artifact == "forge" && fullVersion.StartsWith("1.20.1-", StringComparison.Ordinal) ? fullVersion[7..] : fullVersion;
                entries.Add(new JsonObject { ["version"] = version, ["mavenVersion"] = fullVersion, ["minecraftVersion"] = gameVersion, ["stable"] = !fullVersion.Contains("beta", StringComparison.OrdinalIgnoreCase), ["installerUrl"] = $"https://maven.neoforged.net/releases/net/neoforged/{artifact}/{fullVersion}/{artifact}-{fullVersion}-installer.jar" });
            }
        }

        var ordered = entries.OrderByDescending(x => Text(x, "version"), VersionComparer.Instance).ToArray();
        return new JsonObject
        {
            ["loader"] = loader,
            ["minecraftVersion"] = minecraftVersion,
            ["versions"] = new JsonArray(ordered.Select(x => (JsonNode)x).ToArray())
        };
    }

    public async Task<LoaderInstallationResult> InstallAsync(string minecraftDirectory, string minecraftVersion, string loader, string? loaderVersion = null, string? javaPath = null, IProgress<LoaderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        minecraftDirectory = Path.GetFullPath(minecraftDirectory);
        using var cacheUse = await CacheActivity.EnterAsync(_paths, cancellationToken).ConfigureAwait(false);
        ValidateId(minecraftVersion);
        loader = Normalize(loader);
        JsonObject? selected;
        if (loaderVersion is null)
        {
            var catalog = await ListAsync(loader: loader, minecraftVersion: minecraftVersion, cancellationToken: cancellationToken).ConfigureAwait(false);
            var candidates = catalog.Versions;
            selected = candidates.FirstOrDefault(x => x["stable"]?.GetValue<bool>() == true) ?? candidates.FirstOrDefault();
        }
        else
        {
            selected = ExplicitDescriptor(loader, minecraftVersion, loaderVersion);
        }

        if (selected is null)
            throw new InvalidDataException($"No {loader} builds were returned for Minecraft {minecraftVersion}.");
        loaderVersion = Text(selected, "version")!;
        ValidateId(loaderVersion);
        var versionId = $"{minecraftVersion}-{loader}-{loaderVersion}";
        ValidateId(versionId);
        var workProgress = new LoaderInstallationProgress(progress, loader is "forge" or "neoforge" ? 5 : 4);
        progress = workProgress;
        progress?.Report(new("base", $"Preparing vanilla {minecraftVersion}."));
        await new InstallationService(_paths, _client).InstallAsync(minecraftDirectory, minecraftVersion, progress: workProgress, cancellationToken: cancellationToken).ConfigureAwait(false);
        workProgress.CompletePhase("profile", "Preparing loader metadata.");
        using var lease = await ResourceLease.AcquireAsync(resource: minecraftDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        await SharedRuntimeLayout.EnsureWritableAsync(_paths, minecraftDirectory, cancellationToken).ConfigureAwait(false);
        await SharedRuntimeLayout.EnsureClientSlotAsync(_paths, minecraftDirectory, versionId, cancellationToken).ConfigureAwait(false);
        var work = SafePath(minecraftDirectory, $".rodel/loaders/{versionId}");
        var staging = SafePath(minecraftDirectory, $".rodel/loaders/{versionId}/staging");
        Directory.CreateDirectory(staging);
        var journalPath = Path.Combine(work, "state.json");
        var refreshInstallerChecksum = false;
        if (File.Exists(journalPath))
        {
            try
            {
                refreshInstallerChecksum = Text(JsonFiles.ReadObject(journalPath), "state") == "failed";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                refreshInstallerChecksum = true;
            }
        }

        var journal = new JsonObject
        {
            ["loader"] = loader,
            ["loaderVersion"] = loaderVersion,
            ["minecraftVersion"] = minecraftVersion,
            ["versionId"] = versionId,
            ["state"] = "preparing",
            ["stagingDirectory"] = staging,
            ["committedFiles"] = new JsonArray(),
            ["recovery"] = "retry-install"
        };
        await JsonFiles.WriteAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();
        var processorsRun = 0;
        var profileSource = "installer";
        var preserveLocalProfile = false;
        var rollback = new List<CommittedFile>();
        var installationCompleted = false;
        try
        {
            JsonObject profile;
            if (loader is "fabric" or "quilt")
            {
                var existingProfilePath = SafePath(minecraftDirectory, $"versions/{versionId}/{versionId}.json");
                JsonObject? existingProfile = null;
                if (File.Exists(existingProfilePath))
                {
                    try
                    {
                        var candidate = JsonFiles.ReadObject(existingProfilePath);
                        if (Text(candidate, "id") == versionId && Text(candidate, "inheritsFrom") == minecraftVersion && !string.IsNullOrWhiteSpace(Text(candidate, "mainClass")) && candidate["libraries"] is JsonArray)
                            existingProfile = candidate;
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
                    {
                        progress?.Report(new("profile", "Local loader profile is unreadable; fetching its exact selected version."));
                    }
                }

                if (existingProfile is not null)
                {
                    profile = existingProfile;
                    preserveLocalProfile = true;
                    profileSource = "local";
                    progress?.Report(new("profile", $"Reusing local {loader} {loaderVersion} profile for Minecraft {minecraftVersion}."));
                }
                else
                {
                    profileSource = "network";
                    profile = await GetJsonAsync(Text(selected, "profileUrl")!, cancellationToken).ConfigureAwait(false) as JsonObject ?? throw new InvalidDataException("Loader profile must be an object.");
                }

                if (Text(profile, "inheritsFrom") is string inherited && inherited != minecraftVersion)
                    throw new InvalidDataException($"Loader profile inherits {inherited}, but Minecraft {minecraftVersion} was selected.");
                profile["inheritsFrom"] ??= minecraftVersion;
                profile["id"] = versionId;
                workProgress.CompletePhase("library", "Preparing loader libraries.");
                await PrepareLibrariesAsync(profile["libraries"], minecraftDirectory, staging, null, progress, cancellationToken).ConfigureAwait(false);
                workProgress.CompletePhase("publishing", "Publishing loader files.");
            }
            else
            {
                var installerUrl = Text(selected, "installerUrl")!;
                var installer = SafePath(_paths.CacheDirectory, $"loaders/{loader}/{minecraftVersion}-{loaderVersion}-installer.jar");
                var sha1Path = installer + ".sha1";
                var cachedSha1 = File.Exists(sha1Path) ? (await File.ReadAllTextAsync(sha1Path, cancellationToken).ConfigureAwait(false)).Trim() : null;
                if (cachedSha1 is not { Length: 40 } || !cachedSha1.All(Uri.IsHexDigit))
                    cachedSha1 = null;
                var sha1 = cachedSha1;
                if (sha1 is null || refreshInstallerChecksum)
                    sha1 = await TrySha1Async(installerUrl + ".sha1", cancellationToken).ConfigureAwait(false) ?? cachedSha1;
                await _downloads.DownloadAsync(uri: new Uri(installerUrl), destination: installer, sha1: sha1, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (sha1 is not null && (cachedSha1 != sha1 || !File.Exists(sha1Path)))
                {
                    var checksumStream = new MemoryStream(Encoding.ASCII.GetBytes(sha1));
                    await using var checksumStreamDisposal = checksumStream.ConfigureAwait(false);
                    await WriteAtomicAsync(checksumStream, sha1Path, cancellationToken).ConfigureAwait(false);
                }

                if (sha1 is null)
                    warnings.Add("The installer SHA1 sidecar was unavailable; archive metadata and individual library/output checks still apply.");
                progress?.Report(new("installer", installer));
                using var archive = ZipFile.OpenRead(installer);
                var install = await ReadArchiveJsonAsync(archive, "install_profile.json", cancellationToken).ConfigureAwait(false);
                var mcpConfigs = (install["libraries"] as JsonArray ?? []).OfType<JsonObject>().Select(library => library["name"]?.GetValue<string>())
                    .Where(name => name?.StartsWith("de.oceanlabs.mcp:mcp_config:", StringComparison.Ordinal) == true).Distinct().ToArray();
                if (Text(install, "minecraft") is string declared && declared != minecraftVersion)
                    throw new InvalidDataException($"Installer is for Minecraft {declared}, but {minecraftVersion} was selected.");
                if (install["install"] is JsonObject legacy)
                {
                    var versionInfo = install["versionInfo"];
                    profile = versionInfo is JsonObject info ? (JsonObject)info.DeepClone() : JsonNode.Parse(versionInfo?.GetValue<string>() ?? string.Empty) as JsonObject ?? throw new InvalidDataException("Legacy Forge installer has no versionInfo.");
                    var coordinate = Text(legacy, "path") ?? throw new InvalidDataException("Legacy Forge installer has no universal coordinate.");
                    var entry = Text(legacy, "filePath") ?? throw new InvalidDataException("Legacy Forge installer has no universal file.");
                    workProgress.CompletePhase("library", "Preparing loader libraries.");
                    await ExtractAtomicAsync(archive, entry, SafePath(staging, "libraries/" + MetadataRules.MavenPath(coordinate)), cancellationToken).ConfigureAwait(false);
                    await PrepareLibrariesAsync(profile["libraries"], minecraftDirectory, staging, archive, progress, cancellationToken).ConfigureAwait(false);
                    workProgress.CompletePhase("processor", "No processors required by this installer.");
                }
                else
                {
                    profile = await ReadArchiveJsonAsync(archive, Text(install, "json") ?? "version.json", cancellationToken).ConfigureAwait(false);
                    workProgress.CompletePhase("library", "Preparing loader libraries.");
                    await PrepareLibrariesAsync(install["libraries"], minecraftDirectory, staging, archive, progress, cancellationToken).ConfigureAwait(false);
                    await PrepareLibrariesAsync(profile["libraries"], minecraftDirectory, staging, archive, progress, cancellationToken).ConfigureAwait(false);
                    workProgress.CompletePhase("processor", "Preparing loader processors.");
                    // Some profiles carry their own Maven artifact separately from the library list.
                    if (Text(install, "path") is string mainCoordinate)
                    {
                        var relative = MetadataRules.MavenPath(mainCoordinate);
                        if (archive.GetEntry("maven/" + relative) is not null)
                            await ExtractAtomicAsync(archive, "maven/" + relative, SafePath(staging, "libraries/" + relative), cancellationToken).ConfigureAwait(false);
                    }

                    var baseVersion = await new VersionResolver().ResolveAsync(minecraftDirectory, minecraftVersion, cancellationToken).ConfigureAwait(false);
                    var owner = Text(baseVersion, "_clientJarVersion") ?? minecraftVersion;
                    var stagedClient = SafePath(staging, $"versions/{owner}/{owner}.jar");
                    await CopyAtomicAsync(SafePath(minecraftDirectory, $"versions/{owner}/{owner}.jar"), stagedClient, cancellationToken).ConfigureAwait(false);
                    var java = await SelectJavaAsync(javaPath, minecraftDirectory, baseVersion["javaVersion"]?["majorVersion"]?.GetValue<int>(), warnings, cancellationToken).ConfigureAwait(false);
                    processorsRun = await RunProcessorsAsync(install, archive, installer, staging, stagedClient, minecraftVersion, java, baseVersion, warnings, journalPath, journal, progress, cancellationToken).ConfigureAwait(false);
                }

                workProgress.CompletePhase("publishing", "Publishing loader files.");
                profile["id"] = versionId;
                if (mcpConfigs.Length == 1) profile["_rodelMcpConfig"] = mcpConfigs[0];
                profile["inheritsFrom"] ??= minecraftVersion;
                // Old installers may repeat the vanilla jar download; never make the loader own that jar.
                profile["jar"] ??= minecraftVersion;
            }

            if (Text(profile, "mainClass") is null)
                throw new InvalidDataException("Loader profile has no mainClass.");
            foreach (var library in profile["libraries"] as JsonArray ?? [])
            {
                if (library is null || !MetadataRules.Allows(library["rules"]))
                    continue;
                var relative = Text(library["downloads"]?["artifact"], "path") ?? MetadataRules.MavenPath(Text(library, "name")!);
                if (!File.Exists(SafePath(staging, "libraries/" + relative)) && !File.Exists(SafePath(minecraftDirectory, "libraries/" + relative)))
                    throw new FileNotFoundException("Loader installation did not produce a required library.", relative);
            }

            journal["state"] = "committing";
            await JsonFiles.WriteAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            var committed = journal["committedFiles"]!.AsArray();
            var libraryRoot = Path.Combine(staging, "libraries");
            if (Directory.Exists(libraryRoot))
            {
                foreach (var file in Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".rodel.partial", StringComparison.Ordinal) || file.EndsWith(".rodel.partial.json", StringComparison.Ordinal) || file.EndsWith(".tmp", StringComparison.Ordinal))
                        continue;
                    var relative = Path.GetRelativePath(staging, file);
                    var destination = SafePath(minecraftDirectory, relative);
                    await CommitFileAsync(file, destination, work, rollback, cancellationToken).ConfigureAwait(false);
                    await DownloadOwnership.RecordAsync(_paths, destination, cancellationToken).ConfigureAwait(false);
                    committed.Add((JsonNode?)JsonValue.Create(destination));
                    await JsonFiles.WriteAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
                }
            }

            var metadataPath = SafePath(minecraftDirectory, $"versions/{versionId}/{versionId}.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var previous = JsonFiles.ReadObject(metadataPath);
                    foreach (var (key, value) in previous)
                    {
                        if (!profile.ContainsKey(key))
                            profile[key] = value?.DeepClone();
                    }
                }
                catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException)
                {
                    progress?.Report(new("profile", "Replacing malformed loader metadata; its original bytes are retained in previous-version.json."));
                }

                var backup = Path.Combine(work, "previous-version.json");
                if (!File.Exists(backup))
                    await CopyAtomicAsync(metadataPath, backup, cancellationToken).ConfigureAwait(false);
            }

            if (!preserveLocalProfile)
            {
                var stagedMetadata = SafePath(staging, $"versions/{versionId}/{versionId}.json");
                await JsonFiles.WriteAsync(stagedMetadata, profile, cancellationToken).ConfigureAwait(false);
                await CommitFileAsync(stagedMetadata, metadataPath, work, rollback, cancellationToken).ConfigureAwait(false);
                committed.Add((JsonNode?)JsonValue.Create(metadataPath));
            }

            journal["state"] = "completed";
            journal["processorsRun"] = processorsRun;
            await JsonFiles.WriteAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            progress?.Report(new("completed", versionId));
            installationCompleted = true;
            return new(versionId, minecraftVersion, loader, loaderVersion, profileSource, processorsRun,
                [.. committed.Select(path => path!.GetValue<string>())], [.. warnings], journalPath);
        }
        catch (Exception exception)
        {
            var recoveryWarnings = await RollbackFilesAsync(rollback).ConfigureAwait(false);
            journal["state"] = cancellationToken.IsCancellationRequested ? "cancelled" : "failed";
            journal["error"] = exception.Message;
            journal["rollback"] = recoveryWarnings.Count == 0 ? "completed" : "needs-review";
            journal["rollbackWarnings"] = new JsonArray([.. recoveryWarnings.Select(value => (JsonNode?)JsonValue.Create(value))]);
            try
            {
                await JsonFiles.WriteAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException)
            {
                recoveryWarnings.Add("The recovery journal could not be saved: " + journalError.Message);
            }

            if (recoveryWarnings.Count > 0)
            {
                var message = exception.Message + " Recovery requires review: " + string.Join(" ", recoveryWarnings);
                if (exception is OperationCanceledException)
                    throw new OperationCanceledException(message, exception, cancellationToken);
                throw new IOException(message, exception);
            }

            throw;
        }
        finally
        {
            // These copies belong to this attempt only. Retain failed recovery copies for manual repair.
            foreach (var file in rollback.Where(value => value.Restored || installationCompleted))
            {
                if (file.Backup is not null)
                {
                    try { File.Delete(file.Backup); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    private async Task PrepareLibrariesAsync(JsonNode? libraries, string minecraftDirectory, string staging, ZipArchive? archive, IProgress<LoaderProgress>? progress, CancellationToken ct)
    {
        await Parallel.ForEachAsync((libraries as JsonArray ?? []).ToArray(), new ParallelOptions { MaxDegreeOfParallelism = archive is null ? 4 : 1, CancellationToken = ct }, async (library, token) =>
        {
            if (library is null || !MetadataRules.Allows(library["rules"]))
                return;
            var artifact = library["downloads"]?["artifact"];
            var relative = Text(artifact, "path") ?? MetadataRules.MavenPath(Text(library, "name") ?? throw new InvalidDataException("Loader library has no coordinate or path."));
            var destination = SafePath(staging, "libraries/" + relative);
            var sha1 = Text(artifact, "sha1") ?? Text(library, "sha1");
            var size = artifact?["size"]?.GetValue<long>();
            if (await DownloadService.ValidAsync(destination, sha1, size, token).ConfigureAwait(false))
                return;
            if (archive?.GetEntry("maven/" + relative) is not null)
            {
                await ExtractAtomicAsync(archive, "maven/" + relative, destination, token).ConfigureAwait(false);
                if (!await DownloadService.ValidAsync(destination, sha1, size, token).ConfigureAwait(false))
                    throw new InvalidDataException($"Embedded library checksum mismatch: {relative}");
                return;
            }

            var existing = SafePath(minecraftDirectory, "libraries/" + relative);
            if (await DownloadService.ValidAsync(existing, sha1, size, token).ConfigureAwait(false))
            {
                await CopyAtomicAsync(existing, destination, token).ConfigureAwait(false);
                return;
            }

            var url = Text(artifact, "url");
            // An empty URL denotes a processor output, not a network artifact.
            if (url == string.Empty)
                return;
            url ??= (Text(library, "url") ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/" + relative;
            var triedMirrors = new HashSet<string>(StringComparer.Ordinal);
            if (sha1 is { Length: 40 })
            {
                foreach (var mirrorRoot in _libraryMirrors)
                {
                    var mirrorUrl = mirrorRoot + relative;
                    if (mirrorUrl == url || !triedMirrors.Add(mirrorUrl))
                        continue;
                    progress?.Report(new("library-mirror", mirrorUrl));
                    try
                    {
                        await _downloads.DownloadAsync(uri: new Uri(mirrorUrl), destination: destination, sha1: sha1, size: size, cancellationToken: token).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception mirrorError) when (!token.IsCancellationRequested && mirrorError is IOException or HttpRequestException or OperationCanceledException)
                    { /* A configured mirror failure falls back to the profile's original source. */
                    }
                }
            }

            progress?.Report(new("library", relative));
            try
            {
                await _downloads.DownloadAsync(uri: new Uri(url), destination: destination, sha1: sha1, size: size, cancellationToken: token).ConfigureAwait(false);
            }
            catch (Exception error) when (sha1 is { Length: 40 } && (error is IOException or HttpRequestException || (error is OperationCanceledException && !token.IsCancellationRequested)))
            {
                // Maven Central can serve the same transitive artifact when a loader mirror is unavailable.
                // A publisher-provided checksum is required so this never substitutes different content.
                var failures = new List<Exception>
                {
                    error
                };
                var alternates = _libraryMirrors.Select(root => root + relative).Append("https://repo.maven.apache.org/maven2/" + relative).Where(alternate => alternate != url && !triedMirrors.Contains(alternate)).Distinct(StringComparer.Ordinal);
                foreach (var alternate in alternates)
                {
                    progress?.Report(new("library-fallback", alternate));
                    try
                    {
                        await _downloads.DownloadAsync(uri: new Uri(alternate), destination: destination, sha1: sha1, size: size, cancellationToken: token).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception alternateError) when (!token.IsCancellationRequested && alternateError is IOException or HttpRequestException or OperationCanceledException)
                    {
                        failures.Add(alternateError);
                    }
                }

                throw new IOException($"Could not fetch {relative}. Profile source {url}: {error.Message}. " + $"Checksum-verified alternatives also failed. Place the matching artifact at {destination} and retry.", new AggregateException(failures));
            }
        }).ConfigureAwait(false);
    }

    private async Task<int> RunProcessorsAsync(JsonObject install, ZipArchive archive, string installer, string staging, string clientJar, string minecraftVersion, string javaPath, JsonObject baseVersion, List<string> warnings, string journalPath, JsonObject journal, IProgress<LoaderProgress>? progress, CancellationToken ct)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SIDE"] = "client",
            ["MINECRAFT_JAR"] = clientJar,
            ["MINECRAFT_VERSION"] = minecraftVersion,
            ["ROOT"] = staging,
            ["INSTALLER"] = installer,
            ["LIBRARY_DIR"] = Path.Combine(staging, "libraries")
        };
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, values) in install["data"] as JsonObject ?? [])
        {
            if (Text(values, "client") is string value)
                raw[name] = value;
        }

        var active = new HashSet<string>();
        string Resolve(string input)
        {
            if (input.StartsWith('[') && input.EndsWith(']'))
                return SafePath(staging, "libraries/" + MetadataRules.MavenPath(input[1..^1]));
            if (input.StartsWith('\'') && input.EndsWith('\''))
                return input[1..^1];
            return Regex.Replace(input, @"\{([^}]+)\}", match => Variable(match.Groups[1].Value), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        string Variable(string key)
        {
            if (variables.TryGetValue(key, out var value))
                return value;
            if (!raw.TryGetValue(key, out var literal))
                throw new InvalidDataException($"Installer refers to undefined data '{{{key}}}'.");
            if (!active.Add(key))
                throw new InvalidDataException($"Installer data cycle at {key}.");
            value = Resolve(literal);
            active.Remove(key);
            if (!literal.StartsWith('[') && !literal.StartsWith('\'') && !literal.StartsWith('{'))
            {
                var entryName = literal.TrimStart('/');
                if (archive.GetEntry(entryName) is null)
                    throw new InvalidDataException($"Installer data entry missing: {literal}");
                value = SafePath(staging, "installer-data/" + entryName);
                ExtractAtomicAsync(archive, entryName, value, ct).GetAwaiter().GetResult();
            }

            variables[key] = value;
            return value;
        }

        foreach (var name in raw.Keys)
            _ = Variable(name);
        var processors = (install["processors"] as JsonArray ?? []).OfType<JsonObject>().Where(x => x["sides"] is not JsonArray sides || sides.Any(side => side?.GetValue<string>() == "client")).ToArray();
        var runCount = 0;
        for (var index = 0; index < processors.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var processor = processors[index];
            var coordinate = Text(processor, "jar") ?? throw new InvalidDataException("Processor has no jar coordinate.");
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, hash) in processor["outputs"] as JsonObject ?? [])
            {
                var resolvedPath = Path.GetFullPath(Resolve(path), staging);
                EnsureInside(staging, resolvedPath);
                outputs[resolvedPath] = Resolve(hash!.GetValue<string>());
            }

            if (outputs.Count > 0)
            {
                var allValid = true;
                foreach (var (path, hash) in outputs)
                {
                    if (!await DownloadService.ValidAsync(path, hash, null, ct).ConfigureAwait(false))
                    {
                        allValid = false;
                        break;
                    }
                }

                if (allValid)
                {
                    progress?.Report(new("processor-reused", coordinate, index + 1, processors.Length));
                    continue;
                }
            }

            var args = (processor["args"] as JsonArray ?? []).Select(x => Resolve(x!.GetValue<string>())).ToArray();
            progress?.Report(new("processor", coordinate, index + 1, processors.Length));
            journal["state"] = "processing";
            journal["processorIndex"] = index + 1;
            journal["processorJar"] = coordinate;
            await JsonFiles.WriteAsync(journalPath, journal, ct).ConfigureAwait(false);
            // Use the exact local version descriptor for mappings instead of the processor's legacy metadata URL.
            var taskIndex = Array.IndexOf(args, "--task");
            if (taskIndex >= 0 && taskIndex + 1 < args.Length && args[taskIndex + 1] == "DOWNLOAD_MOJMAPS" && !args.Contains("--sanitize", StringComparer.Ordinal))
            {
                var outputIndex = Array.IndexOf(args, "--output");
                var mapping = baseVersion["downloads"]?["client_mappings"];
                if (outputIndex >= 0 && outputIndex + 1 < args.Length && Text(mapping, "url") is string url)
                {
                    EnsureInside(staging, Path.GetFullPath(args[outputIndex + 1], staging));
                    await _downloads.DownloadAsync(uri: new Uri(url), destination: args[outputIndex + 1], sha1: Text(mapping, "sha1"), size: mapping?["size"]?.GetValue<long>(), cancellationToken: ct).ConfigureAwait(false);
                    foreach (var (path, hash) in outputs)
                    {
                        if (!await DownloadService.ValidAsync(path, hash, null, ct).ConfigureAwait(false))
                            throw new InvalidDataException($"Mapping output does not match the installer SHA1: {path}");
                    }

                    continue;
                }
            }

            var jar = SafePath(staging, "libraries/" + MetadataRules.MavenPath(coordinate));
            var classpath = new List<string>
            {
                jar
            };
            foreach (var dependency in processor["classpath"] as JsonArray ?? [])
                classpath.Add(SafePath(staging, "libraries/" + MetadataRules.MavenPath(dependency!.GetValue<string>())));
            foreach (var path in classpath)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Processor classpath dependency is missing.", path);
            }

            var mainClass = ReadMainClass(jar);
            var start = new ProcessStartInfo(javaPath)
            {
                WorkingDirectory = staging,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("-cp");
            start.ArgumentList.Add(string.Join(Path.PathSeparator, classpath));
            start.ArgumentList.Add(mainClass);
            foreach (var argument in args)
                start.ArgumentList.Add(argument);
            var log = Path.Combine(Path.GetDirectoryName(journalPath)!, $"processor-{index + 1}.log");
            var exitCode = await ExecuteProcessorAsync(start, log, ct).ConfigureAwait(false);
            if (exitCode != 0)
            {
                var recovered = taskIndex >= 0 && taskIndex + 1 < args.Length && args[taskIndex + 1] == "DOWNLOAD_MOJMAPS" && args.Contains("--sanitize", StringComparer.Ordinal) && await TryNormalizeMappingsAsync(args, javaPath, classpath, staging, baseVersion, log, progress, ct).ConfigureAwait(false);
                if (!recovered)
                    throw new IOException($"{coordinate} processor exited {exitCode}; see {log} and any {log}.fallback logs.");
                warnings.Add($"{coordinate} initially exited {exitCode}; verified Mojang mappings were normalized with its publisher srgutils API in a separate Java process. Original diagnostics: {log}.stderr; fallback: {log}.fallback.");
            }

            runCount++;
            foreach (var (path, hash) in outputs)
            {
                if (!await DownloadService.ValidAsync(path, hash, null, ct).ConfigureAwait(false))
                    throw new InvalidDataException($"Processor output is missing or has an incorrect SHA1: {path}");
            }
        }

        return runCount;
    }

    private async Task<bool> TryNormalizeMappingsAsync(string[] arguments, string javaPath, IReadOnlyList<string> classpath, string staging, JsonObject baseVersion, string log, IProgress<LoaderProgress>? progress, CancellationToken ct)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }

        if (Option("--side") != "client" || Option("--output") is not string output)
            return false;
        var accepted = new HashSet<string>(StringComparer.Ordinal)
        {
            "--task",
            "--version",
            "--side",
            "--output",
            "--sanitize",
            "--format",
            "--skipIfExists"
        };
        if (arguments.Any(argument => argument.StartsWith("--", StringComparison.Ordinal) && !accepted.Contains(argument)))
            return false;
        var java = await new JavaDiscovery(_paths).ProbeAsync(javaPath, ct).ConfigureAwait(false);
        if (java.MajorVersion is < 11)
            return false;
        string? package = null;
        foreach (var jar in classpath)
        {
            using var archive = ZipFile.OpenRead(jar);
            if (archive.GetEntry("net/minecraftforge/srgutils/IMappingFile.class") is not null)
            {
                package = "net.minecraftforge.srgutils";
                break;
            }

            if (archive.GetEntry("net/neoforged/srgutils/IMappingFile.class") is not null)
            {
                package = "net.neoforged.srgutils";
                break;
            }
        }

        var mapping = baseVersion["downloads"]?["client_mappings"];
        var sha1 = Text(mapping, "sha1");
        if (package is null || sha1 is not { Length: 40 } || !sha1.All(Uri.IsHexDigit) || Text(mapping, "url") is not string url)
            return false;
        output = Path.GetFullPath(output, staging);
        EnsureInside(staging, output);
        var raw = SafePath(staging, "installer-data/mojang-client-mappings-raw.txt");
        progress?.Report(new("processor-fallback", "Original mapping processor failed; using verified raw mappings and the publisher's Java srgutils API."));
        await _downloads.DownloadAsync(uri: new Uri(url), destination: raw, sha1: sha1, size: mapping?["size"]?.GetValue<long>(), cancellationToken: ct).ConfigureAwait(false);
        var source = SafePath(staging, "installer-data/RodelMappingNormalizer.java");
        var code = $$"""
            import {{package}}.IMappingFile;
            public final class RodelMappingNormalizer {
                public static void main(String[] args) throws Exception {
                    IMappingFile mappings = IMappingFile.load(new java.io.File(args[0]));
                    mappings.write(java.nio.file.Path.of(args[1]), IMappingFile.Format.valueOf(args[2]), false);
                    System.out.println("Mappings normalized with publisher srgutils API");
                }
            }
            """;
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(code));
            await using (stream.ConfigureAwait(false))
                await WriteAtomicAsync(stream, source, ct).ConfigureAwait(false);
        }

        var start = new ProcessStartInfo(javaPath)
        {
            WorkingDirectory = staging,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[]
        {
            "--class-path",
            string.Join(Path.PathSeparator, classpath),
            source,
            raw,
            output,
            Option("--format") ?? "TSRG"
        }

        )
        {
            start.ArgumentList.Add(argument);
        }

        return await ExecuteProcessorAsync(start, log + ".fallback", ct).ConfigureAwait(false) == 0;
    }

    private async Task<string> SelectJavaAsync(string? explicitPath, string minecraftDirectory, int? recommended, List<string> warnings, CancellationToken ct)
    {
        var discovery = new JavaDiscovery(_paths);
        JavaInstallation? java;
        if (explicitPath is not null)
        {
            if (Directory.Exists(explicitPath))
                explicitPath = Path.Combine(explicitPath, "bin", "java.exe");
            java = await discovery.ProbeAsync(Path.GetFullPath(explicitPath), ct).ConfigureAwait(false);
        }
        else
        {
            var candidates = await discovery.DiscoverAsync(minecraftDirectory: minecraftDirectory, ct: ct).ConfigureAwait(false);
            java = candidates.FirstOrDefault(x => x.IsUsable && x.MajorVersion == recommended) ?? candidates.Where(x => x.IsUsable).OrderByDescending(x => x.MajorVersion).FirstOrDefault();
        }

        if (java?.IsUsable != true)
            throw new IOException("A working Java is required to run installer processors. Supply --java.");
        warnings.AddRange(java.Warnings);
        if (recommended is not null && java.MajorVersion != recommended)
            warnings.Add($"Minecraft recommends Java {recommended}; installer uses Java {java.MajorVersion}.");
        return java.Path;
    }

    private static async Task<int> ExecuteProcessorAsync(ProcessStartInfo start, string log, CancellationToken ct)
    {
        using var process = Process.Start(start) ?? throw new IOException("Installer processor did not start.");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        var stdoutFile = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var stdoutFileDisposal = stdoutFile.ConfigureAwait(false);
        var stderrFile = new FileStream(log + ".stderr", FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var stderrFileDisposal = stderrFile.ConfigureAwait(false);
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(stdoutFile, CancellationToken.None);
        var stderr = process.StandardError.BaseStream.CopyToAsync(stderrFile, CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return process.ExitCode;
    }

    private async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct)
    {
        return JsonNode.Parse(await GetTextAsync(url, ct).ConfigureAwait(false)) ?? throw new InvalidDataException($"Empty metadata: {url}");
    }

    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await DownloadRequests.GetBufferedAsync(_client, new Uri(url), NetworkSettings.MetadataTimeout(_paths), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 2 && (exception is HttpRequestException { StatusCode: null or System.Net.HttpStatusCode.TooManyRequests } || (exception is HttpRequestException http && (int?)http.StatusCode >= 500) || (exception is OperationCanceledException && !ct.IsCancellationRequested)))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
            {
                throw new HttpRequestException($"Loader metadata request timed out after limited retries: {url}", exception);
            }
            catch (HttpRequestException exception)
            {
                throw new HttpRequestException($"Loader metadata request failed ({url}): {exception.Message}", exception, exception.StatusCode);
            }
        }
    }

    private async Task<string?> TrySha1Async(string url, CancellationToken ct)
    {
        try
        {
            var text = (await GetTextAsync(url, ct).ConfigureAwait(false)).Trim().Split(' ', '\t', '\r', '\n')[0];
            return text.Length == 40 && text.All(Uri.IsHexDigit) ? text : null;
        }
        catch (Exception error) when (error is HttpRequestException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    private static JsonObject ExplicitDescriptor(string loader, string minecraftVersion, string version)
    {
        ValidateId(version);
        var descriptor = new JsonObject
        {
            ["version"] = version,
            ["minecraftVersion"] = minecraftVersion
        };
        if (loader is "fabric" or "quilt")
        {
            var host = loader == "fabric" ? "https://meta.fabricmc.net/v2" : "https://meta.quiltmc.org/v3";
            descriptor["profileUrl"] = $"{host}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(version)}/profile/json";
        }
        else
        {
            var artifact = loader == "forge" || minecraftVersion == "1.20.1" ? "forge" : "neoforge";
            var mavenVersion = artifact == "forge" && !version.StartsWith(minecraftVersion + "-", StringComparison.Ordinal) ? minecraftVersion + "-" + version : version;
            var root = loader == "forge" ? "https://maven.minecraftforge.net/net/minecraftforge/" : "https://maven.neoforged.net/releases/net/neoforged/";
            descriptor["installerUrl"] = $"{root}{artifact}/{mavenVersion}/{artifact}-{mavenVersion}-installer.jar";
        }

        return descriptor;
    }

    private static string? NeoForgeGameVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[0], out var major))
            return null;
        if (major == 0)
            return parts[1];
        if (!int.TryParse(parts[1], out var minor))
            return null;
        if (major < 26)
            return "1." + major + (minor == 0 ? string.Empty : "." + minor);
        if (parts.Length < 4 || !int.TryParse(parts[2], out var patch))
            return null;
        var game = $"{major}.{minor}" + (patch == 0 ? string.Empty : "." + patch);
        var separator = version.IndexOf('+');
        return separator >= 0 ? game + "-" + version[(separator + 1)..] : game;
    }

    private static async Task<JsonObject> ReadArchiveJsonAsync(ZipArchive archive, string name, CancellationToken ct)
    {
        var stream = (archive.GetEntry(name.TrimStart('/')) ?? throw new InvalidDataException($"Installer entry '{name}' is missing.")).Open();
        await using var streamDisposal = stream.ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false) as JsonObject ?? throw new InvalidDataException($"Installer entry '{name}' must be an object.");
    }

    private static string ReadMainClass(string jar)
    {
        using var archive = ZipFile.OpenRead(jar);
        using var reader = new StreamReader((archive.GetEntry("META-INF/MANIFEST.MF") ?? throw new InvalidDataException($"Processor has no manifest: {jar}")).Open());
        var unfolded = reader.ReadToEnd().Replace("\r\n ", string.Empty, StringComparison.Ordinal).Replace("\n ", string.Empty, StringComparison.Ordinal);
        return unfolded.Split('\n').Select(x => x.TrimEnd('\r')).FirstOrDefault(x => x.StartsWith("Main-Class: ", StringComparison.OrdinalIgnoreCase))?[12..].Trim() ?? throw new InvalidDataException($"Processor manifest has no Main-Class: {jar}");
    }

    private static async Task ExtractAtomicAsync(ZipArchive archive, string entryName, string destination, CancellationToken ct)
    {
        var stream = (archive.GetEntry(entryName.TrimStart('/')) ?? throw new InvalidDataException($"Installer entry missing: {entryName}")).Open();
        await using var streamDisposal = stream.ConfigureAwait(false);
        await WriteAtomicAsync(stream, destination, ct).ConfigureAwait(false);
    }

    private static async Task CopyAtomicAsync(string source, string destination, CancellationToken ct)
    {
        var stream = File.OpenRead(source);
        await using var streamDisposal = stream.ConfigureAwait(false);
        await WriteAtomicAsync(stream, destination, ct).ConfigureAwait(false);
    }

    private static Task WriteAtomicAsync(Stream input, string destination, CancellationToken ct) =>
        AtomicFile.CopyAsync(input, destination, bufferSize: 65536, options: FileOptions.Asynchronous | FileOptions.WriteThrough, ct);

    private sealed class CommittedFile(string destination, string? backup, byte[]? previousHash, byte[] incomingHash)
    {
        internal string Destination { get; } = destination;
        internal string? Backup { get; } = backup;
        internal byte[]? PreviousHash { get; } = previousHash;
        internal byte[] IncomingHash { get; } = incomingHash;
        internal bool Restored { get; set; }
    }

    private static async Task CommitFileAsync(string source, string destination, string work, List<CommittedFile> rollback, CancellationToken ct)
    {
        byte[] incomingHash;
        using (var incoming = File.OpenRead(source))
            incomingHash = await SHA256.HashDataAsync(incoming, ct).ConfigureAwait(false);
        string? recoveryCopy = null;
        byte[]? currentHash = null;
        if (File.Exists(destination))
        {
            var current = File.OpenRead(destination);
            await using var currentDisposal = current.ConfigureAwait(false);
            currentHash = await SHA256.HashDataAsync(current, ct).ConfigureAwait(false);
            if (incomingHash.AsSpan().SequenceEqual(currentHash))
                return;
            // The historic backup can predate this retry. Keep a distinct snapshot of this attempt's bytes.
            recoveryCopy = Path.Combine(work, "rollback", Guid.NewGuid().ToString("N") + ".bak");
            current.Position = 0;
            await WriteAtomicAsync(current, recoveryCopy, ct).ConfigureAwait(false);
            var backupName = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(destination))) + ".bak";
            var backup = Path.Combine(work, "backups", backupName);
            if (!File.Exists(backup))
                await CopyAtomicAsync(destination, backup, ct).ConfigureAwait(false);
            await JsonFiles.WriteAsync(backup + ".json", new JsonObject { ["originalPath"] = destination, ["backupPath"] = backup }, ct).ConfigureAwait(false);
        }

        rollback.Add(new(destination, recoveryCopy, currentHash, incomingHash));
        await CopyAtomicAsync(source, destination, ct).ConfigureAwait(false);
    }

    private static async Task<List<string>> RollbackFilesAsync(List<CommittedFile> files)
    {
        var warnings = new List<string>();
        foreach (var file in files.AsEnumerable().Reverse())
        {
            try
            {
                if (!File.Exists(file.Destination))
                {
                    if (file.Backup is null) { file.Restored = true; continue; }

                    warnings.Add($"A committed file was removed externally; retained recovery copy {file.Backup} for {file.Destination}.");
                    continue;
                }

                byte[] currentHash;
                using (var current = File.OpenRead(file.Destination))
                    currentHash = await SHA256.HashDataAsync(current).ConfigureAwait(false);
                if (file.PreviousHash is not null && currentHash.AsSpan().SequenceEqual(file.PreviousHash))
                {
                    file.Restored = true;
                    continue;
                }

                if (!currentHash.AsSpan().SequenceEqual(file.IncomingHash))
                {
                    warnings.Add($"Kept a later external edit to {file.Destination}; recovery copy: {file.Backup ?? "originally absent"}.");
                    continue;
                }

                if (file.Backup is null)
                {
                    File.Delete(file.Destination);
                }
                else
                {
                    using (var backup = File.OpenRead(file.Backup))
                    {
                        if (!(await SHA256.HashDataAsync(backup).ConfigureAwait(false)).AsSpan().SequenceEqual(file.PreviousHash))
                            throw new IOException("The recovery copy changed after it was captured; no replacement was made.");
                    }

                    await CopyAtomicAsync(file.Backup, file.Destination, CancellationToken.None).ConfigureAwait(false);
                }

                file.Restored = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not restore {file.Destination}; recovery copy: {file.Backup ?? "originally absent"}. {error.Message}");
            }
        }

        return warnings;
    }

    private static string Normalize(string loader)
    {
        loader = loader.ToLowerInvariant();
        return loader is "fabric" or "quilt" or "forge" or "neoforge" ? loader : throw new ArgumentException($"Unknown installer '{loader}'. Existing custom loader metadata can still be launched directly.", nameof(loader));
    }

    private static string? Text(JsonNode? node, string key) => node?[key]?.GetValue<string>();
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Invalid version identifier: {id}");
    }

    private static string SafePath(string root, string relative)
    {
        (root, relative) = SharedRuntimeLayout.Route(root, relative);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(root, path);
        return path;
    }

    private static void EnsureInside(string root, string path)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Installer path escapes its preparation directory: {path}");
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Installer path is a symbolic link: {path}");
        for (var directory = Path.GetDirectoryName(path); directory is not null && directory.Length > root.Length; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Installer path traverses a symbolic link: {directory}");
        }
    }

    private sealed partial class VersionComparer : IComparer<string?>
    {
        public static VersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            var a = MyRegex().Split(left ?? string.Empty);
            var b = MyRegex().Split(right ?? string.Empty);
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var comparison = long.TryParse(a[i], out var x) && long.TryParse(b[i], out var y) ? x.CompareTo(y) : string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);
                if (comparison != 0)
                    return comparison;
            }

            return a.Length.CompareTo(b.Length);
        }

        [GeneratedRegex(@"(\d+)")]
        private static partial Regex MyRegex();
    }
}