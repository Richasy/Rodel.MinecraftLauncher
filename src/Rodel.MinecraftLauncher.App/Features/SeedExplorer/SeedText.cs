// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal sealed record SeedTarget(string Id, string Name, string[] Targets, SeedAppearance Appearance);

/// <summary>Localizes App-owned structure families while keeping original registry IDs and installed-game biome translations intact.</summary>
internal static class SeedText
{
    internal static string Preset(string id) => id switch { "large_biomes" => Resources.SeedLargeBiomes, "amplified" => Resources.SeedAmplified, "single_biome" => Resources.SeedSingleBiome, _ => Resources.SeedNormal };
    internal static string Dimension(string id) => id switch { "overworld" => Resources.SeedOverworld, "the_nether" => Resources.SeedNether, "the_end" => Resources.SeedEnd, _ => id };
    internal static string Biome(string id, SeedExplorerSnapshot state) => state.Catalog?.Names.GetValueOrDefault(id) ?? id;
    internal static int MinimumY(SeedExplorerSnapshot state) => state.Catalog?.Dimensions.FirstOrDefault(item => item.Id == state.World.Dimension)?.MinY ?? (state.World.Dimension == "overworld" ? -64 : 0);
    internal static int MaximumY(SeedExplorerSnapshot state) => MinimumY(state) + (state.Catalog?.Dimensions.FirstOrDefault(item => item.Id == state.World.Dimension)?.Height ?? (state.World.Dimension == "overworld" ? 384 : 256)) - 1;
    internal static int DefaultSliceY(SeedExplorerSnapshot state) => Math.Clamp(MinimumY(state) < 0 ? -32 : 64, MinimumY(state), MaximumY(state));
    internal static string Structure(string id, SeedExplorerSnapshot state)
    {
        if (id.StartsWith("abandoned_camp_", StringComparison.Ordinal)) return string.Format(Resources.SeedVariantFormat, Resources.SeedCamp, Biome(id[15..], state));
        if (id.StartsWith("village_", StringComparison.Ordinal)) return string.Format(Resources.SeedVariantFormat, Resources.SeedVillage, Biome(id[8..] switch { "snowy" => "snowy_plains", _ => id[8..] }, state));
        if (id.StartsWith("ruined_portal_", StringComparison.Ordinal))
        {
            return string.Format(Resources.SeedVariantFormat, Resources.SeedPortal, id[14..] switch
            {
                "nether" => Dimension("the_nether"),
                "mountain" => Resources.SeedMountains,
                _ => Biome(id[14..], state),
            });
        }

        return id switch
        {
            "village" => Resources.SeedAnyVillage,
            "abandoned_camp" => Resources.SeedAnyCamp,
            "ancient_city" => Resources.SeedAncientCity,
            "bastion_remnant" => Resources.SeedBastion,
            "buried_treasure" => Resources.SeedTreasure,
            "desert_pyramid" => Resources.SeedDesertPyramid,
            "end_city" => Resources.SeedEndCity,
            "fortress" => Resources.SeedFortress,
            "igloo" => Resources.SeedIgloo,
            "jungle_pyramid" => Resources.SeedJunglePyramid,
            "mansion" => Resources.SeedMansion,
            "mineshaft" => Resources.SeedMineshaft,
            "mineshaft_mesa" => string.Format(Resources.SeedVariantFormat, Resources.SeedMineshaft, Biome("badlands", state)),
            "monument" => Resources.SeedMonument,
            "nether_fossil" => Resources.SeedFossil,
            "ocean_ruin_cold" => string.Format(Resources.SeedVariantFormat, Resources.SeedOceanRuins, Resources.SeedCold),
            "ocean_ruin_warm" => string.Format(Resources.SeedVariantFormat, Resources.SeedOceanRuins, Resources.SeedWarm),
            "pillager_outpost" => Resources.SeedOutpost,
            "ruined_portal" => Resources.SeedPortal,
            "shipwreck" => Resources.SeedShipwreck,
            "shipwreck_beached" => string.Format(Resources.SeedVariantFormat, Resources.SeedShipwreck, Resources.SeedBeached),
            "stronghold" => Resources.SeedStronghold,
            "swamp_hut" => Resources.SeedHut,
            "trail_ruins" => Resources.SeedTrailRuins,
            "trial_chambers" => Resources.SeedTrialChambers,
            _ => id,
        };
    }
    internal static string Position(SeedPosition position, SeedExplorerSnapshot state) => position.Kind == "origin" ? Resources.SeedOrigin
        : position.Kind == "spawn" ? Resources.SeedSpawn
        : position.Kind == "structure" ? Structure(position.Id, state) : position.Kind == "container" ? SeedRegionText.Name(state, position.Id) : Biome(position.Biome ?? position.Id, state);
    internal static string Coordinates(SeedPosition position) => string.Format(CultureInfo.InvariantCulture, Resources.SeedCoordinatesFormat, position.X, position.Y, position.Z);
    internal static string TargetsName(SeedCondition condition, SeedExplorerSnapshot state)
    {
        if (condition.Targets.Length > 1 && condition.Targets.All(id => id.StartsWith("village_", StringComparison.Ordinal))) return Resources.SeedAnyVillage;
        if (condition.Targets.Length > 1 && condition.Targets.All(id => id.StartsWith("abandoned_camp_", StringComparison.Ordinal))) return Resources.SeedAnyCamp;
        return string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", condition.Targets.Select(id => condition.Kind == "biome" ? Biome(id, state) : Structure(id, state)));
    }

