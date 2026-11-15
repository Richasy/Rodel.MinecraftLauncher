using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using StbImageSharp;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Checks the exported raster and embedded original context through independent PNG decoding, not world generation.</summary>
[TestClass]
public sealed class SeedMapExportTests
{
    /// <summary>
    /// A native API without biomes must retain real terrain colors and unknown coverage, rather than
    /// inventing a biome or indexing palette[-1]. The existing registered-biome raster cannot detect
    /// this failure. Independent PNG bytes and metadata verify the absent-data contract; this is not
    /// evidence that any particular game generator produced these small synthetic samples.
    /// </summary>
    [TestMethod]
    public void NativeTerrainWithoutBiomes_ExportsColorsAndLeavesBiomeLayerEmpty()
    {
        var tile = new SeedMapTile
        {
            Seed = "2",
            Side = 2,
            Step = 1,
            Y = 64,
            DimensionMinY = 0,
            Palette = [],
            Biomes = [-1, -1, -1, -1],
            Heights = [64, 64, 64, 64],
            Fluids = [0, 0, 0, 0],
            Known = [true, true, false, true],
            SurfaceColors = [0x123456, 0xabcdef, 0, 0x102030],
        };
        var query = new SeedMapQuery { Profile = new() { MinecraftDirectory = "unused-render-fixture", VersionId = "native-no-biomes" }, Seed = tile.Seed, Side = 2, Step = 1, Y = 64 };
        SeedValidation.Tile(tile, query);
        var encoded = SeedMapImage.Png(tile);
        var image = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 255, 0xab, 0xcd, 0xef, 255, 0, 0, 0, 0, 0x10, 0x20, 0x30, 255 }, image.Data);
        Assert.IsEmpty(ReadMetadata(encoded).Palette);
        Assert.IsTrue(SeedMapImage.Pixels(tile, terrain: false).All(value => value == 0));
        Assert.Throws<InvalidDataException>(() => SeedValidation.Tile(tile with { SurfaceColors = [] }, query));
    }

    /// <summary>
    /// A visually valid image is insufficient if its seed, dimension, slice or witnesses are lost. Independently
    /// decoding a small synthetic raster and its metadata detects broken PNG framing, RGBA order, fluid/void
    /// handling and accidental numeric seed conversion. Native minimum-height variants also catch treating historical
    /// Overworld void as modern negative terrain. Actual native samples are verified through real CLI/JVM runs.
    /// </summary>
    [TestMethod]
    [DataRow("the_end", 0)]
    [DataRow("overworld", 0)]
    [DataRow("overworld", -64)]
    public void Png_EncodesRasterAndRetainsLosslessContext(string dimension, int minimum)
    {
        var tile = new SeedMapTile
        {
            Seed = "-9223372036854775808",
            World = new() { Dimension = dimension },
            DimensionMinY = minimum,
            Fingerprint = "exact-runtime",
            MinX = -16,
            MinZ = 32,
            Step = 8,
            Side = 2,
            Palette = ["the_end", "end_highlands"],
            Biomes = [0, 1, 0, 1],
            Heights = [minimum - 1, 64, 63, 63],
            Fluids = [0, 0, 1, 2],
            Structures = [new() { Id = "end_city", Kind = "structure", X = -8, Y = 64, Z = 40, Pieces = 8, Bounds = [-10, 60, 38, -6, 68, 42] }],
        };
        var bytes = SeedMapImage.Png(tile);
        var decoded = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual(2, decoded.Width);
        Assert.AreEqual(2, decoded.Height);
        CollectionAssert.AreEqual(SeedMapImage.Pixels(tile), decoded.Data);
        CollectionAssert.AreEqual(new byte[] { 0x30, 0x39, 0x44, 255 }, decoded.Data[..4]);
        CollectionAssert.AreEqual(new byte[] { 0x65, 0x8b, 0x9b, 255 }, decoded.Data[8..12]);
        var context = ReadMetadata(bytes);
        Assert.AreEqual(tile.Seed, context.Seed);
        Assert.AreEqual(tile.World, context.World);
        Assert.AreEqual(minimum, context.DimensionMinY);
        Assert.AreEqual(tile.Step, context.Step);
        Assert.AreEqual(tile.Fingerprint, context.Fingerprint);
        CollectionAssert.AreEqual(tile.Structures[0].Bounds!, context.Structures[0].Bounds!);
    }

    /// <summary>
    /// A progressive viewport can contain overlapping coarse/fine tiles. Export must preserve its rectangular
    /// crop and use the finer native sample without filling unknown pixels or mixing generation contexts.
    /// This independent decoder checks that user-visible composition contract; it does not duplicate world generation.
    /// </summary>
    [TestMethod]
    public async Task ViewportExport_UsesFineSamplesAndRejectsHolesWithoutReplacingDestination()
    {
        var coarse = new SeedMapTile
        {
            Seed = "9223372036854775807",
            Fingerprint = "runtime",
            Side = 2,
            Step = 4,
            Palette = ["plains"],
            Biomes = [0, 0, 0, 0],
            Heights = [64, 64, 64, 64],
            Fluids = [0, 0, 0, 0]
        };
        var fine = coarse with { MinX = 2, MinZ = 2, Step = 1, Palette = ["desert"] };
        var view = new SeedMapView(1, 1, 1, 6, 3, [coarse, fine]);
        var bytes = SeedMapImage.Png(view, terrain: false);
        var decoded = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual(6, decoded.Width); Assert.AreEqual(3, decoded.Height);
        CollectionAssert.AreEqual(new byte[] { 0x9e, 0xae, 0x70, 255 }, decoded.Data[..4]);
        CollectionAssert.AreEqual(new byte[] { 0xd5, 0xbd, 0x86, 255 }, decoded.Data[(7 * 4)..(8 * 4)]);
        CollectionAssert.AreEqual(new byte[] { 0x9e, 0xae, 0x70, 255 }, decoded.Data[(10 * 4)..(11 * 4)]);
        var context = JsonSerializer.Deserialize(MetadataJson(bytes), SeedJsonContext.Default.SeedMapView)!;
        Assert.AreEqual(1, context.MinX); Assert.AreEqual(3, context.Height);
        Assert.AreEqual(coarse.Seed, context.Tiles[1].Seed);
        Assert.ThrowsExactly<InvalidDataException>(() => SeedMapImage.Png(view with { Tiles = [coarse, fine with { Fingerprint = "other-runtime" }] }));
        await ReportStorage.RunAsync(async paths =>
        {
            var destination = Path.Combine(paths.Root, "view.png");
            await File.WriteAllBytesAsync(destination, bytes, TestContext.CancellationToken);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SeedExplorerService.ExportViewAsync(view with { MinX = 7 }, destination, cancellationToken: TestContext.CancellationToken));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
        });
    }

    private static SeedMapTile ReadMetadata(byte[] png) => JsonSerializer.Deserialize(MetadataJson(png), SeedJsonContext.Default.SeedMapTile)!;
    private static string MetadataJson(byte[] png)
    {
        for (var offset = 8; offset + 12 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == "tEXt")
            {
                var text = Encoding.UTF8.GetString(png, offset + 8, length);
                if (text.StartsWith("Rodel Seed Explorer\0", StringComparison.Ordinal))
                    return text[(text.IndexOf('\0') + 1)..];
            }

            offset += length + 12;
        }

        throw new InvalidDataException("Missing original map context.");
    }

    public TestContext TestContext { get; set; }
}