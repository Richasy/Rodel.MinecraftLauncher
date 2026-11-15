// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Decodes saved section shapes and bit layouts rather than selecting readers by game version.</summary>
internal sealed class SavedChunk
{
    private readonly Dictionary<int, Section> _sections = [];
    private byte[]? _blocks;
    private byte[]? _metadata;
    private object? _biomes;
    private readonly int _height;
    private readonly int _minimum, _maximum;
    private readonly SeedBlockState _air = new() { Id = "minecraft:air" };
    private SavedMapColumn[]? _surface;
    internal SeedPosition[] Structures { get; }
    internal bool PreviewEligible { get; }
    internal long CacheBytes { get; private set; }
    internal bool SurfaceOnly { get; private set; }
    internal List<object> BlockEntities { get; }
    internal (int Minimum, int Maximum) VerticalRange => (_minimum, _sections.Count == 0 ? _maximum : (_sections.Keys.Max() * 16) + 15);
    private readonly Dictionary<string, SeedBlockState> _legacyStates = new(StringComparer.Ordinal);

    internal SavedChunk(Dictionary<string, object> root, int chunkX, int chunkZ)
    {
        var data = root.GetValueOrDefault("Level") as Dictionary<string, object> ?? root;
        var status = data.Text("Status");
        if (status?.StartsWith("minecraft:", StringComparison.Ordinal) == true) status = status[10..];
        PreviewEligible = status is "empty" or "structure_starts" or "structure_references" or "biomes" or "noise";
        BlockEntities = (data.GetValueOrDefault("block_entities") ?? data.GetValueOrDefault("TileEntities")) as List<object> ?? [];
        if ((data.Integer("xPos") is { } x && x != chunkX) || (data.Integer("zPos") is { } z && z != chunkZ))
            throw new InvalidDataException("The saved chunk coordinates do not match its region slot.");
        _blocks = data.GetValueOrDefault("Blocks") as byte[];
        _metadata = data.GetValueOrDefault("Data") as byte[];
        _biomes = data.GetValueOrDefault("Biomes");
        _height = (_blocks?.Length / 256) ?? 0;
        var sections = (data.GetValueOrDefault("sections") ?? data.GetValueOrDefault("Sections")) as List<object> ?? [];
        foreach (var value in sections.OfType<Dictionary<string, object>>())
        {
            if (value.Integer("Y") is not { } y || y is < -2048 or > 2047) continue;
            _sections[(int)y] = new(value);
        }

        if (!PreviewEligible && _blocks is null && _sections.Count == 0 && data.GetValueOrDefault("sections") is not List<object> && data.GetValueOrDefault("Sections") is not List<object>)
            throw new InvalidDataException("The saved chunk contains no readable blocks.");
        _minimum = _sections.Count != 0 ? _sections.Keys.Min() * 16 : 0;
        _maximum = _sections.Count != 0 ? (_sections.Where(pair => pair.Value.HasBlocks).Select(pair => pair.Key).DefaultIfEmpty((_minimum / 16) - 1).Max() * 16) + 15 : _height - 1;
        Structures = ReadStructures(data);
        // Reserve room for all 256 lazily sampled columns as well as retained palette/legacy arrays.
        CacheBytes = 12288L + (_blocks?.Length ?? 0) + (_metadata?.Length ?? 0)
            + (_biomes is int[] ids ? ids.Length * 4L : (_biomes as byte[])?.Length ?? 0) + _sections.Values.Sum(section => section.CacheBytes);
    }

    internal SavedMapColumn Sample(int x, int z, int? slice)
    {
        if (slice is not null && SurfaceOnly) throw new InvalidOperationException("Reload the saved section data before reading a height slice.");
        if (slice is { } sliceY) { var block = State(x, sliceY, z); return new(block, Biome(x, sliceY, z), sliceY, Fluid(block.Id), sliceY, 0); }

        var surface = _surface ??= new SavedMapColumn[256];
        var index = (z * 16) + x;
        if (surface[index] is not null) return surface[index];
        return surface[index] = SampleSurface(x, z);
    }

