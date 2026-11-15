// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using StbImageSharp;
using System.IO.Compression;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Reads actual top models and installed biome color maps; missing optional resources retain visible material fallbacks.</summary>
internal sealed class WorldBlockColors : IDisposable
{
    private readonly FileStream? _file;
    private readonly ZipArchive? _archive;
    private readonly SeedSurfaceColors? _models;
    private readonly Dictionary<string, BiomeColors?> _biomes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]?> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint?> _fallbackTextures = new(StringComparer.Ordinal);

    internal WorldBlockColors(string? clientJar)
    {
        try
        {
            if (clientJar is null || !File.Exists(clientJar)) return;
            _file = File.OpenRead(clientJar);
            _archive = new(_file, ZipArchiveMode.Read, leaveOpen: true);
            _models = new(_file);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { _archive?.Dispose(); _file?.Dispose(); }
    }

    internal uint Color(string block, string biome) => Color(new SeedBlockState { Id = block }, biome);

    internal uint Color(SeedBlockState state, string biome)
    {
        if (state.Id == "minecraft:water") return Water(biome);
        var tints = Tints(state.Id, biome);
        if (_models?.Color(state, tints) is { } exact) return exact;
        var material = FallbackTexture(state.Id) ?? Fallback(state.Id);
        // Missing compact defaults and pre-model clients must still retain a useful material/biome fallback.
        // This never selects an arbitrary state variant or applies foliage tint to authored colored leaves.
        if (tints.Length == 0 || state.Id is not ("minecraft:grass_block" or "minecraft:grass" or "minecraft:short_grass" or "minecraft:tall_grass"
            or "minecraft:fern" or "minecraft:large_fern" or "minecraft:vine" or "minecraft:oak_leaves" or "minecraft:spruce_leaves"
            or "minecraft:birch_leaves" or "minecraft:acacia_leaves" or "minecraft:dark_oak_leaves" or "minecraft:jungle_leaves" or "minecraft:mangrove_leaves"))
        {
            return material;
        }

        var tint = (uint)tints[0];
        return ((((material >> 16) & 255) * ((tint >> 16) & 255) / 255) << 16)
            | ((((material >> 8) & 255) * ((tint >> 8) & 255) / 255) << 8) | ((material & 255) * (tint & 255) / 255);
    }

    private uint? FallbackTexture(string id)
    {
        if (_fallbackTextures.TryGetValue(id, out var color)) return color;
        color = null;
        var split = id.Split(':', 2);
        var prefix = "assets/" + (split.Length == 2 ? split[0] : "minecraft") + "/textures/";
        var name = split[^1];
        var entry = _archive?.GetEntry(prefix + "block/" + name + "_top.png") ?? _archive?.GetEntry(prefix + "block/" + name + ".png")
            ?? _archive?.GetEntry(prefix + "blocks/" + name + ".png");
        try
        {
            if (entry is not null && entry.Length <= 4 * 1024 * 1024)
            {
                using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
                var image = ImageResult.FromMemory(memory.ToArray(), ColorComponents.RedGreenBlueAlpha);
                long r = 0, g = 0, b = 0, weight = 0;
                for (var index = 0; index < image.Width * Math.Min(image.Width, image.Height) * 4; index += 4)
                {
                    var alpha = image.Data[index + 3];
                    r += image.Data[index] * alpha; g += image.Data[index + 1] * alpha; b += image.Data[index + 2] * alpha; weight += alpha;
                }

                if (weight > 0) color = ((uint)(r / weight) << 16) | ((uint)(g / weight) << 8) | (uint)(b / weight);
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException) { }

        return _fallbackTextures[id] = color;
    }

    internal uint Water(string biome) => Biome(biome)?.Water ?? 0x3f76e4;

    private int[] Tints(string block, string biome)
    {
        if (block == "minecraft:spruce_leaves") return [0x619961];
        if (block == "minecraft:birch_leaves") return [0x80a755];
        if (block == "minecraft:mangrove_leaves") return [0x92c648];
        var foliage = block.EndsWith("_leaves", StringComparison.Ordinal);
        if (!foliage && block is not ("minecraft:grass_block" or "minecraft:short_grass" or "minecraft:grass" or "minecraft:tall_grass" or "minecraft:fern" or "minecraft:large_fern" or "minecraft:vine")) return [];
        var data = Biome(biome);
        if (data is null) return [];
        var explicitColor = foliage ? data.Foliage : data.Grass;
        if (explicitColor is not null) return [(int)explicitColor];
        var map = ColorMap(foliage ? "foliage" : "grass");
        if (map is null) return [];
        var temperature = Math.Clamp(data.Temperature, 0, 1);
        var humidity = Math.Clamp(data.Downfall, 0, 1) * temperature;
        var index = (((int)((1 - humidity) * 255) * 256) + (int)((1 - temperature) * 255)) * 4;
        return [(map[index] << 16) | (map[index + 1] << 8) | map[index + 2]];
    }

    private BiomeColors? Biome(string id)
    {
        if (_biomes.TryGetValue(id, out var value)) return value;
        value = null;
        var split = id.Split(':', 2);
        var entry = _archive?.GetEntry("data/" + (split.Length == 2 ? split[0] : "minecraft") + "/worldgen/biome/" + split[^1] + ".json");
        try
        {
            if (entry is not null && entry.Length < 1024 * 1024)
            {
                using var stream = entry.Open(); using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                var effects = root.TryGetProperty("effects", out var found) ? found : default;
                value = new(ReadColor(effects, "grass_color"), ReadColor(effects, "foliage_color"), ReadColor(effects, "water_color"),
                    root.TryGetProperty("temperature", out var temperature) ? temperature.GetDouble() : .5,
                    root.TryGetProperty("downfall", out var downfall) ? downfall.GetDouble() : .5);
            }
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException or OverflowException) { }

        return _biomes[id] = value;
    }

    private static uint? ReadColor(JsonElement effects, string name)
    {
        if (effects.ValueKind != JsonValueKind.Object || !effects.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number)) return number & 0xffffff;
        return value.ValueKind == JsonValueKind.String && uint.TryParse(value.GetString()?.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out number) ? number & 0xffffff : null;
    }

    private byte[]? ColorMap(string name)
    {
        if (_maps.TryGetValue(name, out var bytes)) return bytes;
        bytes = null;
        var entry = _archive?.GetEntry("assets/minecraft/textures/colormap/" + name + ".png");
        try
        {
            if (entry is not null && entry.Length < 1024 * 1024)
            {
                using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
                var image = ImageResult.FromMemory(memory.ToArray(), ColorComponents.RedGreenBlueAlpha);
                if (image.Width == 256 && image.Height == 256) bytes = image.Data;
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException) { }

        return _maps[name] = bytes;
    }

    private static uint Fallback(string id) => id switch
    {
        "minecraft:stone" or "minecraft:cobblestone" or "minecraft:stone_bricks" or "minecraft:gravel" => 0x92918c,
        "minecraft:grass_block" or "minecraft:oak_leaves" => 0x8ea477,
        "minecraft:dirt" or "minecraft:oak_log" => 0x866343,
        "minecraft:oak_planks" or "minecraft:chest" => 0xb3915b,
        "minecraft:sand" or "minecraft:sandstone" or "minecraft:end_stone" => 0xd7cb9e,
        "minecraft:snow_block" or "minecraft:iron_block" => 0xdde2df,
        "minecraft:gold_block" => 0xe6c748,
        "minecraft:diamond_block" => 0x58d1c7,
        "minecraft:bricks" or "minecraft:netherrack" => 0x995e52,
        "minecraft:nether_bricks" or "minecraft:obsidian" => 0x443643,
        "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air" => 0x303944,
        _ => 0xa9a191,
    };

    /// <summary>Closes this session's resource readers without changing the original client archive.</summary>
    public void Dispose() { _models?.Dispose(); _archive?.Dispose(); _file?.Dispose(); }

    private sealed record BiomeColors(uint? Grass, uint? Foliage, uint? Water, double Temperature, double Downfall);
}