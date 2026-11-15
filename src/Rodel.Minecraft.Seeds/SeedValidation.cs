// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Validates bounded exact-context requests and complete native samples before transport or cache publication.</summary>
public static class SeedValidation
{
    /// <summary>The smallest map scale accepted by viewport controls and persisted exploration bookmarks.</summary>
    public const double MinimumViewportScale = .25;
    /// <summary>The largest world-block distance per logical pixel accepted for an exploration bookmark.</summary>
    public const double MaximumViewportScale = 65_536;

    /// <summary>Checks that a request targets the same installed version and Java selection as its open session.</summary>
    public static void Profile(SeedRuntimeProfile actual, SeedRuntimeProfile expected)
    {
        RuntimeProfile(actual);
        if (!Path.GetFullPath(actual.MinecraftDirectory).Equals(expected.MinecraftDirectory, StringComparison.OrdinalIgnoreCase)
            || actual.VersionId != expected.VersionId || actual.JavaPath != expected.JavaPath)
        {
            throw new ArgumentException("The request profile does not belong to this open seed session.");
        }
    }

    /// <summary>Checks original dimension and preset identifiers before native generation.</summary>
    public static void World(SeedWorldSettings world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Dimension is not ("overworld" or "the_nether" or "the_end") && !ResourceId(world.Dimension)) throw new ArgumentException("Invalid original dimension identifier.");
        if (world.Preset is not ("normal" or "large_biomes" or "amplified" or "single_biome")) throw new ArgumentException("Unknown original world preset.");
        if (world.Preset == "single_biome" && string.IsNullOrWhiteSpace(world.Biome)) throw new ArgumentException("A single-biome preset needs a registered biome ID.");
    }

    /// <summary>Checks an inclusive original-generation volume before native work or allocation.</summary>
    public static void Region(SeedRegionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RuntimeProfile(query.Profile);
        ArgumentNullException.ThrowIfNull(query.Seed);
        World(query.World);
        var b = query.Bounds ?? throw new ArgumentException("An inclusive region is required.");
        var width = (long)b.MaxX - b.MinX + 1;
        var depth = (long)b.MaxZ - b.MinZ + 1;
        // Actual vertical bounds belong to the selected runtime's dimension registry; this layer bounds transport/allocation.
        const int minimum = -32768;
        const int height = 65536;
        if (width is < 1 or > 256 || depth is < 1 or > 256 || b.MinX < -30_000_000 || b.MinZ < -30_000_000
            || b.MaxX >= 30_000_000 || b.MaxZ >= 30_000_000 || b.MinY < minimum || b.MaxY >= minimum + height || b.MaxY < b.MinY)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The region must fit this dimension and at most 256 × 256 horizontal blocks.");
        }

        if (query.IncludeBlocks && b.Volume > 4_194_304)
            throw new ArgumentOutOfRangeException(nameof(query), "A block preview may contain at most 4194304 positions; select a smaller volume or request statistics only.");
    }

    /// <summary>Rejects incomplete, mismatched or malformed native volumes before rendering or cache publication.</summary>
    public static void Region(SeedRegionResult result, SeedRegionQuery query)
    {
        Region(query);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Seed != query.Seed || result.World != query.World || result.Bounds != query.Bounds || result.GenerationPolicy != "fixed_region_v1"
            || !double.IsFinite(result.ElapsedMilliseconds) || result.ElapsedMilliseconds < 0 || result.Materials is null || result.Containers is null
            || result.Palette is null || result.Blocks is null || result.Materials.Any(item => item is null) || result.Containers.Any(item => item is null))
        {
            throw new InvalidDataException("The original region has a mismatched generation context.");
        }

        var volume = query.Bounds.Volume;
        if (result.Materials.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != result.Materials.Length
            || result.Materials.Any(item => !ResourceId(item.Id) || item.Count < 1 || item.Count > volume)
            || result.Materials.Sum(item => item.Count) != volume)
        {
            throw new InvalidDataException("Original material counts do not cover exactly the requested region.");
        }

        if (result.Containers.Select(item => (item.X, item.Y, item.Z)).Distinct().Count() != result.Containers.Length)
            throw new InvalidDataException("The original region returned duplicate container positions.");
        foreach (var container in result.Containers)
        {
            if (!ResourceId(container.Id) || !query.Bounds.Contains(container.X, container.Y, container.Z)
                || container.State is not ("generated" or "unevaluated" or "dynamic_reward") || container.Slots is < 0 or > 256
                || container.Items is null || container.Items.Any(item => item is null) || container.Items.Select(item => item.Slot).Distinct().Count() != container.Items.Length
                || (container.State != "generated" && container.Items.Length != 0))
            {
                throw new InvalidDataException("The original region returned an invalid container.");
            }

            foreach (var item in container.Items)
            {
                if (!ResourceId(item.Id) || item.Slot < 0 || item.Slot >= container.Slots || item.Count < 1 || item.Components is null)
                    throw new InvalidDataException("The original container returned an invalid item stack.");
            }
        }

        if (!query.IncludeBlocks)
        {
            if (result.Blocks.Length != 0 || result.Palette.Length != 0) throw new InvalidDataException("An analysis-only region unexpectedly includes block storage.");
            return;
        }

        if (result.Blocks.LongLength != volume * 2 || result.Palette.Length is < 1 or > 65_536
            || result.Palette.Any(state => state is null || !ResourceId(state.Id) || state.Properties is null || state.Properties.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Value is null)))
        {
            throw new InvalidDataException("The original region returned an incomplete block palette or volume.");
        }

        RegionColors(result);

        for (var index = 0; index < result.Blocks.Length; index += 2)
        {
            if ((result.Blocks[index] | (result.Blocks[index + 1] << 8)) >= result.Palette.Length)
                throw new InvalidDataException("The original region contains an invalid block palette index.");
        }
    }

    /// <summary>Checks optional frozen biome and original tint-index storage without requiring generated or saved provenance.</summary>
    public static void RegionColors(SeedRegionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.BiomePalette is null || result.Biomes is null || result.TintPalette is null || result.TintIndices is null)
            throw new InvalidDataException("Frozen scene color metadata is missing.");
        if (result.Biomes.Length != 0 || result.BiomePalette.Length != 0)
        {
            if (result.Biomes.LongLength != result.Bounds.Volume * 2 || result.BiomePalette.Length is < 1 or > 65536 || result.BiomePalette.Any(id => !ResourceId(id)))
                throw new InvalidDataException("Frozen saved biome metadata is incomplete.");
            for (var offset = 0; offset < result.Biomes.Length; offset += 2)
                if ((result.Biomes[offset] | (result.Biomes[offset + 1] << 8)) >= result.BiomePalette.Length) throw new InvalidDataException("A frozen biome index is invalid.");
        }

        if (result.TintIndices.Length == 0 && result.TintPalette.Length == 0) return;
        if (result.TintIndices.LongLength != result.Bounds.Volume * 2 || result.TintPalette.Length is < 1 or > 65536
            || result.TintPalette.Any(tint => tint is null || !ResourceId(tint.BiomeId) || tint.Colors is not { Length: <= 64 } || tint.Colors.Any(color => color is < -1 or > 0xFFFFFF)))
        {
            throw new InvalidDataException("Frozen original tint metadata is incomplete.");
        }

        for (var offset = 0; offset < result.TintIndices.Length; offset += 2)
            if ((result.TintIndices[offset] | (result.TintIndices[offset + 1] << 8)) >= result.TintPalette.Length) throw new InvalidDataException("A frozen tint index is invalid.");
    }

    private static bool ResourceId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 256 && id.Count(character => character == ':') == 1
        && id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is ':' or '_' or '-' or '.' or '/');

    /// <summary>Checks an installed original runtime and Overworld settings for shared world-spawn initialization.</summary>
    public static void Spawn(SeedSpawnQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RuntimeProfile(query.Profile);
        ArgumentNullException.ThrowIfNull(query.Seed);
        World(query.World);
        if (query.World.Dimension != "overworld") throw new ArgumentException("World spawn belongs to the Overworld.");
    }

    /// <summary>Rejects a spawn from another seed or preset, invalid coordinates, or a candidate mislabeled as a real world spawn.</summary>
    public static void Spawn(SeedSpawnResult result, SeedSpawnQuery query)
    {
        if (result.Seed != query.Seed || result.World != query.World || result.Position is not { Kind: "spawn", Id: "spawn" } position
            || position.X is < -30_000_000 or >= 30_000_000 || position.Z is < -30_000_000 or >= 30_000_000
            || position.Y is < -64 or > 320 || !double.IsFinite(result.ElapsedMilliseconds) || result.ElapsedMilliseconds < 0)
        {
            throw new InvalidDataException("The original world spawn does not match its generation context.");
        }
    }

    /// <summary>Checks complete tile bounds, sampling limits and explicit dimension slice heights.</summary>
    public static void Map(SeedMapQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RuntimeProfile(query.Profile);
        ArgumentNullException.ThrowIfNull(query.Seed);
        World(query.World);
        if (query.Side is < 1 or > 256 || query.Step is < 1 or > 524_288) throw new ArgumentOutOfRangeException(nameof(query), "Tile side must be 1–256 and step 1–524288.");
        var maxX = (long)query.MinX + ((long)query.Side * query.Step);
        var maxZ = (long)query.MinZ + ((long)query.Side * query.Step);
        if (query.MinX < -30_000_000 || query.MinZ < -30_000_000 || maxX > 30_000_000 || maxZ > 30_000_000)
            throw new ArgumentOutOfRangeException(nameof(query), "The complete tile must be inside the original world boundary.");
        Slice(query.Y);
        if (query.Decorated && (!query.Terrain || query.Y is not null || (long)query.Side * query.Step > 128))
            throw new ArgumentException("Decorated sampling requires a terrain surface spanning at most 128 blocks.", nameof(query));
    }

    /// <summary>Checks the bounded center, radius, result count and requested original structure IDs.</summary>
    public static void Structures(SeedStructureQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RuntimeProfile(query.Profile);
        ArgumentNullException.ThrowIfNull(query.Seed);
        World(query.World);
        if (query.CenterX is < -30_000_000 or >= 30_000_000 || query.CenterZ is < -30_000_000 or >= 30_000_000
            || query.Radius is < 0 or > 8192 || query.Limit is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Structure center, radius or result limit is outside the supported bounds.");
        }

        if (query.Targets is not { Length: > 0 and <= 128 } || query.Targets.Any(string.IsNullOrWhiteSpace)
            || query.Targets.Distinct(StringComparer.Ordinal).Count() != query.Targets.Length)
        {
            throw new ArgumentException("A structure query requires distinct original structure IDs.");
        }
    }

    /// <summary>Rejects native witnesses that do not belong to the frozen query or exceed its positive-result contract.</summary>
    public static void Structures(SeedStructureResult result, SeedStructureQuery query)
    {
        if (result.Seed != query.Seed || result.World != query.World || result.CenterX != query.CenterX || result.CenterZ != query.CenterZ
            || result.Radius != query.Radius || !result.Targets.SequenceEqual(query.Targets) || result.Found < result.Positions.Length
            || result.Positions.Length != Math.Min(result.Found, query.Limit) || !double.IsFinite(result.ElapsedMilliseconds)
            || result.ElapsedMilliseconds < 0 || result.Positions.DistinctBy(position => (position.Id, position.X, position.Y, position.Z)).Count() != result.Positions.Length)
        {
            throw new InvalidDataException("The original structure response does not match its request.");
        }

        long previous = -1;
        foreach (var position in result.Positions)
        {
            var x = (long)position.X - query.CenterX;
            var z = (long)position.Z - query.CenterZ;
            var distance = (x * x) + (z * z);
            if (position.Kind != "structure" || !query.Targets.Contains(position.Id, StringComparer.Ordinal) || position.Pieces < 1
                || position.Bounds is not { Length: 6 } bounds || bounds[0] > position.X || bounds[3] < position.X
                || bounds[1] > position.Y || bounds[4] < position.Y || bounds[2] > position.Z || bounds[5] < position.Z
                || distance > (long)query.Radius * query.Radius || distance < previous)
            {
                throw new InvalidDataException("An original structure witness has invalid bounds, radius or ordering.");
            }

            previous = distance;
        }
    }

    /// <summary>Checks positive condition dependency order, budgets and a nonoverflowing restart cursor.</summary>
    public static void Search(SeedSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RuntimeProfile(query.Profile);
        ArgumentNullException.ThrowIfNull(query.StartSeed);
        World(query.World);
        if (query.MaxSeeds is < 1 or > 1_000_000 || query.MaxResults is < 1 or > 100 || query.TimeLimitSeconds is < 1 or > 3600
            || query.BiomeStep is < 4 or > 256 || query.Offset < 0 || query.Offset > long.MaxValue - query.MaxSeeds)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Search budget or restart cursor is outside the supported bounds.");
        }

        if (query.Conditions is not { Length: > 0 and <= 12 }) throw new ArgumentException("Search requires 1–12 positive conditions.");
        var ids = new HashSet<string>(StringComparer.Ordinal) { "origin", "spawn" };
        foreach (var condition in query.Conditions)
        {
            if (string.IsNullOrWhiteSpace(condition.Id) || ids.Contains(condition.Id) || !ids.Contains(condition.Reference))
                throw new ArgumentException("Condition IDs must be unique and references must identify origin, spawn or a preceding condition.");
            if (condition.Reference == "spawn" && query.World.Dimension != "overworld")
                throw new ArgumentException("World spawn references require the Overworld.");
            if (condition.Kind is not ("structure" or "biome") || condition.Targets is not { Length: > 0 and <= 128 }
                || condition.Targets.Any(string.IsNullOrWhiteSpace) || condition.Minimum is < 1 or > 64
                || (condition.Kind == "biome" && condition.Minimum != 1) || condition.Radius is < 0 or > 8192)
            {
                throw new ArgumentException("Invalid positive structure/biome condition.");
            }

            Slice(condition.Y);
            ids.Add(condition.Id);
        }
    }

    /// <summary>Checks that a selected installed version identifies its metadata root and profile.</summary>
    public static void RuntimeProfile(SeedRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.MinecraftDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.VersionId);
    }

    private static void Slice(int? y)
    {
        if (y is < -32768 or > 32767) throw new ArgumentOutOfRangeException(nameof(y), "Slice Y exceeds the bounded native coordinate transport.");
    }

    /// <summary>Checks that returned samples are complete and belong to the exact request.</summary>
    public static void Tile(SeedMapTile tile, SeedMapQuery query)
    {
        var count = checked(query.Side * query.Side);
        if (tile.Seed != query.Seed || tile.World != query.World || tile.MinX != query.MinX || tile.MinZ != query.MinZ
            || tile.Step != query.Step || tile.Side != query.Side || tile.Y != query.Y || tile.Biomes.Length != count
            || tile.Heights.Length != count || tile.Fluids.Length != count
            || (query.Decorated && (!tile.Decorated || tile.SurfaceColors.Length != count || tile.SurfaceHeights.Length != count || tile.WaterDepths.Length != count))
            || !BiomeSamples(tile) || tile.Fluids.Any(value => value is < 0 or > 3)
            || tile.DimensionMinY is < -32768 or > 32767)
        {
            throw new InvalidDataException("The original tile does not match its request or contains incomplete samples.");
        }
    }

    internal static bool BiomeSamples(SeedMapTile tile)
    {
        if ((tile.Known.Length != 0 && tile.Known.Length != tile.Biomes.Length)
            || (tile.SurfaceColors.Length != 0 && tile.SurfaceColors.Length != tile.Biomes.Length)
            || (tile.SurfaceHeights.Length != 0 && tile.SurfaceHeights.Length != tile.Biomes.Length)
            || (tile.ReliefHeights.Length != 0 && tile.ReliefHeights.Length != (tile.Side + 2) * (tile.Side + 2))
            || (tile.SurfaceShades.Length != 0 && (tile.SurfaceShades.Length != tile.Biomes.Length || tile.SurfaceShades.Any(shade => !double.IsFinite(shade) || shade is < 0 or > 2)))
            || (tile.WaterDepths.Length != 0 && (tile.WaterDepths.Length != tile.Biomes.Length || tile.WaterDepths.Any(depth => depth is < 0 or > 65536)))
            || (tile.Saved.Length != 0 && tile.Saved.Length != tile.Biomes.Length)
            || (tile.PreviewEligible.Length != 0 && tile.PreviewEligible.Length != tile.Biomes.Length)
            || (tile.Source == "mixed" && (tile.Saved.Length != tile.Biomes.Length || string.IsNullOrWhiteSpace(tile.PreviewFingerprint))))
        {
            return false;
        }

        for (var index = 0; index < tile.Biomes.Length; index++)
        {
            if (tile.Saved.Length != 0 && tile.Saved[index] && ((tile.Known.Length != 0 && !tile.Known[index])
                || (tile.PreviewEligible.Length != 0 && tile.PreviewEligible[index])))
            {
                return false;
            }

            if (tile.Known.Length != 0 && !tile.Known[index]) continue;
            var biome = tile.Biomes[index];
            if (tile.Palette.Length == 0)
            {
                if (biome != -1 || tile.SurfaceColors.Length != tile.Biomes.Length) return false;
            }
            else if ((biome < 0 && (biome != -1 || tile.SurfaceColors.Length != tile.Biomes.Length)) || biome >= tile.Palette.Length)
            {
                return false;
            }
        }

        return true;
    }
}