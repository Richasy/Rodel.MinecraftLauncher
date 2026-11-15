// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Checks decorated map transport against an independently requested original frozen volume; it never opens a player save.</summary>
[TestClass]
[TestCategory("Live")]
public sealed class SeedSurfaceNativeTests
{
    /// <summary>Gets or sets cancellation and diagnostics for the actual native worker.</summary>
    public TestContext TestContext { get; set; }

    /// <summary>Review: a base-height/biome-only map cannot show actual crowns or their authored autumn colors. The separately requested FULL voxel volume supplies expected top heights, while warm-color and cached-raster checks distinguish green-palette substitution and a lost decorated request. Synthetic shading and scene cases cannot establish this worker wiring.</summary>
    [TestMethod]
    public async Task DecoratedMap_RetainsOriginalCanopiesAutumnColorsAndCachedPixels()
    {
        var path = Environment.GetEnvironmentVariable("RODEL_SEED_TEST_PROFILE");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set RODEL_SEED_TEST_PROFILE to opt into installed original-JVM integration.");
        var profile = JsonSerializer.Deserialize(File.ReadAllText(path!), SeedJsonContext.Default.SeedRuntimeProfile)
            ?? throw new InvalidDataException("Missing original runtime profile.");
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            Assert.IsTrue((await session.GetCatalogAsync(cancellationToken: TestContext.CancellationToken)).DecoratedMaps);
            var query = new SeedMapQuery
            {
                Profile = session.Profile,
                Seed = "2",
                MinX = 0,
                MinZ = 0,
                Side = 32,
                Step = 1,
                Decorated = true,
                Structures = [],
                World = new() { Preset = "single_biome", Biome = "dappled_forest" },
            };
            var map = await session.MapAsync(query, TestContext.CancellationToken);
            Assert.IsTrue(map.Decorated); Assert.IsFalse(map.Cached);
            Assert.IsEmpty(map.Warnings, "Authored forest materials must resolve instead of silently satisfying warm-color checks with a neutral fallback.");
            Assert.IsGreaterThan(5, map.SurfaceColors.Distinct().Count());
            var region = await session.RegionAsync(new()
            {
                Profile = session.Profile,
                Seed = query.Seed,
                World = query.World,
                IncludeBlocks = true,
                IncludeLoot = false,
                Bounds = new() { MinX = 0, MaxX = 31, MinZ = 0, MaxZ = 31, MinY = -64, MaxY = 319 },
            }, cancellationToken: TestContext.CancellationToken);
            var crowns = 0;
            for (var column = 0; column < 1024; column++)
            {
                var top = 319;
                SeedBlockState state;
                do
                {
                    var offset = (((top + 64) * 1024) + column) * 2;
                    state = region.Palette[region.Blocks[offset] | (region.Blocks[offset + 1] << 8)];
                    if (state.Id is not ("minecraft:air" or "minecraft:cave_air" or "minecraft:void_air")) break;
                } while (--top >= -64);
                Assert.AreEqual(top, map.SurfaceHeights[column], $"Column {column} must retain the original generated top.");
                if (state.Id.EndsWith("_leaves", StringComparison.Ordinal)) crowns++;
            }

            Assert.IsGreaterThan(32, crowns, "The chosen original forest must contain actual crowns, not a painted biome region.");
            Assert.IsGreaterThan(32, map.SurfaceColors.Count(color => (color >> 16) > ((color >> 8) & 255) && ((color >> 8) & 255) > (color & 255)));
            var cached = await session.MapAsync(query, TestContext.CancellationToken);
            Assert.IsTrue(cached.Cached); Assert.IsTrue(cached.Decorated);
            CollectionAssert.AreEqual(SeedMapImage.Pixels(map), SeedMapImage.Pixels(cached));
        });
    }
}