    internal void RetainSurface()
    {
        if (SurfaceOnly) return;
        for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++) _ = Sample(x, z, null);
        // A map zoom needs 256 columns, not the original lighting, entities or all underground sections.
        _sections.Clear(); _blocks = null; _metadata = null; _biomes = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sample in _surface!) { names.Add(sample.State.Id); names.Add(sample.Biome); }

        CacheBytes = 32768L + names.Sum(name => 32L + (name.Length * 2L)) + (Structures.Length * 128L); SurfaceOnly = true;
    }

    private SavedMapColumn SampleSurface(int x, int z)
    {
        var y = _maximum;
        var fluid = 0;
        var biomeY = y;
        for (; y >= _minimum; y--)
        {
            var block = Block(x, y, z);
            var currentFluid = Fluid(block);
            if (currentFluid != 0) { if (fluid == 0) { fluid = currentFluid; biomeY = y; } continue; }

            if (Air(block)) continue;
            return new(State(x, y, z), Biome(x, fluid == 0 ? y : biomeY, z), y, fluid, fluid == 0 ? y : biomeY, fluid == 0 ? 0 : biomeY - y);
        }

        return new(_air, Biome(x, _minimum, z), _minimum - 1, fluid, fluid == 0 ? _minimum - 1 : biomeY, fluid == 0 ? 0 : biomeY - _minimum + 1);
    }

    private string Block(int x, int y, int z)
    {
        if (_sections.TryGetValue(y >> 4, out var section)) return section.Block(x, y & 15, z);
        if (_blocks is not null && y >= 0 && y < _height)
        {
            var index = (((x * 16) + z) * _height) + y;
            var id = _blocks[index];
            var metadata = Nibble(_metadata, index);
            return LegacyBlock(id, metadata);
        }

        return "minecraft:air";
    }

    internal SeedBlockState State(int x, int y, int z)
    {
        if (SurfaceOnly) throw new InvalidOperationException("Reload section data before reading saved block states.");
        if (_sections.TryGetValue(y >> 4, out var section)) return section.State(x, y & 15, z);
        if (_blocks is null || y < 0 || y >= _height) return _air;
        var id = Block(x, y, z);
        if (!_legacyStates.TryGetValue(id, out var state)) _legacyStates[id] = state = new() { Id = id };
        return state;
    }

    internal string Biome(int x, int y, int z)
    {
        if (_sections.TryGetValue(y >> 4, out var section) && section.Biome(x, y & 15, z) is { } name) return name;
        var values = _biomes;
        var index = (z * 16) + x;
        if (values is byte[] bytes && index < bytes.Length) return LegacyBiome(bytes[index]);
        if (values is int[] ids)
        {
            if (ids.Length > 256) index = (Math.Clamp(y >> 2, 0, (ids.Length / 16) - 1) * 16) + ((z >> 2) * 4) + (x >> 2);
            if (index < ids.Length) return LegacyBiome(ids[index]);
        }

        return "unknown";
    }

    private static SeedPosition[] ReadStructures(Dictionary<string, object> data)
    {
        var structures = (data.GetValueOrDefault("structures") ?? data.GetValueOrDefault("Structures")) as Dictionary<string, object>;
        if ((structures?.GetValueOrDefault("starts") ?? structures?.GetValueOrDefault("Starts")) is not Dictionary<string, object> starts) return [];
        var positions = new List<SeedPosition>();
        foreach (var (name, value) in starts)
        {
            if (value is not Dictionary<string, object> start || start.Text("id") == "INVALID") continue;
            var box = start.GetValueOrDefault("BB") as int[];
            if (box is not { Length: 6 } && (start.GetValueOrDefault("Children") ?? start.GetValueOrDefault("children")) is List<object> children)
            {
                var boxes = children.OfType<Dictionary<string, object>>().Select(child => child.GetValueOrDefault("BB") as int[]).OfType<int[]>().Where(bounds => bounds.Length == 6).ToArray();
                if (boxes.Length != 0)
                {
                    box = [boxes.Min(bounds => bounds[0]), boxes.Min(bounds => bounds[1]), boxes.Min(bounds => bounds[2]),
                    boxes.Max(bounds => bounds[3]), boxes.Max(bounds => bounds[4]), boxes.Max(bounds => bounds[5])];
                }
            }

            if (box is not { Length: 6 }) continue;
            positions.Add(new()
            {
                Id = name.StartsWith("minecraft:", StringComparison.Ordinal) ? name[10..] : name,
                Kind = "structure",
                X = (int)(((long)box[0] + box[3]) / 2),
                Y = box[1],
                Z = (int)(((long)box[2] + box[5]) / 2),
                Bounds = box,
                Pieces = (start.GetValueOrDefault("Children") as List<object>)?.Count ?? 0,
            });
        }

        return [.. positions];
    }

    private static bool Air(string block) => block is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air";
    private static int Fluid(string block) => block is "minecraft:water" or "minecraft:flowing_water" ? 1
        : block is "minecraft:lava" or "minecraft:flowing_lava" ? 2 : 0;
    private static int Nibble(byte[]? bytes, int index) => bytes is not null && (index >> 1) < bytes.Length ? (bytes[index >> 1] >> ((index & 1) * 4)) & 15 : 0;

    // These are stored numeric identifiers of the legacy save format, not a version/generation table.
    internal static string LegacyBlock(int id, int metadata = 0) => id switch
    {
        0 => "minecraft:air",
        1 => "minecraft:stone",
        2 => "minecraft:grass_block",
        3 => "minecraft:dirt",
        4 => "minecraft:cobblestone",
        5 => "minecraft:oak_planks",
        7 => "minecraft:bedrock",
        8 or 9 => "minecraft:water",
        10 or 11 => "minecraft:lava",
        12 => "minecraft:sand",
        13 => "minecraft:gravel",
        14 => "minecraft:gold_ore",
        15 => "minecraft:iron_ore",
        16 => "minecraft:coal_ore",
        17 => "minecraft:oak_log",
        18 => "minecraft:oak_leaves",
        20 => "minecraft:glass",
        24 => "minecraft:sandstone",
        35 => "legacy:wool_" + metadata,
        41 => "minecraft:gold_block",
        42 => "minecraft:iron_block",
        45 => "minecraft:bricks",
        49 => "minecraft:obsidian",
        54 => "minecraft:chest",
        57 => "minecraft:diamond_block",
        79 => "minecraft:ice",
        80 => "minecraft:snow_block",
        82 => "minecraft:clay",
        87 => "minecraft:netherrack",
        88 => "minecraft:soul_sand",
        89 => "minecraft:glowstone",
        98 => "minecraft:stone_bricks",
        112 => "minecraft:nether_bricks",
        121 => "minecraft:end_stone",
        _ => "legacy:block_" + id + "_" + metadata,
    };

    internal static string LegacyBiome(int id) => id switch
    {
        0 => "ocean",
        1 => "plains",
        2 => "desert",
        3 => "windswept_hills",
        4 => "forest",
        5 => "taiga",
        6 => "swamp",
        7 => "river",
        8 => "nether_wastes",
        9 => "the_end",
        10 => "frozen_ocean",
        11 => "frozen_river",
        12 => "snowy_plains",
        13 => "snowy_slopes",
        14 or 15 => "mushroom_fields",
        16 => "beach",
        17 => "desert",
        18 => "forest",
        19 => "taiga",
        20 => "windswept_hills",
        21 or 22 or 23 => "jungle",
        24 => "deep_ocean",
        25 => "stony_shore",
        26 => "snowy_beach",
        27 or 28 => "birch_forest",
        29 => "dark_forest",
        30 or 31 => "snowy_taiga",
        32 or 33 => "old_growth_pine_taiga",
        34 => "windswept_forest",
        35 or 36 => "savanna",
        37 or 39 => "badlands",
        38 => "wooded_badlands",
        127 => "the_void",
        129 => "sunflower_plains",
        132 => "flower_forest",
        140 => "ice_spikes",
        165 or 166 => "eroded_badlands",
        168 or 169 => "bamboo_jungle",
        _ => "legacy:biome_" + id,
    };

    private sealed class Section
    {
        private readonly byte[]? _ids, _add, _metadata;
        private readonly Palette? _blocks;
        private readonly Palette? _biomes;
        private readonly SeedBlockState[]? _states;
        internal bool HasBlocks => _blocks?.HasBlocks ?? (_ids?.Any(id => id != 0) == true);
        internal long CacheBytes => 256L + (_ids?.Length ?? 0) + (_add?.Length ?? 0) + (_metadata?.Length ?? 0) + (_blocks?.CacheBytes ?? 0) + (_biomes?.CacheBytes ?? 0);
        internal Section(Dictionary<string, object> data)
        {
            _ids = data.GetValueOrDefault("Blocks") as byte[];
            _add = data.GetValueOrDefault("Add") as byte[];
            _metadata = data.GetValueOrDefault("Data") as byte[];
            var modern = data.Compound("block_states");
            var blocks = (modern.GetValueOrDefault("palette") ?? data.GetValueOrDefault("Palette")) as List<object>;
            if (blocks is not null)
            {
                _states = [.. blocks.Select(BlockState)];
                _blocks = new([.. _states.Select(state => state.Id)],
                    (modern.GetValueOrDefault("data") ?? data.GetValueOrDefault("BlockStates")) as long[], 4096, 4);
            }

            var biomes = data.Compound("biomes");
            if (biomes.GetValueOrDefault("palette") is List<object> ids)
            {
                _biomes = new([.. ids.OfType<string>().Select(id => id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id)],
                    biomes.GetValueOrDefault("data") as long[], 64, 1);
            }
        }

        private static SeedBlockState BlockState(object value)
        {
            var state = value as Dictionary<string, object>;
            var name = value as string ?? state?.Text("Name") ?? state?.Text("id") ?? state?.Text(string.Empty);
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("The saved block palette contains an unreadable state.");
            var properties = name.IndexOf('[');
            var result = new SeedBlockState { Id = properties < 0 ? name : name[..properties], DefaultsOmitted = state?.ContainsKey("Name") != true };
            if ((state?.GetValueOrDefault("Properties") ?? state?.GetValueOrDefault("properties")) is Dictionary<string, object> fields)
                foreach (var (key, property) in fields) if (property is string text) result.Properties[key] = text;
            if (properties >= 0 && name.EndsWith(']'))
            {
                foreach (var pair in name[(properties + 1)..^1].Split(','))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2) result.Properties[parts[0]] = parts[1];
                }
            }

            return result;
        }

        internal SeedBlockState State(int x, int y, int z) => _states is not null && _blocks is not null
            ? _states[_blocks.Index((y * 256) + (z * 16) + x)] : new() { Id = Block(x, y, z) };

        internal string Block(int x, int y, int z)
        {
            var index = (y * 256) + (z * 16) + x;
            if (_blocks is not null) return _blocks.At(index);
            if (_ids is { } ids && index < ids.Length)
                return LegacyBlock(ids[index] | (Nibble(_add, index) << 8), Nibble(_metadata, index));
            return "minecraft:air";
        }

        internal string? Biome(int x, int y, int z) => _biomes?.At(((y >> 2) * 16) + ((z >> 2) * 4) + (x >> 2));
    }

    private sealed class Palette
    {
        private readonly string[] _values;
        private readonly long[] _data;
        private readonly int _bits;
        private readonly bool _dense;
        internal bool HasBlocks => _values.Any(value => !Air(value));
        internal long CacheBytes => 128L + (_data.Length * 8L) + _values.Sum(value => 32L + (value.Length * 2L));

        internal Palette(string[] values, long[]? data, int count, int minimumBits)
        {
            if (values.Length == 0) throw new InvalidDataException("An empty saved block/biome palette is invalid.");
            _values = values; _data = data ?? [];
            _bits = Math.Max(minimumBits, 32 - BitOperations.LeadingZeroCount((uint)(values.Length - 1)));
            if (values.Length == 1 && _data.Length == 0) return;
            var denseLength = ((count * _bits) + 63) / 64;
            var paddedLength = (count + (64 / _bits) - 1) / (64 / _bits);
            _dense = _data.Length == denseLength && denseLength != paddedLength;
            if (_data.Length != denseLength && _data.Length != paddedLength)
                throw new InvalidDataException("The saved palette has an incomplete packed array.");
        }

        internal string At(int index) => _values[Index(index)];

        internal int Index(int index)
        {
            if (_values.Length == 1 && _data.Length == 0) return 0;
            var bit = _dense ? index * _bits : index % (64 / _bits) * _bits;
            var slot = _dense ? bit / 64 : index / (64 / _bits);
            var shift = bit & 63;
            var value = (ulong)_data[slot] >> shift;
            if (_dense && shift + _bits > 64) value |= (ulong)_data[slot + 1] << (64 - shift);
            var paletteIndex = (int)(value & ((1UL << _bits) - 1));
            if (paletteIndex >= _values.Length) throw new InvalidDataException("A saved palette index is outside its palette.");
            return paletteIndex;
        }
    }
}