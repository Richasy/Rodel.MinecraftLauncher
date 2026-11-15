// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

internal sealed record SeedRuntimeBinding(
    SeedRuntimeProfile Profile, string Version, string JavaPath, string[] Classpath,
    string ClientJar, JsonObject Metadata, string Fingerprint, SeedFileStamp[] Files, int JavaMajorVersion, bool Portable, string? MappingsPath)
{
    internal void EnsureUnchanged()
    {
        foreach (var stamp in Files)
        {
            var current = new FileInfo(stamp.Path);
            if (!current.Exists || current.Length != stamp.Length || current.LastWriteTimeUtc != stamp.Modified)
                throw new LauncherOperationException("SEED_RUNTIME_CHANGED", "The selected game files changed. Reopen the version before calculating another map.");
        }
    }

    internal static async Task<SeedRuntimeBinding> CreateAsync(LauncherPaths paths, SeedRuntimeProfile profile, string workspace, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.MinecraftDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.VersionId);
        profile = profile with { MinecraftDirectory = Path.GetFullPath(profile.MinecraftDirectory) };
        var metadataChain = await ReadMetadataChainAsync(profile, ct).ConfigureAwait(false);
        var resolver = new VersionResolver();
        var selected = await resolver.ResolveAsync(profile.MinecraftDirectory, profile.VersionId, ct).ConfigureAwait(false);
        var version = selected["_minecraftVersion"]?.GetValue<string>() ?? profile.VersionId;
        // Plan the original base profile. Loader arguments, agents, mods and data packs never enter this JVM.
        var baseProfile = metadataChain[^1].Id;
        var metadata = await resolver.ResolveAsync(profile.MinecraftDirectory, baseProfile, ct).ConfigureAwait(false);
        var plan = await new LaunchPlanner(paths).BuildAsync(profile.MinecraftDirectory, workspace, baseProfile, profile.JavaPath, ct: ct).ConfigureAwait(false);
        var files = plan.RequiredFiles.Where(file => file.Kind is "library" or "client").ToArray();
        var missing = files.FirstOrDefault(file => !file.Exists);
        if (missing is not null)
            throw new LauncherOperationException("SEED_RUNTIME_INCOMPLETE", $"Original game dependency is missing: {missing.Path}. Prepare the selected version first.");
        var clientJar = files.Single(file => file.Kind == "client").Path;
        bool portable;
        using (var archive = ZipFile.OpenRead(clientJar))
        {
            portable = archive.GetEntry("net/minecraft/world/level/levelgen/densityfunction/SamplerContext.class") is null
                || archive.GetEntry("net/minecraft/world/level/chunk/PalettedContainerFactory.class") is null
                || archive.GetEntry("net/minecraft/server/permissions/LevelBasedPermissionSet.class") is null;
        }

        var resource = typeof(SeedMapQuery).Assembly.GetManifestResourceStream(portable ? "Rodel.SeedExplorer.Portable" : "Rodel.SeedExplorer.26.3")
            ?? throw new InvalidDataException("The packaged original seed adapter is missing.");
        byte[] adapter;
        await using (resource.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await resource.CopyToAsync(buffer, ct).ConfigureAwait(false);
            adapter = buffer.ToArray();
        }

        var requiredJava = SeedRuntimeJava.RequiredMajor(metadata, clientJar, adapter);
        if (plan.JavaMajorVersion is null || plan.JavaMajorVersion < requiredJava)
        {
            // Launch preferences stay advisory for games; this separate helper must run both original and adapter bytecode.
            var available = await new JavaDiscovery(paths).DiscoverAsync(minecraftDirectory: profile.MinecraftDirectory, ct: ct).ConfigureAwait(false);
            var helper = JavaSelection.Choose(available.Where(candidate => candidate.MajorVersion >= requiredJava), requiredJava, metadata)
                ?? throw new LauncherOperationException("SEED_JAVA_REQUIRED", $"The isolated original generation helper requires Java {requiredJava} or newer. Prepare a compatible helper runtime; the instance's existing game Java selection is preserved.");
            plan = await new LaunchPlanner(paths).BuildAsync(profile.MinecraftDirectory, workspace, baseProfile, helper.Path, ct: ct).ConfigureAwait(false);
            files = [.. plan.RequiredFiles.Where(file => file.Kind is "library" or "client")];
            missing = files.FirstOrDefault(file => !file.Exists);
            if (missing is not null)
                throw new LauncherOperationException("SEED_RUNTIME_INCOMPLETE", $"Original game dependency is missing: {missing.Path}. Prepare the selected version first.");
        }

        if (plan.JavaMajorVersion is not { } actualJavaMajor || actualJavaMajor < requiredJava)
            throw new LauncherOperationException("SEED_JAVA_REQUIRED", $"The isolated original generation helper requires Java {requiredJava} or newer. Its selected executable could not satisfy the original and adapter class requirements.");

        var adapterHash = Convert.ToHexStringLower(SHA256.HashData(adapter));
        var adapterPath = Path.Combine(paths.CacheDirectory, "seed-explorer", "adapters", adapterHash + ".jar");
        using (await ResourceLease.AcquireAsync(resource: adapterPath, cancellationToken: ct).ConfigureAwait(false))
        {
            var existing = File.Exists(adapterPath) ? await File.ReadAllBytesAsync(adapterPath, ct).ConfigureAwait(false) : [];
            if (!adapter.AsSpan().SequenceEqual(existing))
                await JsonFiles.WriteBytesAsync(adapterPath, adapter, ct).ConfigureAwait(false);
        }

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fingerprint.AppendData(Encoding.UTF8.GetBytes(metadata.ToJsonString()));
        var stamps = metadataChain.Select(entry => entry.Stamp).ToList();
        var mappingsPath = portable ? await ReadMappingsAsync(paths, metadata, ct).ConfigureAwait(false) : null;
        var inputs = new[] { adapterPath }.Concat(files.Select(file => file.Path)).Append(plan.JavaPath);
        if (mappingsPath is not null) inputs = inputs.Append(mappingsPath);
        foreach (var path in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var stamp = new SeedFileStamp(path, info.Length, info.LastWriteTimeUtc);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
                fingerprint.AppendData(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
            info.Refresh();
            if (info.Length != stamp.Length || info.LastWriteTimeUtc != stamp.Modified)
                throw new IOException("A selected game dependency changed while it was being fingerprinted.");
            stamps.Add(stamp);
        }

        var binding = new SeedRuntimeBinding(profile, version, plan.JavaPath, [adapterPath, .. files.Select(file => file.Path)],
            clientJar, metadata, Convert.ToHexStringLower(fingerprint.GetHashAndReset()), [.. stamps], actualJavaMajor, portable, mappingsPath);
        binding.EnsureUnchanged();
        return binding;
    }

    internal static async Task<string?> ReadMappingsAsync(LauncherPaths paths, JsonObject metadata, CancellationToken ct, HttpClient? mappingClient = null)
    {
        if (metadata["downloads"]?["client_mappings"] is not JsonObject download) return null;
        var digest = download["sha1"]?.GetValue<string>()?.ToLowerInvariant();
        var size = download["size"]?.GetValue<long>();
        if (digest is not { Length: 40 } || !digest.All(Uri.IsHexDigit) || size is <= 0 or > 67108864)
            throw new InvalidDataException("Invalid original mapping descriptor.");
        var path = Path.Combine(paths.CacheDirectory, "seed-explorer", "mappings", digest + ".txt");
        // DownloadService owns the destination lease and verifies/atomically publishes the file.
        // Holding that same lease here while calling it would deadlock a cold mapping cache.
        var cached = File.Exists(path) && new FileInfo(path).Length <= 67108864
            && Convert.ToHexStringLower(SHA1.HashData(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false))) == digest;
        if (!cached)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            using var ownedClient = mappingClient is null ? NetworkSettings.Client(paths, TimeSpan.FromSeconds(30)) : null;
            try
            {
                await new DownloadService(paths, mappingClient ?? ownedClient!, TimeSpan.FromSeconds(30))
                    .DownloadAsync(new Uri(download["url"]!.GetValue<string>()), path, digest, size, cancellationToken: deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new LauncherOperationException("SEED_MAPPINGS_UNAVAILABLE", "The original mapping download timed out. Retry when the selected download source is available; verified cached mappings remain usable offline.");
            }
        }

        return path;
    }

    private static async Task<List<(string Id, SeedFileStamp Stamp)>> ReadMetadataChainAsync(SeedRuntimeProfile profile, CancellationToken ct)
    {
        var result = new List<(string, SeedFileStamp)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var id = profile.VersionId;
        while (!string.IsNullOrEmpty(id))
        {
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or ".." || !visited.Add(id))
                throw new InvalidDataException("Invalid original version inheritance chain.");
            var path = Path.Combine(profile.MinecraftDirectory, "versions", id, id + ".json");
            var info = new FileInfo(path);
            var stamp = new SeedFileStamp(path, info.Length, info.LastWriteTimeUtc);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var metadata = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Version metadata must be an object.");
            result.Add((id, stamp));
            id = metadata["inheritsFrom"]?.GetValue<string>();
        }

        return result;
    }
}

internal sealed record SeedFileStamp(string Path, long Length, DateTime Modified);