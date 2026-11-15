// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Combines native registry states with native saved inventories from a frozen, child-owned export.</summary>
internal static class SeedPortableRegion
{
    internal static async Task<SeedRegionResult> ReadAsync(SeedGeneratedWorld generated, string workspace, SeedRegionQuery query, CancellationToken ct)
    {
        SeedValidation.Region(generated.Region, query);
        var directory = Path.GetFullPath(generated.Directory);
        if (!string.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original generation export escaped its owned workspace.");
        var files = new Dictionary<string, WorldFileStamp>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(directory);
        var entries = 0;
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Original generation exports cannot contain filesystem links.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++entries > 10000) throw new InvalidDataException("The original generation export exceeded its file budget.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Original generation exports cannot contain filesystem links.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else files.Add(entry, WorldFileStamp.Read(entry));
            }
        }

        var dimension = query.World.Dimension switch { "the_nether" => Path.Combine(directory, "DIM-1"), "the_end" => Path.Combine(directory, "DIM1"), _ => directory };
        var nativeId = query.World.Dimension is "overworld" or "the_nether" or "the_end" ? "minecraft:" + query.World.Dimension : query.World.Dimension;
        var namespaced = WorldMapService.StoredDimensionDirectory(directory, nativeId);
        if (namespaced is not null && Directory.Exists(namespaced)) dimension = namespaced;
        const string legacyPrefix = "legacy:dimension_";
        if (query.World.Dimension.StartsWith(legacyPrefix, StringComparison.Ordinal)
            && int.TryParse(query.World.Dimension.AsSpan(legacyPrefix.Length), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var legacyDimension))
        {
            dimension = legacyDimension == 0 ? directory : Path.Combine(directory, "DIM" + legacyDimension.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var containers = new List<SeedContainer>();
        for (var z = query.Bounds.MinZ >> 4; z <= query.Bounds.MaxZ >> 4; z++)
        {
            for (var x = query.Bounds.MinX >> 4; x <= query.Bounds.MaxX >> 4; x++)
            {
                var chunk = await WorldRegionReader.ReadAsync(dimension, x, z, files, ct, containers: true).ConfigureAwait(false)
                    ?? throw new InvalidDataException("An original generated chunk was not flushed into its frozen export.");
                SavedRegionReader.ReadContainers(chunk, query.Bounds, containers);
            }
        }

        var capacities = generated.Inventories.ToDictionary(item => (item.X, item.Y, item.Z));
        return generated.Region with
        {
            Containers = [.. containers.Select(item =>
        {
            if (!capacities.TryGetValue((item.X, item.Y, item.Z), out var inventory)) throw new InvalidDataException("The native generated inventory capacity was not observed.");
            return item with { Id = inventory.Id, State = item.State == "saved" ? "generated" : item.State, Slots = inventory.Slots };
        })]
        };
    }
}