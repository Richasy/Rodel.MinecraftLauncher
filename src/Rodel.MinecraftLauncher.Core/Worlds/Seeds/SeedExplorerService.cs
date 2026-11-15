// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Opens exact-version original generation sessions and reuses the launcher's installation and Java preparation.</summary>
public sealed class SeedExplorerService(LauncherPaths paths)
{
    /// <summary>Calculates or reuses the new world's shared spawn using original world initialization in an isolated session.</summary>
    public async Task<SeedSpawnResult> SpawnAsync(SeedSpawnQuery query, CancellationToken cancellationToken = default)
    {
        var session = await OpenAsync(query.Profile, cancellationToken).ConfigureAwait(false);
        await using var disposal = session.ConfigureAwait(false);
        return await session.SpawnAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a bounded original seed preview through an isolated session; interactive callers should reuse an open session.</summary>
    public async Task<SeedRegionResult> RegionAsync(SeedRegionQuery query, IProgress<SeedRegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = await OpenAsync(query.Profile, cancellationToken).ConfigureAwait(false);
        await using var disposal = session.ConfigureAwait(false);
        return await session.RegionAsync(query, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets durable seed/place bookmarks in this launcher's data root.</summary>
    public SeedBookmarkStore Bookmarks => new(paths);
    /// <summary>Gets durable bounded searches and their restart cursors.</summary>
    public SeedSearchStore Searches => new(paths);

    /// <summary>Gets exact original registry choices without initializing the original generation worker when a matching catalog is already cached; language labels come from installed assets.</summary>
    public async Task<SeedCatalog> GetCatalogAsync(SeedRuntimeProfile profile, string language = "en_us", CancellationToken cancellationToken = default)
    {
        SeedValidation.RuntimeProfile(profile);
        var directory = Path.Combine(paths.CacheDirectory, "seed-explorer", "workers", Guid.NewGuid().ToString("N"));
        var binding = await SeedRuntimeBinding.CreateAsync(paths, profile, directory, cancellationToken).ConfigureAwait(false);
        var catalog = await new SeedCatalogCache(paths, binding.Fingerprint, binding.Version).GetAsync(async ct =>
        {
            try
            {
                var session = await SeedExplorerSession.StartAsync(paths, binding, directory, ct).ConfigureAwait(false);
                await using var disposal = session.ConfigureAwait(false);
                return await session.ReadOriginalCatalogAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
        var names = await SeedGameLanguage.ReadAsync(binding, language, cancellationToken).ConfigureAwait(false);
        binding.EnsureUnchanged();
        return catalog with { Names = names, Fingerprint = binding.Fingerprint };
    }

    /// <summary>Atomically exports completed original samples to an explicit PNG destination, preserving the previous file on cancellation or replacement failure.</summary>
    public static async Task<SeedMapExport> ExportAsync(SeedMapTile tile, string destination, bool terrain = true, CancellationToken cancellationToken = default)
    {
        destination = Path.GetFullPath(destination);
        var png = SeedMapImage.Png(tile, terrain);
        using var lease = await ResourceLease.AcquireAsync(resource: destination, cancellationToken: cancellationToken).ConfigureAwait(false);
        await JsonFiles.WriteBytesAsync(destination, png, cancellationToken).ConfigureAwait(false);
        return new(destination, Convert.ToHexStringLower(SHA256.HashData(png)), tile.Side, tile.Side);
    }

    /// <summary>Atomically exports the frozen visible map rectangle, rejecting unknown pixels or mixed seed/version/world contexts.</summary>
    public static async Task<SeedMapExport> ExportViewAsync(SeedMapView view, string destination, bool terrain = true, CancellationToken cancellationToken = default)
    {
        destination = Path.GetFullPath(destination);
        var png = SeedMapImage.Png(view, terrain);
        using var lease = await ResourceLease.AcquireAsync(resource: destination, cancellationToken: cancellationToken).ConfigureAwait(false);
        await JsonFiles.WriteBytesAsync(destination, png, cancellationToken).ConfigureAwait(false);
        return new(destination, Convert.ToHexStringLower(SHA256.HashData(png)), view.Width, view.Height);
    }
    /// <summary>Combines the cached complete version catalog with registered instances; uninstalled candidates remain disabled.</summary>
    public async Task<SeedVersionOption[]> ListVersionsAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await new InstallationService(paths).GetVersionsAsync(offline: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = new List<SeedVersionOption>();
        foreach (var instance in new InstanceStore(paths).List().Where(instance => instance.VersionId is not null))
        {
            var profile = new SeedRuntimeProfile { MinecraftDirectory = instance.MinecraftDirectory, VersionId = instance.VersionId!, JavaPath = instance.JavaPath };
            try
            {
                profile = SeedRuntimeProfiles.FromInstance(paths, instance);
                var metadata = await new VersionResolver().ResolveAsync(profile.MinecraftDirectory, profile.VersionId, cancellationToken).ConfigureAwait(false);
                var version = metadata["_minecraftVersion"]?.GetValue<string>() ?? profile.VersionId;
                var jarId = metadata["_clientJarVersion"]?.GetValue<string>() ?? profile.VersionId;
                var installed = File.Exists(SharedRuntimeLayout.Resolve(profile.MinecraftDirectory, "versions/" + jarId + "/" + jarId + ".jar"));
                result.Add(new(profile, version, installed, installed ? null : "SEED_VERSION_NOT_INSTALLED") { InstanceIds = [instance.Id] });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            {
                result.Add(new(profile, profile.VersionId, false, exception.Message) { InstanceIds = [instance.Id] });
            }
        }

        var grouped = result.GroupBy(option => option.MinecraftVersion, StringComparer.Ordinal).Select(group =>
            group.OrderByDescending(option => option.Installed).First() with { InstanceIds = [.. group.SelectMany(option => option.InstanceIds).Distinct(StringComparer.Ordinal)] }).ToDictionary(option => option.MinecraftVersion, StringComparer.Ordinal);
        foreach (var entry in catalog.Versions?.OfType<System.Text.Json.Nodes.JsonObject>() ?? [])
        {
            if (entry["id"] is not System.Text.Json.Nodes.JsonValue value || !value.TryGetValue<string>(out var id)) continue;
            var type = entry["type"] is System.Text.Json.Nodes.JsonValue category && category.TryGetValue<string>(out var text) ? text : null;
            if (grouped.TryGetValue(id, out var existing)) grouped[id] = existing with { Type = type };
            else grouped[id] = new(new() { VersionId = id }, id, false, "SEED_VERSION_NOT_INSTALLED") { Type = type };
        }

        return [.. grouped.Values.OrderByDescending(option => option.Installed).ThenBy(option => option.MinecraftVersion, StringComparer.Ordinal)];
    }

    /// <summary>Starts an isolated original JVM using installed client/dependency files; no save, account, mods or network is used.</summary>
    public async Task<SeedExplorerSession> OpenAsync(SeedRuntimeProfile profile, CancellationToken cancellationToken = default)
    {
        SeedValidation.RuntimeProfile(profile);
        var directory = Path.Combine(paths.CacheDirectory, "seed-explorer", "workers", Guid.NewGuid().ToString("N"));
        try
        {
            var binding = await SeedRuntimeBinding.CreateAsync(paths, profile, directory, cancellationToken).ConfigureAwait(false);
            return await SeedExplorerSession.StartAsync(paths, binding, directory, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Only this new, unshared template workspace is disposable. Player and installation directories are never removed.
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

            throw;
        }
    }

    /// <summary>Explicitly downloads the requested original runtime and its metadata-selected managed Java when needed; creates no instance or player save.</summary>
    public async Task<SeedRuntimeProfile> PrepareAsync(string version = "26.3", IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(paths.Root, "seed-explorer", "runtime");
        await new InstallationService(paths).InstallAsync(root, version, progress, cancellationToken).ConfigureAwait(false);
        var metadata = await new VersionResolver().ResolveAsync(root, version, cancellationToken).ConfigureAwait(false);
        var preferred = JavaSelection.PreferredMajor(metadata) ?? 8;
        var candidates = await new JavaDiscovery(paths).DiscoverAsync(minecraftDirectory: root, ct: cancellationToken).ConfigureAwait(false);
        var java = JavaSelection.Choose(candidates.Where(candidate => candidate.MajorVersion == preferred && candidate.MajorVersion >= 8), preferred, metadata);
        if (java is null)
        {
            var component = metadata["javaVersion"]?["component"]?.GetValue<string>()
                ?? JavaSelection.Component(preferred)
                ?? throw new InvalidDataException("The original version does not declare its Java runtime component.");
            await new JavaRuntimeService(paths).InstallAsync(component, ct: cancellationToken).ConfigureAwait(false);
            candidates = await new JavaDiscovery(paths).DiscoverAsync(minecraftDirectory: root, ct: cancellationToken).ConfigureAwait(false);
            java = JavaSelection.Choose(candidates.Where(candidate => candidate.MajorVersion >= 8), preferred, metadata)
                ?? throw new IOException("The prepared Java runtime could not be discovered.");
        }

        return new() { MinecraftDirectory = root, VersionId = version, JavaPath = java.Path };
    }

    /// <summary>Finds positive assembled-structure witnesses in a short-lived original session; interactive callers should reuse a session.</summary>
    public async Task<SeedStructureResult> FindStructuresAsync(SeedStructureQuery query, CancellationToken cancellationToken = default)
    {
        SeedValidation.Structures(query);
        var session = await OpenAsync(query.Profile, cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
            return await session.FindStructuresAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Computes one tile in a short-lived session; interactive callers should retain an open session to avoid repeated startup.</summary>
    public async Task<SeedMapTile> MapAsync(SeedMapQuery query, CancellationToken cancellationToken = default)
    {
        SeedValidation.Map(query);
        var session = await OpenAsync(query.Profile, cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
            return await session.MapAsync(query, cancellationToken).ConfigureAwait(false);
    }
}