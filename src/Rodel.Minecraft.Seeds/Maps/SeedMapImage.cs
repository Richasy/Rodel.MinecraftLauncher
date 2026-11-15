// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Rodel.Minecraft.Seeds;

/// <summary>Provides a shared visual biome palette and terrain raster for the native window and PNG export; it does not generate world data.</summary>
public static class SeedMapImage
{
    /// <summary>Gets the approved map/legend RGB color for a registered biome, retaining a visible fallback for future unknown IDs.</summary>
    public static uint BiomeColor(string id) => id switch
    {
        "plains" => 0x9eae70,
        "sunflower_plains" => 0xb8ba70,
        "cherry_grove" => 0xcea6bd,
        "forest" => 0x688967,
        "flower_forest" => 0x8c9e75,
        "birch_forest" => 0x91a67d,
        "old_growth_birch_forest" => 0x7d977a,
        "dark_forest" => 0x4c6d62,
        "pale_garden" => 0xa5b5a5,
        "meadow" => 0xa3b182,
        "grove" => 0xa8beb3,
        "jagged_peaks" => 0xcbd1c8,
        "frozen_peaks" => 0xe1e5df,
        "stony_peaks" => 0xa3aaa0,
        "snowy_slopes" => 0xd8e1d7,
        "taiga" => 0x76978b,
        "old_growth_pine_taiga" => 0x688d7c,
        "old_growth_spruce_taiga" => 0x65877a,
        "snowy_taiga" => 0xb7cec4,
        "snowy_plains" => 0xd7dfcf,
        "ice_spikes" => 0xd1e5e3,
        "desert" => 0xd5bd86,
        "savanna" => 0xb6ae71,
        "savanna_plateau" => 0xaaa068,
        "windswept_savanna" => 0xb4ac80,
        "badlands" => 0xba8e6c,
        "wooded_badlands" => 0x9c8d63,
        "eroded_badlands" => 0xc99b75,
        "jungle" => 0x557c50,
        "sparse_jungle" => 0x7c995e,
        "bamboo_jungle" => 0x74945d,
        "swamp" => 0x7b8a6b,
        "mangrove_swamp" => 0x577a6b,
        "river" => 0x729aa5,
        "frozen_river" => 0xb5d1d5,
        "ocean" => 0x5c8298,
        "deep_ocean" => 0x496c87,
        "warm_ocean" => 0x6aafaf,
        "cold_ocean" => 0x678ba4,
        "lukewarm_ocean" => 0x649da6,
        "deep_cold_ocean" => 0x4f758f,
        "deep_lukewarm_ocean" => 0x4c8997,
        "frozen_ocean" => 0xa2bbc6,
        "deep_frozen_ocean" => 0x7e9bab,
        "beach" => 0xc6bc96,
        "snowy_beach" => 0xc9d2c2,
        "stony_shore" => 0xa7aa94,
        "windswept_hills" => 0x8c9e8e,
        "windswept_forest" => 0x798f76,
        "windswept_gravelly_hills" => 0xa4aaa1,
        "lush_caves" => 0x84a382,
        "dripstone_caves" => 0xa5947a,
        "deep_dark" => 0x45676c,
        "sulfur_caves" => 0xb8b67c,
        "dappled_forest" => 0xe68e30,
        "mushroom_fields" => 0xa58c9d,
        "nether_wastes" => 0x95695f,
        "crimson_forest" => 0xad5869,
        "warped_forest" => 0x508e86,
        "soul_sand_valley" => 0xa48b79,
        "basalt_deltas" => 0x787d83,
        "the_end" => 0xd7cea0,
        "end_highlands" => 0xc7c18a,
        "end_midlands" => 0xaaa87b,
        "end_barrens" => 0x97937a,
        "small_end_islands" => 0x8a839a,
        "the_void" => 0x303944,
        _ => UnknownBiomeColor(id),
    };

    private static uint UnknownBiomeColor(string id)
    {
        // This is a stable display palette, not a claim about native biome/block colors.
        // Numeric historical registries and future IDs must remain distinguishable in map and legend.
        var hash = 2166136261u;
        foreach (var character in id) hash = unchecked((hash ^ character) * 16777619u);
        return ((104u + (hash & 63)) << 16) | ((104u + ((hash >> 8) & 63)) << 8) | (104u + ((hash >> 16) & 63));
    }

