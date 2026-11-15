// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Selects visible positive witnesses without mixing nearby places or depending on popup geometry.</summary>
internal static class SeedMapMarkers
{
    internal static SeedPosition[] Positions(SeedExplorerSnapshot state)
    {
        if (!state.HasMap || state.Catalog is null) return [];
        var positions = state.MapPositions.AsEnumerable();
        if (state.StructureResult is { } structures && structures.Seed == state.Seed && structures.World == state.World && structures.Fingerprint == state.Fingerprint)
            positions = positions.Concat(structures.Positions);
        if (state.Search is { } search && search.Query.World == state.World && search.Fingerprint == state.Fingerprint
            && search.Result.Hits.FirstOrDefault(hit => hit.Seed == state.Seed) is { } hit)
        {
            positions = positions.Concat(hit.Matches.SelectMany(match => match.Positions));
        }

        positions = positions.Concat(SavedPositions(state));
        var focused = state.SelectedPlace ?? state.FocusedPlace;
        if (focused is { } selected) positions = positions.Append(selected);
        if (state.ShowOrigin) positions = positions.Prepend(new() { Id = "origin", Kind = "origin", X = 0, Y = 80, Z = 0 });
        if (state.Spawn is { } spawn && state.World.Dimension == "overworld") positions = positions.Prepend(spawn);
        var saved = SavedPositions(state).Select(Identity).ToHashSet();
        return [.. positions.Where(position => saved.Contains(Identity(position)) || (focused is { } active && Identity(active) == Identity(position))
            || (position.Kind is "origin" or "spawn" or "biome" ? position.Kind != "biome" || state.ShowBiomeWitnesses
            : state.StructureLayers is null || state.StructureLayers.Contains(position.Id)))
            .DistinctBy(position => (position.Kind, position.Id, position.X, position.Z))];
    }

    internal static SeedMarker[] Layout(SeedExplorerSnapshot state)
    {
        var saved = SavedPositions(state).Select(Identity).ToHashSet();
        var targets = new HashSet<(string Kind, string Id, int X, int Z)>();
        if (state.StructureResult is { } result && result.Seed == state.Seed && result.World == state.World && result.Fingerprint == state.Fingerprint)
            targets.UnionWith(result.Positions.Select(Identity));
        if (state.Search is { } search && search.Query.World == state.World && search.Fingerprint == state.Fingerprint
            && search.Result.Hits.FirstOrDefault(hit => hit.Seed == state.Seed) is { } hit)
        {
            targets.UnionWith(hit.Matches.SelectMany(match => match.Positions).Select(Identity));
        }

        int Priority(SeedPosition position) => (state.SelectedPlace ?? state.FocusedPlace) is { } selected && Identity(selected) == Identity(position) ? -1
            : position.Kind is "spawn" or "origin" ? 0 : saved.Contains(Identity(position)) ? 2 : targets.Contains(Identity(position)) ? 3 : 4;
        return SeedMapPlanning.Points(Positions(state), new(state.CenterX, state.CenterZ, state.BlocksPerPixel, state.MapWidth, state.MapHeight), Priority);
    }

    private static (string Kind, string Id, int X, int Z) Identity(SeedPosition position) => (position.Kind, position.Id, position.X, position.Z);
    private static IEnumerable<SeedPosition> SavedPositions(SeedExplorerSnapshot state) => state.Bookmarks
        .Where(item => item.Kind == "place" && item.World == state.World && item.Position is not null
            && (state.UseSavedMap ? item.SavedWorld == state.WorldSnapshot?.Selection : item.SavedWorld is null && item.Seed == state.Seed && item.Fingerprint == state.Fingerprint))
        .Select(item => item.Position!);
}