// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Buffers.Binary;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects frozen biome/state transport and reassembly; the native color producer alone is substituted, not the partitioner or JSON contract.</summary>
[TestClass]
public sealed class SeedTintTransportTests
{
    /// <summary>Review: a large snapshot needs multiple bounded requests. Wrong axis order, halo cropping, or state-only deduplication would color another position or biome; coordinates and independently encoded biome identities reveal each error.</summary>
    [TestMethod]
    public async Task FrozenTints_VolumePartitionsPreserveEveryVoxelAndDistinctBiomes()
    {
        var source = Region(new() { MinX = -21, MaxX = 18, MinY = 55, MaxY = 86, MinZ = 72, MaxZ = 111 }, false);
        var requests = 0;
        var result = await SeedTintResolver.ResolveAsync(source, (query, _) =>
        {
            requests++;
            Assert.IsLessThanOrEqualTo(32768L, query.Bounds.Volume);
            Assert.HasCount(1, query.Palette, "Unused global states must not inflate every worker request.");
            Assert.IsLessThan(900001, JsonSerializer.SerializeToUtf8Bytes(new SeedWorkerRequest(null, "tints", Tints: query), SeedWorkerJsonContext.Default.SeedWorkerRequest).Length);
            return Task.FromResult(CoordinateColors(query));
        }, CancellationToken.None);

        Assert.IsGreaterThan(1, requests);
        Assert.IsEmpty(source.TintIndices, "Completing a derived snapshot must not mutate the frozen input.");
        VerifyCoordinates(result);
        SeedValidation.RegionColors(result);
    }

    /// <summary>Review: a small volume can exceed the 1 MiB worker line limit through its state palette. Actual UTF-8 request bytes, dense palette remapping and final voxel colors distinguish this from the voxel-count partition case.</summary>
    [TestMethod]
    public async Task FrozenTints_LargePalettePartitionsBySerializedBytesBeforeSending()
    {
        var source = Region(new() { MaxX = 31, MaxZ = 31 }, true);
        var requests = 0;
        var result = await SeedTintResolver.ResolveAsync(source, (query, _) =>
        {
            requests++;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new SeedWorkerRequest(null, "tints", Tints: query), SeedWorkerJsonContext.Default.SeedWorkerRequest);
            Assert.IsLessThan(900001, bytes.Length, "A palette must fit the existing line cap even when the volume alone fits.");
            Assert.IsLessThan(source.Palette.Length, query.Palette.Length);
            return Task.FromResult(CoordinateColors(query));
        }, CancellationToken.None);

        Assert.IsGreaterThan(1, requests);
        VerifyCoordinates(result);
    }

    /// <summary>Review: an indivisible over-budget state cannot be repaired by sending it to a worker and losing the session. It must fail locally without any transport call or input mutation.</summary>
    [TestMethod]
    public async Task FrozenTints_OneOversizedStateIsRejectedBeforeTransport()
    {
        var source = Region(new(), false);
        source.Palette[0].Properties["payload"] = new('a', 910000);
        var calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SeedTintResolver.ResolveAsync(source, (query, _) =>
        {
            calls++;
            return Task.FromResult(CoordinateColors(query));
        }, CancellationToken.None));
        Assert.AreEqual(0, calls);
        Assert.IsEmpty(source.TintIndices);
    }

    /// <summary>Review: cancellation after the final native response must not publish a completed snapshot, while an invalid palette index must not leak an incomplete one. Both outcomes preserve the original readable frozen data.</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task FrozenTints_InterruptedOrMalformedResponseDoesNotCommitColors(bool cancel)
    {
        var source = Region(new() { MaxX = 1 }, false);
        using var cancellation = new CancellationTokenSource();
        var operation = SeedTintResolver.ResolveAsync(source, (query, _) =>
        {
            var response = CoordinateColors(query);
            if (cancel) cancellation.Cancel();
            else BinaryPrimitives.WriteUInt16LittleEndian(response.TintIndices, ushort.MaxValue);
            return Task.FromResult(response);
        }, cancellation.Token);
        if (cancel) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => operation);
        else await Assert.ThrowsExactlyAsync<InvalidDataException>(() => operation);
        Assert.IsEmpty(source.TintPalette);
        Assert.IsEmpty(source.TintIndices);
        Assert.HasCount(4, source.Blocks);
    }

    private static SeedRegionResult Region(SeedBounds bounds, bool largePalette)
    {
        var volume = (int)bounds.Volume;
        var blocks = new byte[volume * 2];
        var biomes = new byte[blocks.Length];
        var palette = largePalette
            ? [.. Enumerable.Range(0, volume).Select(i => new SeedBlockState { Id = "minecraft:stone", Properties = new() { ["proof"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture), ["padding"] = new('a', 1600) } })]
            : new SeedBlockState[] { new() { Id = "minecraft:grass_block" }, new() { Id = "minecraft:unused_fixture_state" } };
        for (var i = 0; i < volume; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(blocks.AsSpan(i * 2), (ushort)(largePalette ? i : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(biomes.AsSpan(i * 2), (ushort)(i % 2));
        }

        return new() { Bounds = bounds, Palette = palette, Blocks = blocks, BiomePalette = ["minecraft:jungle", "minecraft:swamp"], Biomes = biomes };
    }

    private static SeedRegionTints CoordinateColors(SeedFrozenTintQuery query)
    {
        var palette = new List<SeedRegionTint>();
        var indices = new byte[query.Blocks.Length];
        var i = 0;
        for (var y = query.Bounds.MinY; y <= query.Bounds.MaxY; y++)
        {
            for (var z = query.Bounds.MinZ; z <= query.Bounds.MaxZ; z++)
            {
                for (var x = query.Bounds.MinX; x <= query.Bounds.MaxX; x++, i++)
                {
                    var state = query.Palette[BinaryPrimitives.ReadUInt16LittleEndian(query.Blocks.AsSpan(i * 2))];
                    var biome = query.BiomePalette[BinaryPrimitives.ReadUInt16LittleEndian(query.Biomes.AsSpan(i * 2))];
                    palette.Add(new() { BiomeId = biome, Colors = [CoordinateColor(x, y, z), state.Properties.TryGetValue("proof", out var proof) ? int.Parse(proof, System.Globalization.CultureInfo.InvariantCulture) : 77] });
                    BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(i * 2), (ushort)i);
                }
            }
        }

        return new([.. palette], indices);
    }

    private static void VerifyCoordinates(SeedRegionResult result)
    {
        var i = 0;
        for (var y = result.Bounds.MinY; y <= result.Bounds.MaxY; y++)
        {
            for (var z = result.Bounds.MinZ; z <= result.Bounds.MaxZ; z++)
            {
                for (var x = result.Bounds.MinX; x <= result.Bounds.MaxX; x++, i++)
                {
                    var tint = result.TintPalette[BinaryPrimitives.ReadUInt16LittleEndian(result.TintIndices.AsSpan(i * 2))];
                    Assert.AreEqual(i % 2 == 0 ? "minecraft:jungle" : "minecraft:swamp", tint.BiomeId, $"Biome at {x},{y},{z}");
                    Assert.AreEqual(CoordinateColor(x, y, z), tint.Colors[0], $"Position at {x},{y},{z}");
                    Assert.AreEqual(result.Palette.Length > 2 ? i : 77, tint.Colors[1], $"State at {x},{y},{z}");
                }
            }
        }
    }

    private static int CoordinateColor(int x, int y, int z) => ((x & 255) << 16) | ((y & 255) << 8) | (z & 255);
}