    /// <summary>Builds row-major RGBA pixels with transparent unknown samples and the window's fluid/height shading.</summary>
    public static byte[] Pixels(SeedMapTile tile, bool terrain = true)
    {
        Validate(tile);
        var pixels = new byte[tile.Side * tile.Side * 4];
        var minimum = tile.DimensionMinY ?? (tile.World.Dimension == "overworld" ? -64 : 0);
        for (var index = 0; index < tile.Biomes.Length; index++)
        {
            if (tile.Known.Length != 0 && !tile.Known[index]) continue;
            if (!terrain && (tile.Palette.Length == 0 || tile.Biomes[index] < 0)) continue;
            var color = terrain && tile.SurfaceColors.Length != 0 ? tile.SurfaceColors[index] : BiomeColor(tile.Palette[tile.Biomes[index]]);
            if (terrain && tile.Y is null)
            {
                var generated = tile.Source == "generated" || (tile.Source == "mixed" && !tile.Saved[index]);
                if (tile.Heights[index] < minimum && (generated || tile.SurfaceColors.Length == 0)) color = 0x303944;
                else if (tile.SurfaceColors.Length == 0 && tile.Fluids[index] == 1) color = 0x658b9b;
                else if (tile.SurfaceColors.Length == 0 && tile.Fluids[index] == 2) color = 0xbf7856;
            }

            var shade = terrain && tile.Y is null && tile.Heights[index] >= minimum ? Relief(tile, index, minimum) : 1;
            pixels[index * 4] = (byte)Math.Clamp((int)((color >> 16) * shade), 0, 255);
            pixels[(index * 4) + 1] = (byte)Math.Clamp((int)(((color >> 8) & 255) * shade), 0, 255);
            pixels[(index * 4) + 2] = (byte)Math.Clamp((int)((color & 255) * shade), 0, 255);
            pixels[(index * 4) + 3] = 255;
        }

        return pixels;
    }

    private static double Relief(SeedMapTile tile, int index, int minimum)
    {
        if (tile.SurfaceShades.Length != 0) return tile.SurfaceShades[index];
        if (tile.Fluids[index] != 0)
            return tile.WaterDepths.Length == 0 ? 1 : Math.Clamp(1.08 - (Math.Log2(1 + tile.WaterDepths[index]) * .10), .58, 1.04);
        var heights = tile.SurfaceHeights.Length == 0 ? tile.Heights : tile.SurfaceHeights;
        var x = index % tile.Side;
        var z = index / tile.Side;
        if (tile.ReliefHeights.Length != 0)
        {
            var stride = tile.Side + 2;
            var center = ((z + 1) * stride) + x + 1;
            var values = tile.ReliefHeights;
            int Neighbor(int sample) => values[sample] >= minimum ? sample : center;
            var left = Neighbor(center - 1); var right = Neighbor(center + 1);
            var top = Neighbor(center - stride); var bottom = Neighbor(center + stride);
            var horizontal = right == left ? 0 : (values[right] - values[left]) / (double)(Math.Max(1, tile.Step) * (right - left));
            var vertical = bottom == top ? 0 : (values[bottom] - values[top]) / (double)(Math.Max(1, tile.Step) * ((bottom - top) / stride));
            return Slope(horizontal, vertical);
        }

        bool Available(int sample) => (tile.Known.Length == 0 || tile.Known[sample]) && tile.Fluids[sample] == 0 && heights[sample] >= minimum;
        var west = x > 0 && Available(index - 1) ? index - 1 : index;
        var east = x + 1 < tile.Side && Available(index + 1) ? index + 1 : index;
        var north = z > 0 && Available(index - tile.Side) ? index - tile.Side : index;
        var south = z + 1 < tile.Side && Available(index + tile.Side) ? index + tile.Side : index;
        var dx = east == west ? 0 : (heights[east] - heights[west]) / (double)(Math.Max(1, tile.Step) * (east - west));
        var dz = south == north ? 0 : (heights[south] - heights[north]) / (double)(Math.Max(1, tile.Step) * ((south - north) / tile.Side));
        return Slope(dx, dz);
    }

    private static double Slope(double dx, double dz)
    {
        // North-west illumination depends on slope, never absolute altitude. A level plateau keeps its material color.
        var shade = .70 + ((.30 + (.32 * (dx + dz))) / Math.Sqrt(1 + (dx * dx) + (dz * dz)));
        return Math.Clamp(shade, .48, 1.25);
    }

    /// <summary>Freezes source-local terrain shading before composition, preventing predictions from casting artificial cliffs across edited saved terrain.</summary>
    public static double[] Shades(SeedMapTile tile)
    {
        Validate(tile);
        var minimum = tile.DimensionMinY ?? (tile.World.Dimension == "overworld" ? -64 : 0);
        return [.. Enumerable.Range(0, tile.Biomes.Length).Select(index => tile.Y is null && tile.Heights[index] >= minimum
            && (tile.Known.Length == 0 || tile.Known[index]) ? Relief(tile, index, minimum) : 1)];
    }

