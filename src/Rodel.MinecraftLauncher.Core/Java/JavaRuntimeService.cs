// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Downloads, registers and removes managed Java runtimes while preserving associated external installations.</summary>
public sealed class JavaRuntimeService
{
    private readonly LauncherPaths _paths;
    private readonly HttpClient _http;
    private readonly Uri _manifestUri;
    private readonly DownloadService _downloads;
    /// <summary>Uses the selected data root and optional HTTP/catalog overrides for runtime management.</summary>
    public JavaRuntimeService(LauncherPaths paths, HttpClient? httpClient = null, Uri? manifestUri = null)
    {
        _paths = paths;
        _http = httpClient ?? Rodel.MinecraftLauncher.Core.Downloads.NetworkSettings.Client(paths, TimeSpan.FromMinutes(10));
        _manifestUri = manifestUri ?? new Uri("https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json");
        _downloads = new(paths, _http);
    }

    /// <summary>Reads the official catalog or an explicitly identified cached fallback; cache-write failures retain fresh network data.</summary>
    public async Task<JavaRuntimeCatalog> GetAvailableAsync(bool offline = false, CancellationToken ct = default)
    {
        var cache = Path.Combine(_paths.CacheDirectory, "java-runtime-all.json");
        if (offline)
        {
            var exists = File.Exists(cache);
            return Catalog(exists ? JsonFiles.ReadObject(cache) : [], "cache", true,
                exists ? null : "No cached Java runtime catalog is available offline.");
        }

        JsonObject manifest;
        try
        {
            using var response = await DownloadRequests.GetBufferedAsync(_http, _manifestUri, NetworkSettings.MetadataTimeout(_paths), ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var stream = (await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            await using var streamDisposal = stream.ConfigureAwait(false);
            manifest = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false) as JsonObject ?? throw new InvalidDataException("Java runtime manifest must be an object.");
        }
        catch (Exception ex) when ((ex is HttpRequestException or IOException or System.Text.Json.JsonException or InvalidDataException || (ex is OperationCanceledException && !ct.IsCancellationRequested)) && File.Exists(cache))
        {
            return Catalog(JsonFiles.ReadObject(cache), "cache-fallback", null,
                "Java runtime catalog request failed; cached releases are being used: " + ex.Message);
        }

        string? warning = null;
        try { await JsonFiles.WriteAsync(cache, manifest, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = "Java runtime catalog was fetched, but its cache could not be saved: " + ex.Message;
        }

        return Catalog(manifest, "network", null, warning);
    }

    private static JavaRuntimeCatalog Catalog(JsonObject manifest, string source, bool? offline, string? warning) => new()
    {
        Source = source,
        Offline = offline,
        Warning = warning,
        Platforms = StoredReportFields.Remaining(new JsonObject(manifest.Where(pair => pair.Key is not ("source" or "offline" or "warning"))
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))))
    };