    internal static SeedTarget[] Targets(SeedExplorerSnapshot state, string kind, string[]? selected = null)
    {
        if (state.Catalog is not { } catalog) return [];
        var targets = kind == "biome" ? catalog.Biomes.Where(biome => state.World.Preset == "single_biome" ? biome.Id == state.World.Biome : biome.Dimensions.Contains(state.World.Dimension))
            .Select(biome => new SeedTarget(biome.Id, Biome(biome.Id, state), [biome.Id], SeedAppearance.Biome(biome.Id))).ToList()
            : [.. catalog.Structures.Where(structure => state.World.Preset == "single_biome" ? structure.Biomes.Length == 0 || structure.Biomes.Contains(state.World.Biome!) : structure.Dimensions.Contains(state.World.Dimension)).Select(structure => new SeedTarget(structure.Id, Structure(structure.Id, state), [structure.Id], SeedAppearance.Structure(structure.Id)))];
        targets = [.. targets.OrderBy(target => target.Name, StringComparer.CurrentCulture)];
        if (kind == "structure")
        {
            foreach (var family in new[] { "abandoned_camp", "village" })
            {
                var ids = targets.Where(target => target.Id.StartsWith(family + "_", StringComparison.Ordinal)).Select(target => target.Id).Order(StringComparer.Ordinal).ToArray();
                if (ids.Length > 1) targets.Insert(0, new(family, Structure(family, state), ids, SeedAppearance.Structure(family)));
            }
        }

        if (selected is { Length: > 0 } && !targets.Any(target => SameTargets(target.Targets, selected)))
        {
            targets.Insert(0, new("selected", TargetsName(new() { Kind = kind, Targets = selected }, state), selected,
                kind == "biome" ? SeedAppearance.Biome(selected[0]) : SeedAppearance.Structure(selected[0])));
        }

        return [.. targets];
    }
    internal static bool SameTargets(string[] left, string[] right) => left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal));
    internal static string Status(string status) => status switch
    {
        "running" => Resources.SeedSearching,
        "completed" => Resources.SeedCompleted,
        "cancelled" => Resources.SeedStopped,
        "budget_exhausted" => Resources.SeedBudgetEnded,
        "time_limit" => Resources.SeedTimeEnded,
        _ => Resources.SeedReady,
    };
}