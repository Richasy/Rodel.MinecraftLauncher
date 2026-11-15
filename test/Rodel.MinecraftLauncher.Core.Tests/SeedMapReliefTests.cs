// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using StbImageWriteSharp;
using System.IO.Compression;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects readable terrain and original top-material colors; native generation and save preservation have separate integration cases.</summary>
[TestClass]
public sealed class SeedMapReliefTests
{
    /// <summary>Review: saved compact states and older clients may lack usable model metadata. Their installed texture and explicit biome color must remain readable without Java; green constants or recoloring authored red leaves would both misrepresent autumn terrain.</summary>
    [TestMethod]
    public async Task SavedMaterialFallback_RetainsInstalledBiomeColorWithoutStateGuessing()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var path = Path.Combine(paths.Root, "resources.jar");
            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("data/minecraft/worldgen/biome/dappled_forest.json").Open(), Encoding.UTF8))
                    await writer.WriteAsync("""{"effects":{"grass_color":"#df6827","foliage_color":"#e68e30"}}""");
                using (var texture = archive.CreateEntry("assets/minecraft/textures/block/grass_block_top.png").Open())
                    new ImageWriter().WritePng([255, 255, 255, 255], 1, 1, ColorComponents.RedGreenBlueAlpha, texture);
                using (var texture = archive.CreateEntry("assets/minecraft/textures/block/red_poplar_leaves.png").Open())
                    new ImageWriter().WritePng([200, 40, 20, 255], 1, 1, ColorComponents.RedGreenBlueAlpha, texture);
            }

            using var colors = new WorldBlockColors(path);
            Assert.AreEqual(0xdf6827u, colors.Color(new SeedBlockState { Id = "minecraft:grass_block", DefaultsOmitted = true }, "dappled_forest"));
            Assert.AreEqual(0xc82814u, colors.Color("minecraft:red_poplar_leaves", "dappled_forest"));
            Assert.AreEqual(0xa9a191u, colors.Color("future:unknown", "dappled_forest"));
        });
    }

    /// <summary>Review: absolute-altitude brightening makes opposing slopes indistinguishable. A translated ridge must keep its shading, while reversed slopes differ. Uniform-color synthetic materials isolate relief from biome palettes and texture interpretation.</summary>
    [TestMethod]
    public void Relief_DistinguishesOpposingSlopesWithoutRecoloringHigherPlateaus()
    {
        var ridge = Tile([64, 65, 66, 65, 64, 64, 65, 66, 65, 64, 64, 65, 66, 65, 64, 64, 65, 66, 65, 64, 64, 65, 66, 65, 64]);
        var pixels = SeedMapImage.Pixels(ridge);
        var translated = SeedMapImage.Pixels(ridge with { Heights = [.. ridge.Heights.Select(height => height + 100)] });
        CollectionAssert.AreEqual(pixels, translated);
        Assert.IsGreaterThan(pixels[(13 * 4)], pixels[(11 * 4)], "The north-west-facing slope must be lighter than the opposing slope.");
        var low = SeedMapImage.Pixels(ridge with { Heights = [.. Enumerable.Repeat(64, 25)] });
        var high = SeedMapImage.Pixels(ridge with { Heights = [.. Enumerable.Repeat(200, 25)] });
        CollectionAssert.AreEqual(low, high);
        CollectionAssert.AreEqual(new byte[] { 128, 128, 128, 255 }, low[..4]);
    }

    /// <summary>Review: independently shaded tiles must match the same terrain shaded as one area. A curved slope makes a one-sided edge derivative observably wrong; a retained one-sample halo supplies the real neighboring heights without fabricating coverage.</summary>
    [TestMethod]
    public void Relief_PaddedTileMatchesInteriorOfContinuousTerrain()
    {
        var heights = Enumerable.Range(0, 49).Select(index => 64 + (index % 7 * (index % 7)) + (index / 7)).ToArray();
        var whole = new SeedMapTile
        {
            Side = 7,
            Step = 1,
            Palette = ["plains"],
            Biomes = new int[49],
            Heights = heights,
            Fluids = new int[49],
            SurfaceColors = [.. Enumerable.Repeat(0x808080u, 49)],
        };
        var fragment = Tile([.. Enumerable.Range(0, 25).Select(index => heights[(((index / 5) + 1) * 7) + (index % 5) + 1])]);
        var expected = SeedMapImage.Pixels(whole);
        var padded = SeedMapImage.Pixels(fragment with { ReliefHeights = heights });
        var unpadded = SeedMapImage.Pixels(fragment);
        for (var index = 0; index < 25; index++)
            CollectionAssert.AreEqual(expected.AsSpan(((((index / 5) + 1) * 7) + (index % 5) + 1) * 4, 4).ToArray(), padded.AsSpan(index * 4, 4).ToArray());
        Assert.IsFalse(unpadded.SequenceEqual(padded), "The curved fixture must distinguish a lost halo from continuous shading.");
    }

    /// <summary>Review: overriding every fluid with one blue discards original biome water and depth. An unknown neighbor must remain transparent without casting a fictitious cliff. Existing PNG framing cases do not exercise either material preservation or missing-neighbor relief.</summary>
    [TestMethod]
    public void WaterAndUnknownSamples_PreserveNativeHueDepthAndCoverage()
    {
        var tile = Tile([.. Enumerable.Repeat(64, 25)]);
        tile.SurfaceColors = [.. Enumerable.Repeat(0x306090u, 25)];
        tile.Fluids = [.. Enumerable.Repeat(1, 25)];
        tile.WaterDepths = [.. Enumerable.Range(0, 25)];
        var pixels = SeedMapImage.Pixels(tile);
        Assert.IsGreaterThan(pixels[24 * 4], pixels[0]);
        Assert.AreEqual(pixels[0] * 3, pixels[2], 3, "Depth shading retains the supplied water hue.");
        tile.Fluids = new int[25]; tile.WaterDepths = [];
        tile.Known = [.. Enumerable.Repeat(true, 25)]; tile.Known[11] = false; tile.Heights[11] = -32768;
        pixels = SeedMapImage.Pixels(tile);
        Assert.AreEqual(0, pixels[(11 * 4) + 3]);
        CollectionAssert.AreEqual(new byte[] { 0x30, 0x60, 0x90, 255 }, pixels[(12 * 4)..(13 * 4)]);
        CollectionAssert.AreEqual(SeedMapImage.Pixels(tile with { Y = 64 })[(12 * 4)..(13 * 4)], pixels[(12 * 4)..(13 * 4)]);
    }

    /// <summary>Review: the saved-map reader selected the first variant and rejected sprite objects. Actual state selection, top faces, authored tint indices and alpha must determine color. Tiny independent textures make a side/first-variant/green-everything implementation observably wrong; the scene tests cover geometry instead.</summary>
    [TestMethod]
    public void SurfaceResources_UseActualStateTopFaceAndOnlyAuthoredTint()
    {
        using var jar = new MemoryStream();
        using (var archive = new ZipArchive(jar, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "blockstates/material.json", """{"variants":{"axis=x":{"model":"test:block/other"},"axis=y":{"model":"test:block/top"}}}""");
            Add(archive, "models/block/top.json", """{"textures":{"top":{"sprite":"test:block/white"},"side":"test:block/red"},"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"up":{"texture":"#top","tintindex":0},"down":{"texture":"#side"},"north":{"texture":"#side"}}}]}""");
            Add(archive, "models/block/other.json", """{"textures":{"top":"test:block/red"},"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"up":{"texture":"#top"}}}]}""");
            Texture(archive, "white", [255, 255, 255, 255, 0, 0, 255, 0, 255, 255, 255, 255, 0, 0, 255, 0], 2);
            Texture(archive, "red", [200, 40, 20, 255], 1);
        }

        jar.Position = 0;
        using (var colors = new SeedSurfaceColors(jar))
        {
            var state = new SeedBlockState { Id = "test:material", Properties = new() { ["axis"] = "y" } };
            Assert.AreEqual(0x408020u, colors.Color(state, [0x408020]));
            Assert.AreEqual(0xc82814u, colors.Color(state with { Properties = new() { ["axis"] = "x" } }, [0x408020]));
            Assert.IsNull(colors.Color(new() { Id = "test:missing" }, []));
        }

        Assert.IsTrue(jar.CanRead, "The resource reader borrows the installed client stream.");
    }

    private static SeedMapTile Tile(int[] heights) => new()
    {
        Side = 5,
        Step = 1,
        Palette = ["plains"],
        Biomes = new int[25],
        Heights = heights,
        Fluids = new int[25],
        SurfaceColors = [.. Enumerable.Repeat(0x808080u, 25)],
    };

    private static void Add(ZipArchive archive, string name, string json)
    {
        using var writer = new StreamWriter(archive.CreateEntry("assets/test/" + name).Open(), Encoding.UTF8);
        writer.Write(json);
    }

    private static void Texture(ZipArchive archive, string name, byte[] bytes, int side)
    {
        using var stream = archive.CreateEntry("assets/test/textures/block/" + name + ".png").Open();
        new ImageWriter().WritePng(bytes, side, side, ColorComponents.RedGreenBlueAlpha, stream);
    }
}