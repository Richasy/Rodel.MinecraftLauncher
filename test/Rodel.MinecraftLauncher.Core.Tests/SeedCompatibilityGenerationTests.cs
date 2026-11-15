// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Opt-in cross-ABI integration using isolated original game runtimes, independent of modern-version registry counts.</summary>
[TestClass]
[TestCategory("Live")]
[DoNotParallelize] // Each real native JVM owns substantial registries/chunk caches; bound integration-host memory.
public sealed class SeedCompatibilityGenerationTests
{
    /// <summary>Provides cancellation to original JVMs and isolated cache ownership.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Reads explicitly supplied installed-runtime profiles; no player world is opened or modified.</summary>
    public static IEnumerable<object?[]> Profiles()
    {
        var file = Environment.GetEnvironmentVariable("RODEL_SEED_COMPAT_PROFILES");
        if (string.IsNullOrWhiteSpace(file)) { yield return [null]; yield break; }

        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        foreach (var item in document.RootElement.EnumerateArray())
            yield return [item.Deserialize(SeedJsonContext.Default.SeedRuntimeProfile) ?? throw new InvalidDataException("An original compatibility profile is missing.")];
    }

    /// <summary>
    /// The same actual world positions must survive different tile partitions, and a complete native
    /// input error must leave an uncached query usable. Modern-count/temple tests cannot cover legacy
    /// registries or API selection. Expectations come from the partition invariant, not copied output
    /// constants; original generation and the Core/JVM transport remain real.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(Profiles))]
    public async Task OriginalApiFamily_PreservesPartitionsAndRecoversAfterNativeInputError(SeedRuntimeProfile? profile)
    {
        RequireProfile(profile);
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile!, TestContext.CancellationToken);
            var catalog = await session.GetCatalogAsync(cancellationToken: TestContext.CancellationToken);
            var dimension = catalog.Dimensions.Single(item => item.Id == "overworld");
            var query = Map(session.Profile);
            var complete = await session.MapAsync(query, TestContext.CancellationToken);
            Assert.AreEqual(session.MinecraftVersion, catalog.Version);
            Assert.IsTrue(complete.Palette.All(id => dimension.Biomes.Contains(id)));
            for (var row = 0; row < 2; row++)
            {
                for (var column = 0; column < 2; column++)
                {
                    var part = await session.MapAsync(query with { MinX = query.MinX + (column * 2 * query.Step), MinZ = query.MinZ + (row * 2 * query.Step), Side = 2 }, TestContext.CancellationToken);
                    var expected = Enumerable.Range(0, 2).SelectMany(z => Enumerable.Range(0, 2).Select(x => Cell(complete, (((row * 2) + z) * 4) + (column * 2) + x))).ToArray();
                    CollectionAssert.AreEqual(expected, Enumerable.Range(0, 4).Select(index => Cell(part, index)).ToArray());
                }
            }

            await Assert.ThrowsAsync<LauncherOperationException>(() => session.MapAsync(query with { Y = dimension.MinY - 1 }, TestContext.CancellationToken));
            var recovered = await session.MapAsync(query with { MinX = query.MinX + 64 }, TestContext.CancellationToken);
            Assert.IsFalse(recovered.Cached, "A cached success would not prove recovery of the original worker.");
            Assert.HasCount(16, recovered.Biomes);
        });
    }

    /// <summary>
    /// A real FULL-generation export must be readable after native saving and storage-lock release,
    /// then survive reopening from its durable Core cache. Synthetic scene/NBT tests cannot expose
    /// copying a live session.lock, incomplete native flushing or returning a temporary-file reference.
    /// Only an owned helper world is created; the profile contributes read-only game dependencies.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(Profiles))]
    public async Task OriginalApiFamily_FreezesFullRegionAndReopensDurableResult(SeedRuntimeProfile? profile)
    {
        RequireProfile(profile);
        await ReportStorage.RunAsync(async paths =>
        {
            SeedRegionQuery query;
            SeedRegionResult original;
            await using (var session = await new SeedExplorerService(paths).OpenAsync(profile!, TestContext.CancellationToken))
            {
                var catalog = await session.GetCatalogAsync(cancellationToken: TestContext.CancellationToken);
                var dimension = catalog.Dimensions.Single(item => item.Id == "overworld");
                var y = Math.Clamp(48, dimension.MinY, dimension.MinY + dimension.Height - 4);
                query = new() { Profile = session.Profile, Seed = "357913", Bounds = new() { MinX = 144, MaxX = 147, MinY = y, MaxY = y + 3, MinZ = 240, MaxZ = 243 }, IncludeBlocks = true, IncludeLoot = false };
                original = await session.RegionAsync(query, cancellationToken: TestContext.CancellationToken);
                Assert.IsFalse(original.Cached);
                Assert.HasCount(128, original.Blocks);
                Assert.AreEqual(64L, original.Materials.Sum(item => item.Count));
            }

            await using var reopened = await new SeedExplorerService(paths).OpenAsync(profile!, TestContext.CancellationToken);
            var cached = await reopened.RegionAsync(query, cancellationToken: TestContext.CancellationToken);
            Assert.IsTrue(cached.Cached);
            Assert.AreEqual(original.Fingerprint, cached.Fingerprint);
            CollectionAssert.AreEqual(original.Blocks, cached.Blocks);
            CollectionAssert.AreEqual(original.Materials, cached.Materials);
        });
    }

    private static void RequireProfile(SeedRuntimeProfile? profile)
    {
        if (profile is null) Assert.Inconclusive("Set RODEL_SEED_COMPAT_PROFILES to an array of isolated installed-runtime profiles.");
    }

    private static SeedMapQuery Map(SeedRuntimeProfile profile) => new() { Profile = profile, Seed = "357913", MinX = 144, MinZ = 240, Side = 4, Step = 4, Terrain = true, Structures = [] };
    private static (string? Biome, int Height, int Fluid, uint? Color) Cell(SeedMapTile tile, int index) =>
        (tile.Biomes[index] < 0 ? null : tile.Palette[tile.Biomes[index]], tile.Heights[index], tile.Fluids[index], tile.SurfaceColors.Length == 0 ? null : tile.SurfaceColors[index]);
}