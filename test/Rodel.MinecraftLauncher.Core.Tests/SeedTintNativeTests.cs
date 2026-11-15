// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Buffers.Binary;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Exercises installed original color maps/providers through the real worker, independently of synthetic partition and renderer cases.</summary>
[TestClass]
[TestCategory("Live")]
public sealed class SeedTintNativeTests
{
    /// <summary>Gets or sets the runner's cancellation and diagnostic context.</summary>
    public TestContext TestContext { get; set; }

    /// <summary>Review: headless color tables were uninitialized, and a blanket green fix would tint constant foliage, redstone and uncolored faces incorrectly. A frozen state/biome volume crosses the real provider protocol without generating terrain or opening a save; unknown biomes must stay unavailable.</summary>
    [TestMethod]
    public async Task FrozenTints_OriginalProvidersUseActualBiomesStatesAndInitializedColorMaps()
    {
        var path = Environment.GetEnvironmentVariable("RODEL_SEED_TEST_PROFILE");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set RODEL_SEED_TEST_PROFILE to opt into installed original-JVM integration.");
        var profile = JsonSerializer.Deserialize(File.ReadAllText(path!), SeedJsonContext.Default.SeedRuntimeProfile)
            ?? throw new InvalidDataException("Missing original runtime profile.");
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            var source = new SeedRegionResult
            {
                Bounds = new() { MinX = -10, MaxX = -1, MinY = 64, MaxY = 64, MinZ = -20, MaxZ = -20 },
                Palette = [new() { Id = "minecraft:grass_block" }, new() { Id = "minecraft:oak_leaves" },
                    new() { Id = "minecraft:spruce_leaves" }, new() { Id = "minecraft:birch_leaves" }, new() { Id = "minecraft:cherry_leaves" },
                    new() { Id = "minecraft:redstone_wire", Properties = new() { ["power"] = "0" } },
                    new() { Id = "minecraft:redstone_wire", Properties = new() { ["power"] = "15" } }],
                Blocks = Indices(0, 0, 1, 2, 3, 4, 5, 6, 0, 1),
                BiomePalette = ["minecraft:bamboo_jungle", "minecraft:swamp", "rodel:unknown"],
                Biomes = Indices(0, 1, 0, 1, 1, 0, 0, 0, 2, 2),
            };
            var result = await session.ResolveTintsAsync(source, TestContext.CancellationToken);
            int[] Colors(int x) => result.TintPalette[BinaryPrimitives.ReadUInt16LittleEndian(result.TintIndices.AsSpan(x * 2))].Colors;
            Assert.IsGreaterThan(0, Colors(0).Single(), "An uninitialized headless grass table returns black.");
            Assert.AreNotEqual(Colors(0).Single(), Colors(1).Single(), "The same grass state must retain the original swamp biome modifier.");
            Assert.AreNotEqual(Colors(0).Single(), Colors(2).Single(), "Grass and foliage use different original color maps.");
            Assert.AreEqual(0x619961, Colors(3).Single(), "Original spruce foliage is constant, independent of swamp grass.");
            Assert.AreEqual(0x80A755, Colors(4).Single(), "Original birch foliage has its own constant provider.");
            Assert.IsEmpty(Colors(5), "Cherry leaves use their authored texture without a foliage tint.");
            Assert.AreNotEqual(Colors(6).Single(), Colors(7).Single(), "The native redstone provider depends on the actual power property.");
            Assert.AreEqual(-1, Colors(8).Single());
            Assert.AreEqual(-1, Colors(9).Single());
            Assert.IsEmpty(source.TintIndices);
            Assert.IsFalse(Directory.EnumerateDirectories(Path.Combine(paths.CacheDirectory, "seed-explorer", "workers"), "request-*", SearchOption.AllDirectories).Any(), "Frozen colors must not initialize an isolated generation world.");
        });
    }

    private static byte[] Indices(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        return bytes;
    }
}