    /// <summary>Encodes a completed tile as PNG, embedding its full typed context and witnesses in a Rodel metadata text chunk.</summary>
    public static byte[] Png(SeedMapTile tile, bool terrain = true)
    {
        var pixels = Pixels(tile, terrain);
        return Encode(tile.Side, tile.Side, pixels, JsonSerializer.Serialize(tile, SeedJsonContext.Default.SeedMapTile));
    }

    /// <summary>Exports the finest available rectangular samples, rejecting missing tiles, unrelated saved revisions and mismatched preview runtimes.</summary>
    public static byte[] Png(SeedMapView view, bool terrain = true)
    {
        if (view.Width is < 1 or > 4096 || view.Height is < 1 or > 4096 || !double.IsFinite(view.MinX) || !double.IsFinite(view.MinZ)
            || !double.IsFinite(view.BlocksPerPixel) || view.BlocksPerPixel <= 0 || view.Tiles.Length == 0)
        {
            throw new InvalidDataException("Invalid original map viewport.");
        }

        var first = view.Tiles[0];
        var savedContext = first.Source is "saved" or "mixed";
        var previewFingerprint = view.Tiles.Select(tile => tile.PreviewFingerprint).FirstOrDefault(value => value.Length != 0);
        foreach (var tile in view.Tiles)
        {
            Validate(tile);
            if (tile.Step <= 0 || tile.Seed != first.Seed || tile.World != first.World || tile.Y != first.Y || tile.Fingerprint != first.Fingerprint
                || (savedContext ? tile.Source is not ("saved" or "mixed") : tile.Source != first.Source) || tile.SourceName != first.SourceName
                || (tile.PreviewFingerprint.Length != 0 && tile.PreviewFingerprint != previewFingerprint))
            {
                throw new InvalidDataException("Cannot combine map tiles from different generation contexts.");
            }
        }

        var sources = view.Tiles.OrderBy(tile => tile.Step).Select(tile => (Tile: tile, Pixels: Pixels(tile, terrain))).ToArray();
        var pixels = new byte[view.Width * view.Height * 4];
        for (var row = 0; row < view.Height; row++)
        {
            for (var column = 0; column < view.Width; column++)
            {
                var x = view.MinX + ((column + .5) * view.BlocksPerPixel);
                var z = view.MinZ + ((row + .5) * view.BlocksPerPixel);
                var found = false;
                foreach (var source in sources)
                {
                    var sampleX = (int)Math.Floor((x - source.Tile.MinX) / source.Tile.Step);
                    var sampleZ = (int)Math.Floor((z - source.Tile.MinZ) / source.Tile.Step);
                    if (sampleX < 0 || sampleX >= source.Tile.Side || sampleZ < 0 || sampleZ >= source.Tile.Side) continue;
                    source.Pixels.AsSpan(((sampleZ * source.Tile.Side) + sampleX) * 4, 4).CopyTo(pixels.AsSpan(((row * view.Width) + column) * 4, 4));
                    found = true; break;
                }

                if (!found) throw new InvalidDataException("The viewport contains original map samples that have not completed yet.");
            }
        }

        return Encode(view.Width, view.Height, pixels, JsonSerializer.Serialize(view, SeedJsonContext.Default.SeedMapView));
    }

    private static byte[] Encode(int width, int height, byte[] pixels, string context)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR"u8, header);
        var metadata = Encoding.UTF8.GetBytes("Rodel Seed Explorer\0" + context);
        Chunk(output, "tEXt"u8, metadata);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels, row * width * 4, width * 4);
            }
        }

        Chunk(output, "IDAT"u8, compressed.ToArray());
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Validate(SeedMapTile tile)
    {
        if (tile.Side is < 1 or > 8192 || tile.Biomes.Length != tile.Side * tile.Side || tile.Heights.Length != tile.Biomes.Length
            || tile.Fluids.Length != tile.Biomes.Length || !SeedValidation.BiomeSamples(tile))
        {
            throw new InvalidDataException("Cannot render incomplete original map samples.");
        }
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> bytes)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
        output.Write(number);
        output.Write(type);
        output.Write(bytes);
        var crc = uint.MaxValue;
        foreach (var value in type) Add(value);
        foreach (var value in bytes) Add(value);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
        void Add(byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
    }
}