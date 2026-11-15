// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Reuses original registry metadata only within the exact adapter and installed-runtime fingerprint.</summary>
internal sealed class SeedCatalogCache(LauncherPaths paths, string fingerprint, string version)
{
    internal async Task<SeedCatalog> GetAsync(Func<CancellationToken, Task<SeedCatalog>> create, CancellationToken ct)
    {
        var path = Path.Combine(paths.CacheDirectory, "seed-explorer", "catalogs", fingerprint + ".json");
        using var lease = await ResourceLease.AcquireAsync(path, ct).ConfigureAwait(false);
        if (File.Exists(path))
        {
            try
            {
                var cached = JsonFiles.ReadSnapshot(path, stream => JsonSerializer.Deserialize(stream, SeedJsonContext.Default.SeedCatalog));
                if (Valid(cached)) return cached!;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }

        var catalog = (await create(ct).ConfigureAwait(false)) with { Fingerprint = fingerprint };
        if (!Valid(catalog)) throw new InvalidDataException("The original runtime returned incomplete registry metadata.");
        try { await JsonFiles.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(catalog, SeedJsonContext.Default.SeedCatalog), ct).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Original seed catalog could not be cached: {0}", error);
        }

        return catalog;
    }

    private bool Valid(SeedCatalog? catalog) => catalog is { Dimensions.Length: > 0, Biomes: not null, Structures: not null, Names: not null }
        && catalog.Fingerprint == fingerprint && catalog.Version == version
        && catalog.Dimensions.All(item => item is not null && !string.IsNullOrEmpty(item.Id) && item.Biomes is not null
            && item.MinY >= -32768 && item.Height > 0 && (long)item.MinY + item.Height <= 32768)
        && catalog.Biomes.All(item => item is not null && !string.IsNullOrEmpty(item.Id) && item.Dimensions is not null)
        && catalog.Structures.All(item => item is not null && !string.IsNullOrEmpty(item.Id) && item.Dimensions is not null && item.Biomes is not null);
}