    /// <summary>Lists registered resources with current probe status, preferring current registrations over same-ID legacy entries.</summary>
    public async Task<IReadOnlyList<JavaRuntimeSummary>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<JavaRuntimeSummary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[] { _paths.RuntimesDirectory, _paths.LegacyRuntimesDirectory }.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (Path.GetFileName(directory).StartsWith('.'))
                continue;
            var registration = Path.Combine(directory, "registration.json");
            var id = Path.GetFileName(directory);
            if (!File.Exists(registration) || !seen.Add(id))
                continue;
            JsonObject entry;
            try
            {
                entry = JsonFiles.ReadObject(registration);
                if (!string.Equals(entry["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Java registration ID does not match its directory.");
                await AddProbeAsync(entry, directory, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                entry = new JsonObject { ["id"] = Path.GetFileName(directory), ["isUsable"] = false, ["warning"] = exception.Message };
            }

            result.Add(JavaRuntimeSummary.Read(entry));
        }

        return result;
    }

    /// <summary>Installs the selected Java runtime component and architecture into managed storage with actual transfer progress.</summary>
    public async Task<JavaRegistrationResult> InstallAsync(string component, string? architecture = null, IProgress<DownloadProgress>? progress = null, string? expectedVersion = null, CancellationToken ct = default)
    {
        ValidateId(component);
        using var cacheUse = await CacheActivity.EnterAsync(_paths, ct).ConfigureAwait(false);
        architecture ??= RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };
        var platform = architecture.StartsWith("windows-", StringComparison.Ordinal) ? architecture : "windows-" + architecture;
        ValidateId(platform);
        var all = await GetAvailableAsync(ct: ct).ConfigureAwait(false);
        var releases = all.GetPlatform(platform)?[component] as JsonArray;
        var release = releases?.Where(item => expectedVersion is null || item?["version"]?["name"]?.ToString() == expectedVersion)
            .OrderByDescending(item => item?["version"]?["released"]?.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidDataException(expectedVersion is null
                ? $"No Mojang Java runtime for component '{component}' on '{platform}'. You can select or import a custom Java executable."
                : $"The reviewed Java {expectedVersion} is no longer available for '{component}' on '{platform}'. Reload the catalog before installing.");
        var manifestDescriptor = release["manifest"] ?? throw new InvalidDataException("Runtime release has no manifest.");
        var manifestHash = manifestDescriptor["sha1"]?.GetValue<string>();
        if (manifestHash is not null && (manifestHash.Length != 40 || !manifestHash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid runtime manifest SHA1.");
        var id = component + "-" + platform + "-" + (manifestHash?[..12] ?? "custom");
        var directory = RuntimeDirectory(id);
        using var lease = await ResourceLease.AcquireAsync(resource: directory, cancellationToken: ct).ConfigureAwait(false);
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        await DownloadDescriptorAsync(manifestDescriptor, manifestPath, progress, ct).ConfigureAwait(false);
        var manifest = JsonFiles.ReadObject(manifestPath);
        var files = manifest["files"] as JsonObject ?? throw new InvalidDataException("Java runtime manifest has no files.");
        var published = Path.Combine(directory, "runtime");
        var stage = Directory.Exists(published) ? published : Path.Combine(directory, "runtime.partial");
        Directory.CreateDirectory(stage);
        await JsonFiles.WriteAsync(Path.Combine(directory, "installation.json"), new JsonObject { ["id"] = id, ["component"] = component, ["platform"] = platform, ["state"] = "downloading", ["manifestSha1"] = manifestHash }, ct).ConfigureAwait(false);
        foreach (var (relative, entry) in files)
        {
            if (entry?["type"]?.GetValue<string>() == "directory")
                Directory.CreateDirectory(SafePath(stage, relative));
        }

        var fileEntries = files.Where(pair => pair.Value?["type"]?.GetValue<string>() == "file").ToArray();
        var batch = new DownloadBatchProgress(progress, fileEntries.Length + 1);
        batch.Report(new(stage, 0, null, false));
        await Parallel.ForEachAsync(fileEntries, new ParallelOptions { MaxDegreeOfParallelism = NetworkSettings.Concurrency(_paths, 6), CancellationToken = ct }, async (pair, token) =>
        {
            var raw = pair.Value?["downloads"]?["raw"] ?? throw new InvalidDataException($"No raw Java download: {pair.Key}");
            var target = SafePath(stage, pair.Key);
            await DownloadDescriptorAsync(raw, target, batch, token).ConfigureAwait(false);
            batch.Complete(target);
        }).ConfigureAwait(false);
        foreach (var (relative, entry) in files)
        {
            if (entry?["type"]?.GetValue<string>() == "link")
                MaterializeInternalLink(stage, relative, entry["target"]!.GetValue<string>());
        }

        ct.ThrowIfCancellationRequested();
        FindJava(stage);
        if (stage != published)
            Directory.Move(stage, published);
        var java = FindJava(published);
        var record = await ProbeRegistrationAsync(new InstalledJavaRegistration
        {
            Id = id,
            Component = component,
            Architecture = platform,
            Version = release["version"]?.DeepClone(),
            Managed = true,
            RuntimeDirectory = published,
            JavaPath = java
        }, ct).ConfigureAwait(false);
        await WriteRegistrationAsync(directory, record, ct).ConfigureAwait(false);
        await JsonFiles.WriteAsync(Path.Combine(directory, "installation.json"), new JsonObject { ["id"] = id, ["state"] = "complete", ["manifestSha1"] = manifestHash }, ct).ConfigureAwait(false);
        batch.Complete(java);
        return record;
    }

    /// <summary>Executable/directory imports are associations. Archives are extracted into a managed directory.</summary>
    public async Task<JavaRegistrationResult> ImportAsync(string source, string? id = null, CancellationToken ct = default)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("Java import source does not exist.", source);
        id ??= "custom-" + Guid.NewGuid().ToString("N")[..12];
        var directory = RuntimeDirectory(id);
        using var lease = await ResourceLease.AcquireAsync(resource: directory, cancellationToken: ct).ConfigureAwait(false);
        if (File.Exists(Path.Combine(directory, "registration.json")))
            throw new IOException($"Java registration already exists: {id}");
        Directory.CreateDirectory(directory);
        var archive = File.Exists(source) && (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));
        string java;
        string? published = null;
        if (archive)
        {
            published = Path.Combine(directory, "runtime");
            var stage = Path.Combine(directory, "runtime.partial");
            Directory.CreateDirectory(stage);
            await JsonFiles.WriteAsync(Path.Combine(directory, "installation.json"), new JsonObject { ["id"] = id, ["state"] = "extracting", ["source"] = source }, ct).ConfigureAwait(false);
            if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                await ExtractZipAsync(source, stage, ct).ConfigureAwait(false);
            else
                await ExtractTarAsync(source, stage, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            FindJava(stage);
            if (Directory.Exists(published))
                throw new IOException($"Unregistered runtime directory already exists: {published}");
            Directory.Move(stage, published);
            java = FindJava(published);
        }
        else
        {
            java = Directory.Exists(source) ? FindJava(source) : source;
        }

        var record = await ProbeRegistrationAsync(new ImportedJavaRegistration
        {
            Id = id,
            Managed = archive,
            JavaPath = java,
            RuntimeDirectory = published,
            Source = source
        }, ct).ConfigureAwait(false);
        await WriteRegistrationAsync(directory, record, ct).ConfigureAwait(false);
        return record;
    }

    /// <summary>Permanently deletes owned runtime files and registration; external associations lose only their registration.</summary>
    public async Task<JavaRemovalResult> RemoveAsync(string id, CancellationToken ct = default)
    {
        using var dependenciesLease = await ResourceLease.AcquireAsync(resource: _paths.DependenciesDirectory, cancellationToken: ct).ConfigureAwait(false);
        var directory = RuntimeDirectory(id);
        using var lease = await ResourceLease.AcquireAsync(resource: directory, cancellationToken: ct).ConfigureAwait(false);
        var registration = Path.Combine(directory, "registration.json");
        if (!File.Exists(registration))
            throw new FileNotFoundException($"Java registration not found: {id}", registration);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Java registration directory is redirected by a link; remove the link explicitly without deleting its target.");
        var record = JsonFiles.ReadObject(registration);
        var used = new Rodel.MinecraftLauncher.Core.Instances.InstanceStore(_paths).List().Any(instance =>
        {
            var settings = new Rodel.MinecraftLauncher.Core.Configuration.SettingsService(_paths).GetEffective(instance).Values;
            return string.Equals(settings["javaRuntimeId"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)
                || (settings["javaPath"]?.GetValue<string>() is { } selected && Path.GetFullPath(selected, instance.GameDirectory).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        });
        var global = new Rodel.MinecraftLauncher.Core.Configuration.SettingsService(_paths).GetEffective().Values;
        if (used || string.Equals(global["javaRuntimeId"]?.ToString(), id, StringComparison.OrdinalIgnoreCase)
            || (global["javaPath"]?.ToString() is { } globalJava && globalJava.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            || new Rodel.MinecraftLauncher.Core.Launch.SessionService(_paths).List().Any(session => (session.Status is "running" or "preparing") && session.JavaPath is { } running && running.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("This Java runtime is referenced by an instance or active game.");
        }

        ct.ThrowIfCancellationRequested();
        var managed = record["managed"]?.GetValue<bool>() == true;
        if (managed)
        {
            foreach (var name in new[] { "runtime", "runtime.partial" })
            {
                var runtime = Path.Combine(directory, name);
                if (Directory.Exists(runtime))
                    Directory.Delete(runtime, recursive: (File.GetAttributes(runtime) & FileAttributes.ReparsePoint) == 0);
            }

            File.Delete(Path.Combine(directory, "manifest.json"));
            File.Delete(Path.Combine(directory, "installation.json"));
        }
        // Keep registration until owned-file deletion succeeds so a sharing failure can be retried.
        File.Delete(registration);
        var warnings = new List<string>();
        try
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                warnings.Add("Java registration was removed; unrecognized or externally associated files were retained in " + directory);
            else
                Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Java registration was removed, but its directory could not be cleaned: " + ex.Message);
        }

        return new(id, true, managed, warnings);
    }

    /// <summary>Resolves a registered runtime ID or a selected executable inside its managed runtime without probing or launching it.</summary>
    public string ResolveJavaPath(string id, string? executable = null)
    {
        var directory = RuntimeDirectory(id);
        if (executable is not null)
        {
            var runtime = Path.Combine(directory, "runtime");
            var selected = Path.GetFullPath(executable, runtime);
            if (!selected.StartsWith(runtime + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Java executable must stay within its managed runtime.");
            return selected;
        }

        var java = JsonFiles.ReadObject(Path.Combine(directory, "registration.json"))["javaPath"]?.GetValue<string>()
            ?? throw new InvalidDataException("Java registration has no executable path.");
        return Path.GetFullPath(java, directory);
    }

    private async Task DownloadDescriptorAsync(JsonNode descriptor, string path, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var url = descriptor["url"]?.GetValue<string>() ?? throw new InvalidDataException($"Missing Java download URL: {path}");
        await _downloads.DownloadAsync(uri: new Uri(url), destination: path, sha1: descriptor["sha1"]?.GetValue<string>(), size: descriptor["size"]?.GetValue<long>(), progress: progress, cancellationToken: ct).ConfigureAwait(false);
    }

    private static Task WriteRegistrationAsync(string directory, JavaRegistrationResult record, CancellationToken ct)
    {
        var stored = record.Managed
            ? record with { RuntimeDirectory = "runtime", JavaPath = Path.GetRelativePath(directory, record.JavaPath) }
            : record;
        return JsonFiles.WriteAsync(Path.Combine(directory, "registration.json"),
            JsonSerializer.SerializeToNode(stored, JavaOperationJsonContext.Default.JavaRegistrationResult)!, ct);
    }

    private async Task<JavaRegistrationResult> ProbeRegistrationAsync(JavaRegistrationResult record, CancellationToken ct)
    {
        var probe = await new JavaDiscovery(_paths).ProbeAsync(record.JavaPath, ct).ConfigureAwait(false);
        return record with
        {
            Warnings = probe.Warnings,
            MajorVersion = probe.MajorVersion,
            FullVersion = probe.FullVersion,
            VersionOutput = probe.VersionOutput,
            IsUsable = probe.IsUsable
        };
    }

    private async Task AddProbeAsync(JsonObject record, string directory, CancellationToken ct)
    {
        var java = record["javaPath"]?.GetValue<string>() ?? throw new InvalidDataException("Java registration has no executable path.");
        java = Path.GetFullPath(java, directory);
        record["javaPath"] = java;
        if (record["runtimeDirectory"]?.GetValue<string>() is { } runtime)
            record["runtimeDirectory"] = Path.GetFullPath(runtime, directory);
        var probe = await new JavaDiscovery(_paths).ProbeAsync(java, ct).ConfigureAwait(false);
        record["warnings"] = new JsonArray([.. probe.Warnings.Select(warning => (JsonNode?)JsonValue.Create(warning))]);
        record["majorVersion"] = probe.MajorVersion;
        record["fullVersion"] = probe.FullVersion;
        record["versionOutput"] = probe.VersionOutput;
        record["isUsable"] = probe.IsUsable;
    }

    private static string FindJava(string directory)
    {
        var preferred = Path.Combine(directory, "bin", "java.exe");
        if (File.Exists(preferred))
            return preferred;
        return Directory.EnumerateFiles(directory, "java.exe", SearchOption.AllDirectories).FirstOrDefault() ?? throw new FileNotFoundException($"No java.exe found in imported or installed runtime: {directory}");
    }

    private string RuntimeDirectory(string id)
    {
        ValidateId(id);
        var current = SafePath(_paths.RuntimesDirectory, id);
        var legacy = SafePath(_paths.LegacyRuntimesDirectory, id);
        return !File.Exists(Path.Combine(current, "registration.json")) && File.Exists(Path.Combine(legacy, "registration.json")) ? legacy : current;
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith('.') || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Invalid Java registration ID: {id}");
    }

    private static string SafePath(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Java archive/manifest path escapes destination: {relative}");
        for (var parent = Path.GetDirectoryName(full); parent is not null && parent.Length > root.Length; parent = Path.GetDirectoryName(parent))
        {
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Java destination is redirected by a directory link: {parent}");
        }

        return full;
    }

    private static async Task ExtractZipAsync(string archive, string destination, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = SafePath(destination, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var input = entry.Open();
            await using var inputDisposal = input.ConfigureAwait(false);
            await CopyEntryAsync(input, target, ct).ConfigureAwait(false);
        }
    }

    private static async Task ExtractTarAsync(string archive, string destination, CancellationToken ct)
    {
        var file = File.OpenRead(archive);
        await using var fileDisposal = file.ConfigureAwait(false);
        var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var gzipDisposal = gzip.ConfigureAwait(false);
        var tar = new TarReader(gzip);
        await using var tarDisposal = tar.ConfigureAwait(false);
        var links = new List<(string Name, string Target)>();
        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false)) is not null)
        {
            if (entry.Name.TrimEnd('/') is "" or ".")
                continue;
            var target = SafePath(destination, entry.Name);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
            }
            else if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                var relativeTarget = entry.EntryType == TarEntryType.HardLink ? Path.GetRelativePath(Path.GetDirectoryName(target)!, SafePath(destination, entry.LinkName)) : entry.LinkName;
                links.Add((entry.Name, relativeTarget));
            }
            else if (entry.DataStream is { } data)
            {
                await CopyEntryAsync(data, target, ct).ConfigureAwait(false);
            }
        }

        foreach (var (name, target) in links)
            MaterializeInternalLink(destination, name, target);
    }

    private static void MaterializeInternalLink(string root, string relative, string target)
    {
        var path = SafePath(root, relative);
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
        SafePath(root, Path.GetRelativePath(root, resolved));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(resolved))
            File.Copy(resolved, path, true);
        else if (Directory.Exists(resolved))
            Directory.CreateSymbolicLink(path, resolved);
        else
            throw new FileNotFoundException($"Java link target is missing: {relative} -> {target}", resolved);
    }

    private static Task CopyEntryAsync(Stream input, string destination, CancellationToken ct) =>
        AtomicFile.CopyAsync(input, destination, bufferSize: 4096, options: FileOptions.None, ct);
}