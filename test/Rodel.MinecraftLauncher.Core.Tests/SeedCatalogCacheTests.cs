// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects metadata reuse without substituting a generator or treating cached choices as computed world data.</summary>
[TestClass]
public sealed class SeedCatalogCacheTests
{
    /// <summary>Provides cancellation for isolated cache operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Reopen an actual disk cache with a producer that must never run, then change the runtime fingerprint.
    /// This detects eager JVM startup or cross-runtime reuse; only the original catalog producer is substituted.
    /// </summary>
    [TestMethod]
    public async Task ReopenedCatalog_ReusesOnlyTheExactRuntime()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var token = TestContext.CancellationToken;
            var first = await new SeedCatalogCache(paths, "runtime-a", "26.3").GetAsync(_ => Task.FromResult(Catalog("plains")), token);
            var cached = await new SeedCatalogCache(paths, "runtime-a", "26.3").GetAsync(_ => throw new InvalidOperationException("A cache hit must not initialize Java."), token);
            Assert.AreEqual("runtime-a", cached.Fingerprint);
            CollectionAssert.AreEqual(first.Biomes.Select(item => item.Id).ToArray(), cached.Biomes.Select(item => item.Id).ToArray());
            var other = await new SeedCatalogCache(paths, "runtime-b", "26.3").GetAsync(_ => Task.FromResult(Catalog("desert")), token);
            Assert.AreEqual("desert", other.Biomes[0].Id);
            Assert.HasCount(2, Directory.GetFiles(Path.Combine(paths.CacheDirectory, "seed-explorer", "catalogs")));
        });
    }

    /// <summary>
    /// A malformed disposable cache must retry the real producer. A failed retry must preserve the existing bytes;
    /// a later successful retry repairs that record. This distinguishes recovery from silently returning empty choices.
    /// </summary>
    [TestMethod]
    public async Task MalformedCatalog_PreservesFailuresAndRepairsOnRetry()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var token = TestContext.CancellationToken;
            var cache = new SeedCatalogCache(paths, "runtime-a", "26.3");
            await cache.GetAsync(_ => Task.FromResult(Catalog("plains")), token);
            var path = Directory.GetFiles(Path.Combine(paths.CacheDirectory, "seed-explorer", "catalogs")).Single();
            const string malformed = "{\"version\":\"26.3\",\"fingerprint\":\"runtime-a\",\"dimensions\":null,\"biomes\":[],\"structures\":[]}";
            await File.WriteAllTextAsync(path, malformed, token);
            await Assert.ThrowsAsync<IOException>(() => cache.GetAsync(_ => throw new IOException("Original runtime unavailable."), token));
            Assert.AreEqual(malformed, await File.ReadAllTextAsync(path, token));
            var repaired = await cache.GetAsync(_ => Task.FromResult(Catalog("desert")), token);
            Assert.AreEqual("desert", repaired.Biomes[0].Id);
            var reopened = await cache.GetAsync(_ => throw new InvalidOperationException("Repair was not committed."), token);
            Assert.AreEqual("desert", reopened.Dimensions[0].Biomes[0]);
        });
    }

    /// <summary>
    /// An actual directory/file collision makes the optional cache unwritable on every platform.
    /// Valid original choices must remain usable; this does not substitute or suppress the original producer failure above.
    /// </summary>
    [TestMethod]
    public async Task UnwritableCache_ReturnsTheOriginalRegistry()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var collision = Path.Combine(paths.CacheDirectory, "seed-explorer", "catalogs", "runtime-a.json");
            Directory.CreateDirectory(collision);
            var catalog = await new SeedCatalogCache(paths, "runtime-a", "26.3").GetAsync(_ => Task.FromResult(Catalog("plains")), TestContext.CancellationToken);
            Assert.AreEqual("plains", catalog.Biomes[0].Id);
            Assert.IsTrue(Directory.Exists(collision));
        });
    }

    /// <summary>
    /// Original APIs may expose numeric biome identities and no structures. Existing cache cases all
    /// contain structures and no native labels; they cannot detect rejecting a valid historical catalog
    /// or discarding its only real names. Reopening with a failing producer proves durable preservation.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NativeCatalogWithoutStructures_PreservesOriginalLabelsOnReopen(bool hasBiomes)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var original = new SeedCatalog("old-native", [new("overworld", 0, 256, hasBiomes ? ["legacy:biome_1"] : [])],
                hasBiomes ? [new("legacy:biome_1", ["overworld"])] : [], [])
            {
                Names = hasBiomes ? new Dictionary<string, string> { ["legacy:biome_1"] = "Native plains" } : [],
                SeedBoundSpawn = false,
            };
            var cache = new SeedCatalogCache(paths, "old-runtime", original.Version);
            await cache.GetAsync(_ => Task.FromResult(original), TestContext.CancellationToken);
            var reopened = await cache.GetAsync(_ => throw new InvalidOperationException("The valid original catalog must be cached."), TestContext.CancellationToken);
            Assert.IsEmpty(reopened.Structures);
            Assert.HasCount(hasBiomes ? 1 : 0, reopened.Biomes);
            if (hasBiomes)
            {
                Assert.AreEqual("legacy:biome_1", reopened.Biomes.Single().Id);
                Assert.AreEqual("Native plains", reopened.Names["legacy:biome_1"]);
            }
            else
            {
                Assert.IsEmpty(reopened.Names);
            }

            Assert.IsFalse(reopened.SeedBoundSpawn);
        });
    }

    private static SeedCatalog Catalog(string biome) => new("26.3", [new("overworld", -64, 384, [biome])],
        [new(biome, ["overworld"])], [new("desert_pyramid", ["overworld"], [biome])]);
}