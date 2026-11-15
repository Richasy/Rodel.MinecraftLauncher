// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Buffers.Binary;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Partitions frozen state/biome data below the existing worker line budget and preserves voxel ownership while merging native colors.</summary>
internal static class SeedTintResolver
{
    private const int MaximumVolume = 32768;
    private const int MaximumRequestBytes = 900000;

    internal static async Task<SeedRegionResult> ResolveAsync(SeedRegionResult region,
        Func<SeedFrozenTintQuery, CancellationToken, Task<SeedRegionTints>> read, CancellationToken ct)
    {
        var bounds = region.Bounds;
        if (bounds.MaxX < bounds.MinX || bounds.MaxY < bounds.MinY || bounds.MaxZ < bounds.MinZ
            || (long)bounds.MaxX - bounds.MinX >= 256 || (long)bounds.MaxZ - bounds.MinZ >= 256
            || bounds.MinY < -32768 || bounds.MaxY > 32767 || bounds.MinX < -30000000 || bounds.MaxX >= 30000000 || bounds.MinZ < -30000000 || bounds.MaxZ >= 30000000
            || bounds.Volume is < 1 or > 4194304 || region.Blocks.LongLength != bounds.Volume * 2 || region.Biomes.LongLength != bounds.Volume * 2
            || region.Palette.Length is < 1 or > 65536 || region.BiomePalette.Length is < 1 or > 65536)
        {
            throw new InvalidDataException("Original tint resolution requires a complete bounded frozen block and biome volume.");
        }

        var pending = new Stack<SeedBounds>(); pending.Push(bounds with { });
        var palette = new List<SeedRegionTint>(); var identities = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var indices = new byte[region.Blocks.Length];
        while (pending.TryPop(out var part))
        {
            ct.ThrowIfCancellationRequested();
            var expanded = Expand(part, bounds);
            if (expanded.Volume > MaximumVolume) { Split(part, pending); continue; }

            var request = Subset(region, expanded);
            if (JsonSerializer.SerializeToUtf8Bytes(new SeedWorkerRequest(null, "tints", Tints: request), SeedWorkerJsonContext.Default.SeedWorkerRequest).Length > MaximumRequestBytes)
            { Split(part, pending); continue; }

            var response = await read(request, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Validate(response, expanded.Volume);
            var remap = new ushort[response.TintPalette.Length];
            for (var i = 0; i < remap.Length; i++)
            {
                var tint = response.TintPalette[i];
                var key = tint.BiomeId + ":" + string.Join(',', tint.Colors);
                if (!identities.TryGetValue(key, out var index))
                {
                    if (palette.Count >= 65536) throw new InvalidDataException("Original tint palette exceeds the frozen volume budget.");
                    index = (ushort)palette.Count; palette.Add(tint with { Colors = [.. tint.Colors] }); identities.Add(key, index);
                }

                remap[i] = index;
            }

            for (var y = part.MinY; y <= part.MaxY; y++)
            {
                for (var z = part.MinZ; z <= part.MaxZ; z++)
                {
                    for (var x = part.MinX; x <= part.MaxX; x++)
                    {
                        var local = Offset(expanded, x, y, z);
                        var global = Offset(bounds, x, y, z);
                        var index = BinaryPrimitives.ReadUInt16LittleEndian(response.TintIndices.AsSpan(local * 2, 2));
                        BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(global * 2, 2), remap[index]);
                    }
                }
            }
        }

        return region with { TintPalette = [.. palette], TintIndices = indices };
    }

