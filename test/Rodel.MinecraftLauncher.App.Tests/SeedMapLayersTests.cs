// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;

using NativeApp::Rodel.MinecraftLauncher.App.Features.SeedExplorer;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Sprout.Graphics;
using Sprout.Layout;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the actual App's map paint order independently of generation and native window ownership.</summary>
[TestClass]
public sealed class SeedMapLayersTests
{
    /// <summary>
    /// Review: array order alone cannot prove that retained keyed image widgets keep their paint order when
    /// neighboring tiles arrive or marker progress changes. Exercise the production map subtree across those
    /// updates and inspect the actual displayed pixels. Producers and artwork are synthetic;
    /// the retained controls, reactive updates, layout and drawing commands are real.
    /// </summary>
    [TestMethod]
    public void RefinedImage_StaysAboveCoarseCoverageAcrossNeighborAndProgressUpdates()
    {
        var state = new SeedExplorerState();
        var coarseImage = ImageSource.FromPixels(new(1, 1, [0, 0, 255, 255]));
        var fineImage = ImageSource.FromPixels(new(1, 1, [255, 0, 0, 255]));
        var neighborImage = ImageSource.FromPixels(new(1, 1, [0, 255, 0, 255]));
        var coarse = new SeedRenderedTile(1, "coarse", new() { Step = 8, Side = 2 }, coarseImage, coarseImage);
        var fine = new SeedRenderedTile(2, "fine", new() { Step = 1, Side = 8 }, fineImage, fineImage);
        var neighbor = new SeedRenderedTile(3, "neighbor", new() { MinX = 8, Step = 1, Side = 8 }, neighborImage, neighborImage);
        state.Current = new() { MapWidth = 16, MapHeight = 8, CenterX = 8, CenterZ = 4, BlocksPerPixel = 1, Tiles = [coarse] };
        using var layers = new SeedMapRaster(state);
        layers.Mount();
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, ColorAt(4, 4));
        state.Current = state.Current with { Tiles = SeedMapTiles.Layers([coarse, fine], state.Current) };
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, ColorAt(4, 4));
        state.Current = state.Current with { Tiles = SeedMapTiles.Layers([coarse, fine, neighbor], state.Current) };
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, ColorAt(4, 4));
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255 }, ColorAt(12, 4));
        var completed = PaintedImages(layers, 16, 8).Single().Image.Source;
        state.Current = state.Current with { MarkerRegionsChecked = 1 };
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, ColorAt(4, 4));
        Assert.AreSame(completed, PaintedImages(layers, 16, 8).Single().Image.Source, "Marker-only updates must not recompose or reupload raster pixels.");

        byte[] ColorAt(float x, float y)
        {
            var command = PaintedImages(layers, 16, 8).Last(command => command.Dest.Left <= x && command.Dest.Right > x && command.Dest.Top <= y && command.Dest.Bottom > y);
            var image = Decoded(command.Image.Source);
            var column = (int)((x - command.Dest.Left) / command.Dest.Width * image.Width);
            var row = (int)((y - command.Dest.Top) / command.Dest.Height * image.Height);
            return image.PixelSpan.Slice(((row * image.Width) + column) * 4, 4).ToArray();
        }
    }

    /// <summary>
    /// Review: keeping old sharp tiles above a zoomed-out map creates conspicuous high-detail islands.
    /// Replace the former paint-order assertion with literal display pixels: history and new tiles share
    /// one world-aligned sampling grid, including negative coordinates, and the matching new level replaces
    /// history. Zooming back restores the immutable original detail. This exercises production composition
    /// with synthetic artwork, independently of native generation, GPU output and export behavior.
    /// </summary>
    [TestMethod]
    public void ZoomOut_AllSourcesShareCurrentGridAndZoomBackRestoresRetainedDetail()
    {
        var state = new SeedExplorerSnapshot { BlocksPerPixel = 8, MapWidth = 16, MapHeight = 8 };
        var detail = new byte[128 * 128 * 4];
        for (var index = 0; index < 128 * 128; index++) { detail[(index * 4) + 2] = (byte)(index % 128); detail[(index * 4) + 3] = 255; }

        var fineImage = ImageSource.FromPixels(new(128, 128, detail));
        var blue = ImageSource.FromPixels(new(1, 1, [255, 0, 0, 255]));
        var green = ImageSource.FromPixels(new(1, 1, [0, 255, 0, 255]));
        var old = new SeedRenderedTile(1, "old", new() { MinX = -64, MinZ = -32, Step = 1, Side = 128 }, fineImage, fineImage);
        var overview = new SeedRenderedTile(2, "overview", new() { MinX = -2048, MinZ = -2048, Step = 256, Side = 16 }, blue, blue);
        var refined = new SeedRenderedTile(3, "refined", new() { MinX = -64, MinZ = -32, Step = 32, Side = 4 }, green, green);
        var offscreen = old with { Key = 4, Tile = old.Tile with { MinX = 100_000 } };
        var presentation = new SeedMapPresentation();
        state = state with { Tiles = [old, offscreen] };
        var retained = presentation.Frame(state)!;
        Assert.AreEqual((-64, -32, 32, 4, 2), (retained.MinX, retained.MinZ, retained.Step, retained.Width, retained.Height));
        CollectionAssert.AreEqual(new byte[] { 0, 32, 64, 96, 0, 32, 64, 96 }, Decoded(retained.Image).PixelSpan.ToArray().Where((_, index) => index % 4 == 2).ToArray());
        state = state with { Tiles = [old, overview] };
        CollectionAssert.AreEqual(Decoded(retained.Image).Pixels, Decoded(presentation.Frame(state)!.Image).Pixels, "A newly arrived parent must not undo completed viewport coverage.");
        state = state with { Tiles = [overview, old with { Tile = old.Tile with { Side = 64 } }] };
        Assert.AreEqual(256, presentation.Frame(state)!.Step, "Partial fine coverage must not create a sharp island while its neighbors remain coarse.");
        state = state with { Tiles = [refined, old, offscreen, overview] };
        var complete = Decoded(presentation.Frame(state)!.Image).PixelSpan.ToArray();
        for (var index = 0; index < complete.Length; index += 4) CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255 }, complete[index..(index + 4)]);
        state = state with { Tiles = [overview, offscreen, old, refined] };
        CollectionAssert.AreEqual(complete, Decoded(presentation.Frame(state)!.Image).Pixels);
        state = state with { BlocksPerPixel = .5 };
        var restored = presentation.Frame(state)!;
        Assert.AreEqual((1, 8, 4), (restored.Step, restored.Width, restored.Height));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 60, 255 }, Decoded(restored.Image).PixelSpan[..4].ToArray());
        Assert.AreEqual(128, Decoded(fineImage).Width, "Presentation must not replace or downsample the retained source.");
    }

    /// <summary>
    /// Review: the old capacity-only FIFO could remove a completed active tile while a neighbor arrived, exposing
    /// its coarse parent again. A two-entry cache reaches that pressure deterministically; moving the active
    /// viewport must then release the old tile instead of allowing inactive history to grow without a bound.
    /// </summary>
    [TestMethod]
    public void CachePressure_RetainsActiveDetailAndEvictsDepartedHistory()
    {
        var image = ImageSource.FromPixels(new(1, 1, [255, 0, 0, 255]));
        var first = new SeedRenderedTile(1, "first", new(), image, image);
        var parent = first with { Key = 2, RequestKey = "parent" };
        var second = first with { Key = 3, RequestKey = "second" };
        var third = first with { Key = 4, RequestKey = "third" };
        var cache = new SeedMapTileCache(2);
        cache.Protect(["first", "second"]);
        cache.Store(first); cache.Store(parent); cache.Store(second);
        Assert.IsTrue(cache.TryGet("first", out var retained));
        Assert.AreSame(first, retained);
        Assert.IsTrue(cache.TryGet("second", out _));
        Assert.IsFalse(cache.TryGet("parent", out _));
        cache.Protect(["second", "third"]);
        cache.Store(third);
        Assert.IsFalse(cache.TryGet("first", out _));
        CollectionAssert.AreEquivalent(new[] { second, third }, cache.Tiles.ToArray());
    }

    /// <summary>
    /// Review: saved-revision refresh previously cleared all fine pixels before reloading coarse coverage.
    /// Paint the real retained subtree through that boundary: old fine detail stays visible, new fine detail
    /// replaces it, and another saved-world selection cannot inherit the backdrop. Current export/analysis
    /// tiles remain a separate collection, so old source revisions are never promoted into new data.
    /// </summary>
    [TestMethod]
    public void SavedRevisionRefresh_PreservesFineBackdropUntilReplacementAndScopesItToItsWorld()
    {
        var oldImage = ImageSource.FromPixels(new(1, 1, [255, 0, 0, 255]));
        var coarseImage = ImageSource.FromPixels(new(1, 1, [0, 0, 255, 255]));
        var newImage = ImageSource.FromPixels(new(1, 1, [0, 255, 0, 255]));
        var old = new SeedRenderedTile(1, "fine", new() { Side = 8, Step = 1, Fingerprint = "old" }, oldImage, oldImage);
        var coarse = new SeedRenderedTile(2, "coarse", new() { Side = 2, Step = 8, Fingerprint = "new" }, coarseImage, coarseImage);
        var replacement = old with { Key = 3, Tile = old.Tile with { Fingerprint = "new" }, TerrainImage = newImage, BiomeImage = newImage };
        var selection = new WorldMapSelection("instance", "saved");
        var state = new SeedExplorerState
        {
            Current = new()
            {
                MapWidth = 8,
                MapHeight = 8,
                CenterX = 4,
                CenterZ = 4,
                BlocksPerPixel = 1,
                WorldSource = true,
                WorldLoaded = true,
                WorldSnapshot = new() { Selection = selection, Name = "saved", Version = "test", Profile = new(), Revision = "new" },
                Tiles = [coarse],
                MapBackdrop = new(selection, [old])
            }
        };
        using var raster = new SeedMapRaster(state);
        raster.Mount();
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, Decoded(PaintedImages(raster, 8, 8).Last().Image.Source).PixelSpan[..4].ToArray());
        state.Current = state.Current with { Tiles = [coarse, replacement] };
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255 }, Decoded(PaintedImages(raster, 8, 8).Last().Image.Source).PixelSpan[..4].ToArray());
        state.Current = state.Current with { MapBackdrop = null };
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255 }, Decoded(PaintedImages(raster, 8, 8).Last().Image.Source).PixelSpan[..4].ToArray());
        state.Current = state.Current with
        {
            Tiles = [],
            MapBackdrop = new(selection, [old]),
            WorldSnapshot = state.Current.WorldSnapshot! with { Selection = new("instance", "other") }
        };
        Assert.IsEmpty(PaintedImages(raster, 8, 8));
    }

    private static DrawImageCommand[] PaintedImages(SeedMapRaster raster, float width, float height)
    {
        LayoutPass.Run(raster.Render, BoxConstraints.Tight(new(width, height)));
        var renderer = new MockSceneRenderer();
        raster.Render.Paint(renderer);
        return [.. renderer.Commands.OfType<DrawImageCommand>()];
    }

    private static DecodedImage Decoded(ImageSource source)
    {
        Assert.IsTrue(source.TryGetDecodedPixels(out var image));
        return image;
    }
}