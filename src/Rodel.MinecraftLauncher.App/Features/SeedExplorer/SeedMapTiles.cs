// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Plans only the current viewport's coarse/refined native samples; it never fills unknown terrain by repetition.</summary>
internal static class SeedMapTiles
{
    internal static SeedMapViewport View(SeedExplorerSnapshot state) => new(state.CenterX, state.CenterZ, state.BlocksPerPixel, state.MapWidth, state.MapHeight);
    internal static int Step(double blocksPerPixel, float width, float height) => SeedMapPlanning.Step(new(0, 0, blocksPerPixel, width, height));
    internal static SeedMapQuery Template(SeedExplorerSnapshot state, SeedRuntimeProfile profile) => new()
    {
        Profile = profile,
        Seed = state.Seed,
        World = state.World with { },
        Y = state.SliceY,
        Terrain = true,
        Decorated = state.UseSavedMap || state.Catalog?.DecoratedMaps == true,
        Structures = [],
    };
    internal static SeedMapQuery[] Plan(SeedExplorerSnapshot state, SeedRuntimeProfile profile) => SeedMapPlanning.Raster(Template(state, profile), View(state));
    internal static string Key(SeedMapQuery query) => SeedMapPlanning.Key(query);

    internal static ImageSource Image(SeedMapTile tile, bool terrain)
    {
        var pixels = SeedMapImage.Pixels(tile, terrain);
        for (var index = 0; index < pixels.Length; index += 4) (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
        return ImageSource.FromPixels(new(tile.Side, tile.Side, pixels));
    }

    internal static bool Visible(SeedMapTile tile, SeedExplorerSnapshot state) => SeedMapPlanning.Visible(tile, View(state));

    internal static SeedRenderedTile[] Layers(IEnumerable<SeedRenderedTile> tiles, SeedExplorerSnapshot state)
    {
        // A newly arrived parent must never cover sharper completed samples, even after the target LOD changes.
        return [.. tiles.Where(entry => Visible(entry.Tile, state)).OrderByDescending(entry => entry.Tile.Step)];
    }

    internal static SeedRenderedTile[] Presentation(SeedExplorerSnapshot state)
    {
        var backdrop = state.UseSavedMap && state.MapBackdrop is { } retained && retained.Selection == state.WorldSnapshot?.Selection
            ? retained.Tiles.Where(tile => tile.Tile.World == state.World && tile.Tile.Y == state.SliceY && tile.Tile.Seed == state.Seed)
            : [];
        var step = SeedMapPlanning.Step(View(state));
        // Coarse parents fill gaps, history is resampled for this zoom, and the matching level replaces history.
        return [.. Layers(backdrop.Concat(state.Tiles), state).OrderBy(tile => tile.Tile.Step == step ? 2 : tile.Tile.Step < step ? 1 : 0).ThenByDescending(tile => tile.Tile.Step)];
    }

    internal static SeedRenderedTile[] Retain(SeedExplorerSnapshot state, SeedExplorerSnapshot next) =>
        [.. Presentation(state).Where(tile => tile.Tile.World == next.World && tile.Tile.Y == next.SliceY && tile.Tile.Seed == next.Seed && Visible(tile.Tile, next))
            .GroupBy(tile => tile.RequestKey, StringComparer.Ordinal)
            .Select(group => group.OrderBy(tile => tile.Tile.Known.Length == 0 ? tile.Tile.Side * tile.Tile.Side : tile.Tile.Known.Count(value => value)).Last())
            .OrderBy(tile => tile.Tile.Step).Take(192)];
}