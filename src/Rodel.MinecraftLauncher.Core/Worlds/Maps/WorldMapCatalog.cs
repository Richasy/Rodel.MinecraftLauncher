// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.IO.Compression;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Reads installed vanilla registry resources for legends independently of which saved chunks have been sampled.</summary>
internal static class WorldMapCatalog
{
    internal static SeedCatalog Read(string? clientJar, WorldMapSnapshot snapshot)
    {
        if (clientJar is null || !File.Exists(clientJar)) return Empty(snapshot);
        try
        {
            using var archive = ZipFile.OpenRead(clientJar);
            var biomeIds = Entries(archive, "worldgen/biome");
            var structureIds = Entries(archive, "worldgen/structure");
            var dimensions = snapshot.Dimensions.Select(id =>
            {
                using var type = Json(archive, Resource(id, "dimension_type"));
                var min = type?.RootElement.TryGetProperty("min_y", out var minY) == true && minY.TryGetInt32(out var y) ? y : -32768;
                var height = type?.RootElement.TryGetProperty("height", out var value) == true && value.TryGetInt32(out var h) ? h : 65536;
                var tag = id switch { "the_nether" => "is_nether", "the_end" => "is_end", _ => "is_" + id };
                return new SeedDimension(id, min, height, Values(archive, "#" + tag, []));
            }).ToArray();
            var biomes = biomeIds.Select(id => new SeedBiome(id, [.. dimensions.Where(dimension => dimension.Biomes.Contains(id)).Select(dimension => dimension.Id)])).ToArray();
            var structures = structureIds.Select(id =>
            {
                using var definition = Json(archive, Resource(id, "worldgen/structure"));
                var allowed = definition?.RootElement.TryGetProperty("biomes", out var entries) == true ? Values(archive, entries, []) : [];
                return new SeedStructure(id, [.. dimensions.Where(dimension => dimension.Biomes.Intersect(allowed, StringComparer.Ordinal).Any()).Select(dimension => dimension.Id)], allowed);
            }).ToArray();
            return new(snapshot.Version, dimensions, biomes, structures) { Fingerprint = snapshot.Revision };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Trace.TraceWarning("Saved map registry resources could not be read: {0}", error.Message);
            return Empty(snapshot);
        }
    }

    private static SeedCatalog Empty(WorldMapSnapshot snapshot) => new(snapshot.Version,
        [.. snapshot.Dimensions.Select(id => new SeedDimension(id, -32768, 65536, []))], [], [])
    { Fingerprint = snapshot.Revision };

    private static string[] Entries(ZipArchive archive, string registry)
    {
        var result = new List<string>();
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Split('/', 3);
            var prefix = registry + "/";
            if (parts.Length != 3 || parts[0] != "data" || !parts[2].StartsWith(prefix, StringComparison.Ordinal) || !parts[2].EndsWith(".json", StringComparison.Ordinal)) continue;
            var id = parts[2][prefix.Length..^5];
            result.Add(parts[1] == "minecraft" ? id : parts[1] + ":" + id);
        }

        return [.. result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string[] Values(ZipArchive archive, JsonElement value, HashSet<string> visiting) => value.ValueKind switch
    {
        JsonValueKind.String => Values(archive, value.GetString()!, visiting),
        JsonValueKind.Array => [.. value.EnumerateArray().SelectMany(item => Values(archive, item, visiting)).Distinct(StringComparer.Ordinal)],
        JsonValueKind.Object when value.TryGetProperty("id", out var id) => Values(archive, id, visiting),
        _ => [],
    };

    private static string[] Values(ZipArchive archive, string id, HashSet<string> visiting)
    {
        if (!id.StartsWith('#')) return [WorldMapService.Dimension(id)];
        if (visiting.Count >= 32 || !visiting.Add(id)) return [];
        try
        {
            using var tag = Json(archive, Resource(id[1..], "tags/worldgen/biome"));
            return tag?.RootElement.TryGetProperty("values", out var values) == true ? Values(archive, values, visiting) : [];
        }
        finally { visiting.Remove(id); }
    }

    private static string Resource(string id, string registry)
    {
        var parts = id.Split(':', 2);
        return "data/" + (parts.Length == 2 ? parts[0] : "minecraft") + "/" + registry + "/" + parts[^1] + ".json";
    }

    private static JsonDocument? Json(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null || entry.Length > 1024 * 1024) return null;
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }
}