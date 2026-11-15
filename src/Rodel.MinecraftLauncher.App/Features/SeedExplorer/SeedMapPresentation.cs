// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>A display-only raster on the viewport's shared world grid, independent of source/export tile resolution.</summary>
/// <param name="MinX">The aligned western sample origin.</param>
/// <param name="MinZ">The aligned northern sample origin.</param>
/// <param name="Step">The common world distance between displayed samples.</param>
/// <param name="Width">The number of displayed columns.</param>
/// <param name="Height">The number of displayed rows.</param>
/// <param name="Image">Immutable premultiplied BGRA samples, including transparent unknowns.</param>
internal sealed record SeedMapFrame(int MinX, int MinZ, int Step, int Width, int Height, ImageSource Image);

/// <summary>Resamples retained and current images onto one viewport grid; marker/progress updates reuse the last image.</summary>
internal sealed class SeedMapPresentation
{
    private SeedMapFrame? _frame;
    private SeedRenderedTile[] _tiles = [];
    private bool _terrain;
    private (int X, int Z, int Step, int Width, int Height) _view;

    internal SeedMapFrame? Frame(SeedExplorerSnapshot state)
    {
        var tiles = SeedMapTiles.Presentation(state);
        if (tiles.Length == 0) { _tiles = []; return _frame = null; }

        var step = SeedMapPlanning.Step(SeedMapTiles.View(state));
        var west = state.CenterX - (state.MapWidth * state.BlocksPerPixel / 2);
        var north = state.CenterZ - (state.MapHeight * state.BlocksPerPixel / 2);
        var view = Grid(step);
        if (_frame is { } cached && _view == view && _terrain == state.Terrain && SameImages(tiles, state.Terrain)) return cached;

        var pixels = new byte[view.Width * view.Height * 4];
        var quality = new int[view.Width * view.Height];
        foreach (var entry in tiles) Paint(entry, state.Terrain, view.X, view.Z, step, view.Width, view.Height, pixels, quality);
        // Refine the whole viewport together. A lone fine tile cannot become a sharp island among coarse parents.
        var displayStep = Math.Max(step, quality.Max());
        var (displayX, displayZ, _, displayWidth, displayHeight) = Grid(displayStep);
        if (displayStep != step)
        {
            pixels = new byte[displayWidth * displayHeight * 4];
            foreach (var entry in tiles) Paint(entry, state.Terrain, displayX, displayZ, displayStep, displayWidth, displayHeight, pixels);
        }

        _tiles = tiles; _terrain = state.Terrain; _view = view;
        return _frame = new(displayX, displayZ, displayStep, displayWidth, displayHeight, ImageSource.FromPixels(new(displayWidth, displayHeight, pixels)));

        (int X, int Z, int Step, int Width, int Height) Grid(int gridStep)
        {
            var x = (int)(Math.Floor(west / gridStep) * gridStep);
            var z = (int)(Math.Floor(north / gridStep) * gridStep);
            return (x, z, gridStep, Math.Max(1, (int)Math.Ceiling((west + (state.MapWidth * state.BlocksPerPixel) - x) / gridStep)),
                Math.Max(1, (int)Math.Ceiling((north + (state.MapHeight * state.BlocksPerPixel) - z) / gridStep)));
        }
    }

    private bool SameImages(SeedRenderedTile[] tiles, bool terrain) => tiles.Length == _tiles.Length && tiles.Zip(_tiles).All(pair =>
        pair.First.Tile.MinX == pair.Second.Tile.MinX && pair.First.Tile.MinZ == pair.Second.Tile.MinZ
        && pair.First.Tile.Step == pair.Second.Tile.Step && pair.First.Tile.Side == pair.Second.Tile.Side
        && ReferenceEquals(Image(pair.First, terrain), Image(pair.Second, terrain)));

    private static ImageSource Image(SeedRenderedTile tile, bool terrain) => terrain ? tile.TerrainImage : tile.BiomeImage;

    private static void Paint(SeedRenderedTile entry, bool terrain, int minX, int minZ, int step, int width, int height, byte[] target, int[]? quality = null)
    {
        var tile = entry.Tile;
        if (tile.Step <= 0 || tile.Side <= 0 || !Image(entry, terrain).TryGetDecodedPixels(out var image)) return;
        var span = tile.Step * tile.Side;
        var left = Math.Max(0, (int)Math.Ceiling((tile.MinX - minX) / (double)step));
        var top = Math.Max(0, (int)Math.Ceiling((tile.MinZ - minZ) / (double)step));
        var right = Math.Min(width, (int)Math.Ceiling((tile.MinX + span - minX) / (double)step));
        var bottom = Math.Min(height, (int)Math.Ceiling((tile.MinZ + span - minZ) / (double)step));
        var source = image.PixelSpan;
        for (var z = top; z < bottom; z++)
        {
            var sourceZ = (minZ + (z * step) - tile.MinZ) / tile.Step * image.Height / tile.Side;
            for (var x = left; x < right; x++)
            {
                var sourceX = (minX + (x * step) - tile.MinX) / tile.Step * image.Width / tile.Side;
                var offset = ((sourceZ * image.Width) + sourceX) * 4;
                if (source[offset + 3] == 0) continue;
                var index = (z * width) + x;
                if (quality is not null && (quality[index] == 0 || tile.Step < quality[index])) quality[index] = tile.Step;
                source.Slice(offset, 4).CopyTo(target.AsSpan(index * 4, 4));
            }
        }
    }
}