    private static SeedFrozenTintQuery Subset(SeedRegionResult region, SeedBounds bounds)
    {
        var blocks = new byte[checked((int)bounds.Volume * 2)]; var biomes = new byte[blocks.Length];
        var states = new List<SeedBlockState>(); var names = new List<string>();
        var stateIds = new Dictionary<ushort, ushort>(); var biomeIds = new Dictionary<ushort, ushort>();
        var position = 0;
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var z = bounds.MinZ; z <= bounds.MaxZ; z++)
            {
                for (var x = bounds.MinX; x <= bounds.MaxX; x++, position++)
                {
                    var source = Offset(region.Bounds, x, y, z);
                    var state = BinaryPrimitives.ReadUInt16LittleEndian(region.Blocks.AsSpan(source * 2, 2));
                    var biome = BinaryPrimitives.ReadUInt16LittleEndian(region.Biomes.AsSpan(source * 2, 2));
                    if (state >= region.Palette.Length || biome >= region.BiomePalette.Length) throw new InvalidDataException("A frozen block or biome palette index is invalid.");
                    if (!stateIds.TryGetValue(state, out var stateIndex)) { stateIndex = (ushort)states.Count; states.Add(region.Palette[state]); stateIds.Add(state, stateIndex); }

                    if (!biomeIds.TryGetValue(biome, out var biomeIndex)) { biomeIndex = (ushort)names.Count; names.Add(region.BiomePalette[biome]); biomeIds.Add(biome, biomeIndex); }

                    BinaryPrimitives.WriteUInt16LittleEndian(blocks.AsSpan(position * 2, 2), stateIndex);
                    BinaryPrimitives.WriteUInt16LittleEndian(biomes.AsSpan(position * 2, 2), biomeIndex);
                }
            }
        }

        return new(bounds, [.. states], blocks, [.. names], biomes);
    }

    private static int Offset(SeedBounds b, int x, int y, int z) => checked(((((y - b.MinY) * (b.MaxZ - b.MinZ + 1)) + z - b.MinZ) * (b.MaxX - b.MinX + 1)) + x - b.MinX);

    private static SeedBounds Expand(SeedBounds part, SeedBounds bounds) => new()
    {
        MinX = Math.Max(bounds.MinX, part.MinX - 1),
        MaxX = Math.Min(bounds.MaxX, part.MaxX + 1),
        MinY = Math.Max(bounds.MinY, part.MinY - 1),
        MaxY = Math.Min(bounds.MaxY, part.MaxY + 1),
        MinZ = Math.Max(bounds.MinZ, part.MinZ - 1),
        MaxZ = Math.Min(bounds.MaxZ, part.MaxZ + 1),
    };

    private static void Split(SeedBounds bounds, Stack<SeedBounds> pending)
    {
        var x = bounds.MaxX - bounds.MinX; var y = bounds.MaxY - bounds.MinY; var z = bounds.MaxZ - bounds.MinZ;
        if (x >= y && x >= z && x > 0) { var mid = bounds.MinX + (x / 2); pending.Push(bounds with { MinX = mid + 1 }); pending.Push(bounds with { MaxX = mid }); }
        else if (y >= z && y > 0) { var mid = bounds.MinY + (y / 2); pending.Push(bounds with { MinY = mid + 1 }); pending.Push(bounds with { MaxY = mid }); }
        else if (z > 0) { var mid = bounds.MinZ + (z / 2); pending.Push(bounds with { MinZ = mid + 1 }); pending.Push(bounds with { MaxZ = mid }); }
        else
        {
            throw new InvalidDataException("A frozen state exceeds the original tint request size budget.");
        }
    }

    private static void Validate(SeedRegionTints response, long volume)
    {
        if (response.TintPalette is not { Length: >= 1 and <= 65536 } || response.TintIndices is null || response.TintIndices.LongLength != volume * 2
            || response.TintPalette.Any(tint => tint is null || string.IsNullOrWhiteSpace(tint.BiomeId) || tint.BiomeId.Length > 256
                || tint.Colors is not { Length: <= 64 } || tint.Colors.Any(color => color is < -1 or > 0xFFFFFF)))
        {
            throw new InvalidDataException("The original tint response is incomplete or contains invalid colors.");
        }

        for (var offset = 0; offset < response.TintIndices.Length; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(response.TintIndices.AsSpan(offset, 2)) >= response.TintPalette.Length)
                throw new InvalidDataException("The original tint response contains an invalid palette index.");
        }
    }
}