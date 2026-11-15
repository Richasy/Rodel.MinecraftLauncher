using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Opt-in original-JVM integration; RODEL_SEED_TEST_PROFILE points to an installed-runtime profile JSON, read without modifying its files.</summary>
[TestClass]
[TestCategory("Live")]
public sealed class SeedNativeGenerationTests
{
    /// <summary>
    /// Review: a saved table/seed must be evaluated on the requested detached container, not by regenerating a nearby natural chest.
    /// A barrel in the End has no naturally generated counterpart. Repeated original no-player evaluations must retain slots and exact components;
    /// an unknown table must fail honestly and leave the same worker usable. Existing region-loot cases cannot distinguish this source override.
    /// </summary>
    [TestMethod]
    public async Task RecordedLoot_UsesExplicitTableSeedAndDetachedContainer()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            var query = new SeedLootQuery
            {
                Profile = session.Profile,
                Seed = "2",
                World = new() { Dimension = "the_end" },
                Id = "minecraft:barrel",
                X = 0,
                Y = 80,
                Z = 0,
                LootTable = "minecraft:chests/ruined_portal",
                LootSeed = "-3077848731074122968"
            };
            var first = await session.PredictLootAsync(query, TestContext.CancellationToken);
            Assert.AreEqual("minecraft:barrel", first.Id);
            Assert.AreEqual(27, first.Slots);
            Assert.IsNotEmpty(first.Items);
            Assert.AreEqual(query.LootTable, first.LootTable);
            Assert.AreEqual(query.LootSeed, first.LootSeed);
            await Assert.ThrowsAsync<LauncherOperationException>(() => session.PredictLootAsync(query with { LootTable = "minecraft:chests/rodel_missing_prediction_table" }, TestContext.CancellationToken));
            var repeated = await session.PredictLootAsync(query, TestContext.CancellationToken);
            Assert.AreEqual(JsonSerializer.Serialize(first, SeedJsonContext.Default.SeedContainer), JsonSerializer.Serialize(repeated, SeedJsonContext.Default.SeedContainer));
        });
    }

    /// <summary>
    /// Actual original registries, assembled structures and all three dimensions must cross the typed process boundary.
    /// A corrupt cached tile must be recomputed, and a complete native input error must leave the warmed session usable.
    /// Synthetic persistence/export cases cannot establish these behaviors. This does not generate a decorated save.
    /// </summary>
    [TestMethod]
    public async Task NativeSession_MapsThreeDimensionsAndRecoversFromCacheAndInputErrors()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            var catalog = await session.GetCatalogAsync("zh_cn", TestContext.CancellationToken);
            Assert.HasCount(67, catalog.Biomes);
            Assert.HasCount(3, catalog.Dimensions);
            Assert.AreEqual("平原", catalog.Names["plains"]);
            Assert.Contains(structure => structure.Id == "end_city", catalog.Structures);
            var query = Map(profile);
            var tile = await session.MapAsync(query, TestContext.CancellationToken);
            Assert.IsFalse(tile.Cached);
            Assert.Contains(position => position.Id == "desert_pyramid" && position.Pieces > 0 && position.Bounds is { Length: 6 }, tile.Structures);
            Assert.IsTrue((await session.MapAsync(query, TestContext.CancellationToken)).Cached);
            var cache = Directory.GetFiles(Path.Combine(paths.CacheDirectory, "seed-explorer", "tiles"), "*.json").Single();
            await File.WriteAllTextAsync(cache, "{\"seed\":\"different-seed\"}", TestContext.CancellationToken);
            var repaired = await session.MapAsync(query, TestContext.CancellationToken);
            Assert.IsFalse(repaired.Cached);
            Assert.AreEqual(query.Seed, repaired.Seed);
            CollectionAssert.AreEqual(tile.Biomes, repaired.Biomes);
            foreach (var dimension in OtherDimensions)
            {
                var slice = await session.MapAsync(query with { World = new() { Dimension = dimension }, Y = 64, Side = 8, Structures = [] }, TestContext.CancellationToken);
                Assert.AreEqual(dimension, slice.World.Dimension);
                Assert.IsTrue(slice.Palette.All(biome => catalog.Dimensions.Single(entry => entry.Id == dimension).Biomes.Contains(biome)));
                var top = await session.MapAsync(query with { World = new() { Dimension = dimension }, Y = 255, Side = 1, Structures = [] }, TestContext.CancellationToken);
                Assert.AreEqual(255, top.Y);
            }

            var overworldTop = await session.MapAsync(query with { Y = 319, Side = 1, Structures = [] }, TestContext.CancellationToken);
            Assert.AreEqual(319, overworldTop.Y);
            var coarse = await session.MapAsync(query with { MinX = -262144, MinZ = -262144, Step = 65536, Side = 8, Terrain = false, Structures = [] }, TestContext.CancellationToken);
            Assert.HasCount(64, coarse.Biomes);
            await Assert.ThrowsAsync<LauncherOperationException>(() => session.SearchAsync(new()
            {
                Profile = profile,
                Conditions = [new() { Id = "invalid", Kind = "biome", Targets = ["not_registered"] }],
            }, cancellationToken: TestContext.CancellationToken));
            var afterError = await session.MapAsync(query with { Seed = "2", Side = 1, Structures = [] }, TestContext.CancellationToken);
            Assert.AreEqual("2", afterError.Seed, "An uncached native request must still work after a complete input error response.");
            Assert.IsTrue((await session.MapAsync(query, TestContext.CancellationToken)).Cached);
        });
    }

    /// <summary>
    /// Large surface requests may use the auxiliary vanilla sampler; smaller requests use the serial path.
    /// Compare every world position across request partitions and dimensions to detect row offsets, omitted
    /// samples or palette/height/fluid corruption. Slice/catalog coverage above cannot detect these defects.
    /// </summary>
    [TestMethod]
    public async Task NativeSession_SurfaceSamplingMatchesIndependentSmallTiles()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            foreach (var dimension in SurfaceDimensions)
            {
                var query = Map(profile) with { World = new() { Dimension = dimension }, MinX = -113, MinZ = -177, Side = 32, Step = 8, Structures = [] };
                var full = await session.MapAsync(query, TestContext.CancellationToken);
                var fullCells = SurfaceCells(full);
                for (var row = 0; row < 2; row++)
                {
                    for (var column = 0; column < 2; column++)
                    {
                        var small = await session.MapAsync(query with { MinX = query.MinX + (column * 16 * query.Step), MinZ = query.MinZ + (row * 16 * query.Step), Side = 16 }, TestContext.CancellationToken);
                        Assert.IsFalse(small.Cached);
                        var expected = Enumerable.Range(0, 16).SelectMany(offset => fullCells.Skip((((row * 16) + offset) * 32) + (column * 16)).Take(16)).ToArray();
                        CollectionAssert.AreEqual(expected, SurfaceCells(small), $"Surface partition changed in {dimension}: column {column}, row {row}.");
                    }
                }
            }
        });
    }

    private static (string Biome, int Height, int Fluid)[] SurfaceCells(SeedMapTile tile) => [.. Enumerable.Range(0, tile.Biomes.Length)
        .Select(index => (tile.Palette[tile.Biomes[index]], tile.Heights[index], tile.Fluids[index]))];

    /// <summary>
    /// Independent marker cells must preserve the full original query's confirmed structures and bounds.
    /// Planner-only rectangle coverage cannot detect native boundary exclusions or lost assembled starts.
    /// All dimensions share this contract; a tiny biome or single-temple check does not establish it.
    /// </summary>
    [TestMethod]
    public async Task NativeSession_StructurePartitionsPreserveConfirmedPositions()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            foreach (var dimension in SurfaceDimensions)
            {
                var query = Map(profile) with { World = new() { Dimension = dimension }, MinX = 1280, MinZ = 1280, Side = 1, Step = 2048, Y = 64, Terrain = false, Structures = null };
                var full = await session.MapAsync(query, TestContext.CancellationToken);
                var partitioned = new List<SeedPosition>();
                for (var row = 0; row < 2; row++)
                {
                    for (var column = 0; column < 2; column++)
                        partitioned.AddRange((await session.MapAsync(query with { MinX = query.MinX + (column * 1024), MinZ = query.MinZ + (row * 1024), Step = 1024 }, TestContext.CancellationToken)).Structures);
                }

                CollectionAssert.AreEqual(full.Structures.Select(StructureIdentity).Order().ToArray(), partitioned.Select(StructureIdentity).Distinct().Order().ToArray(),
                    $"Original structure coverage changed across partitions in {dimension}.");
            }
        });
    }

    private static string StructureIdentity(SeedPosition position) => $"{position.Id}:{position.X}:{position.Y}:{position.Z}:{position.Pieces}:{string.Join(',', position.Bounds ?? [])}";

    /// <summary>
    /// A viewport cancellation must not kill its warmed worker and force every pan to pay another startup.
    /// Existing retained-search coverage deliberately disposes each session, and a cache hit cannot detect a dead
    /// worker. Cancel after native progress, then demand a distinct uncached map through the same live transport.
    /// </summary>
    [TestMethod]
    public async Task NativeSession_CooperativeCancellation_AcceptsTheNextUncachedMap()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var result = await session.SearchAsync(new()
            {
                Profile = profile,
                MaxSeeds = 100_000,
                MaxResults = 1,
                TimeLimitSeconds = 3,
                Conditions = [new() { Id = "temple", Kind = "structure", Targets = ["desert_pyramid"], Radius = 0 }],
            }, new CancelNativeAtProgress(cancellation), cancellation.Token);
            Assert.AreEqual("cancelled", result.Status);
            var tile = await session.MapAsync(Map(profile) with { Seed = "2", Side = 1, Structures = [] }, TestContext.CancellationToken);
            Assert.IsFalse(tile.Cached);
            Assert.AreEqual("2", tile.Seed);
        });
    }

    /// <summary>
    /// Cancellation triggered by a real committed progress snapshot must preserve its cursor, then a new store/JVM
    /// must continue from that cursor. No mock search results are supplied. Sparse positive structure constraints
    /// keep this bounded run active long enough to exercise cancellation rather than racing an immediate first hit.
    /// </summary>
    [TestMethod]
    public async Task NativeSearch_CancelAtCommittedProgress_ReopensAndAdvancesCursor()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new SeedSearchStore(paths);
            var state = await store.CreateAsync(new()
            {
                Profile = profile,
                MaxSeeds = 100_000,
                MaxResults = 1,
                TimeLimitSeconds = 3,
                Conditions = [new() { Id = "temple", Kind = "structure", Targets = ["desert_pyramid"], Radius = 0 }],
            }, TestContext.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var cancelled = await store.RunAsync(state.Id, new CancelAtProgress(cancellation), cancellation.Token);
            Assert.AreEqual("cancelled", cancelled.Result.Status);
            Assert.IsGreaterThan(0L, cancelled.Result.Checked);
            Assert.AreEqual(cancelled.Result.NextOffset, new SeedSearchStore(paths).Read(state.Id).Result.NextOffset);
            var resumed = await new SeedSearchStore(paths).RunAsync(state.Id, cancellationToken: TestContext.CancellationToken);
            Assert.IsGreaterThan(cancelled.Result.NextOffset, resumed.Result.NextOffset);
            Assert.AreEqual(resumed.Result.Checked, resumed.Result.NextOffset);
            Assert.AreEqual(cancelled.Fingerprint, resumed.Fingerprint);
            Assert.AreEqual(resumed.Result.Hits.Length, resumed.Result.Hits.Select(hit => hit.Seed).Distinct().Count());
        });
    }

    /// <summary>
    /// Review: FULL-state transport, original loot evaluation and original-model decoding are new boundaries not
    /// covered by base terrain tiles. Four chest positions and one stack/component come from the independently
    /// decoded original temple snapshot. Byte-for-byte regeneration after corrupting only the volume protects
    /// preview consistency and prevents a valid-looking cache header from admitting incomplete block storage.
    /// </summary>
    [TestMethod]
    public async Task NativeRegion_OriginalTempleLootModelsAndCorruptVolumeRecovery()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            var positions = await session.FindStructuresAsync(new()
            {
                Profile = profile,
                Seed = "-5495201639033488759",
                CenterX = -710,
                CenterZ = -998,
                Radius = 32,
                Targets = ["desert_pyramid"],
                Limit = 1,
            }, TestContext.CancellationToken);
            Assert.HasCount(1, positions.Positions);
            Assert.AreEqual(-710, positions.Positions[0].X);
            var query = new SeedRegionQuery
            {
                Profile = profile,
                Seed = "-5495201639033488759",
                Bounds = new() { MinX = -720, MaxX = -700, MinY = 48, MaxY = 79, MinZ = -1008, MaxZ = -988 },
                IncludeBlocks = true,
            };
            var region = await session.RegionAsync(query, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual(14112L, region.Materials.Sum(item => item.Count));
            CollectionAssert.AreEquivalent(new[] { (-712, 52, -998), (-710, 52, -1000), (-710, 52, -996), (-708, 52, -998) }, region.Containers.Select(item => (item.X, item.Y, item.Z)).ToArray());
            var west = region.Containers.Single(item => item.X == -712);
            Assert.Contains(item => item.Id == "minecraft:emerald" && item.Count == 1 && item.Slot == 11, west.Items);
            var north = region.Containers.Single(item => item.Z == -1000);
            var book = north.Items.Single(item => item.Id == "minecraft:enchanted_book");
            Assert.AreEqual(1, book.Components["minecraft:stored_enchantments"].GetProperty("minecraft:piercing").GetInt32());
            var index = (((((52 - query.Bounds.MinY) * 21) + (-998 - query.Bounds.MinZ)) * 21) + (-712 - query.Bounds.MinX)) * 2;
            var state = region.Palette[System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(region.Blocks.AsSpan(index, 2))];
            Assert.AreEqual("minecraft:chest", state.Id);
            Assert.AreEqual("east", state.Properties["facing"]);
            var analysis = await session.RegionAsync(query with { IncludeBlocks = false }, cancellationToken: TestContext.CancellationToken);
            Assert.IsEmpty(analysis.Blocks);
            CollectionAssert.AreEqual(region.Materials, analysis.Materials, "Palette counting of aligned sections and partial-edge scans must equal the complete block volume.");
            var scene = await session.BuildSceneAsync(region, TestContext.CancellationToken);
            Assert.IsEmpty(scene.UnavailableModels, "Every block in this original temple sample has a supported original model or an explicitly hidden fluid/air state.");
            Assert.IsGreaterThan(0, scene.CreateMesh(50, 79, TestContext.CancellationToken).Vertices.Length);
            Assert.IsTrue((await session.RegionAsync(query, cancellationToken: TestContext.CancellationToken)).Cached);
            var cache = Directory.GetFiles(Path.Combine(paths.CacheDirectory, "seed-explorer", "regions"), "*.json")
                .Single(path => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!["blocks"]!.GetValue<string>().Length > 0);
            var damaged = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(cache, TestContext.CancellationToken))!;
            damaged["blocks"] = "AAA=";
            await File.WriteAllTextAsync(cache, damaged.ToJsonString(), TestContext.CancellationToken);
            var repaired = await session.RegionAsync(query, cancellationToken: TestContext.CancellationToken);
            Assert.IsFalse(repaired.Cached);
            CollectionAssert.AreEqual(region.Blocks, repaired.Blocks);
        });
    }

    /// <summary>
    /// Review: cancel from an observed FEATURES stage, not a sleep, then demand a distinct uncached map through
    /// the same JVM. This protects framed cancellation and bounded-world cleanup; search-cursor cancellation
    /// does not initialize or close an original server world. It does not claim a maximum cancellation latency.
    /// </summary>
    [TestMethod]
    public async Task NativeRegion_CancelAtFeatures_DrainsAndKeepsItsWorkerUsable()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var progress = new CancelRegionAtFeatures(cancellation);
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.RegionAsync(new()
            {
                Profile = profile,
                Seed = "17",
                Bounds = new() { MinX = 1024, MaxX = 1087, MinY = 0, MaxY = 79, MinZ = -2048, MaxZ = -1985 },
            }, progress, cancellation.Token));
            Assert.IsTrue(progress.Reached);
            var tile = await session.MapAsync(Map(profile) with { Seed = "314159265", Side = 1, Structures = [] }, TestContext.CancellationToken);
            Assert.IsFalse(tile.Cached);
            Assert.AreEqual("314159265", tile.Seed);
            Assert.IsFalse(Directory.EnumerateDirectories(Path.Combine(paths.CacheDirectory, "seed-explorer", "workers"), "request-*", SearchOption.AllDirectories).Any());
        });
    }

    private sealed class CancelRegionAtFeatures(CancellationTokenSource cancellation) : IProgress<SeedRegionProgress>
    {
        internal bool Reached { get; private set; }
        /// <summary>Requests cancellation only after actual original region generation enters FEATURES.</summary>
        public void Report(SeedRegionProgress value) { if (value.Phase == "features") { Reached = true; cancellation.Cancel(); } }
    }

    private static SeedRuntimeProfile ReadProfile()
    {
        var path = Environment.GetEnvironmentVariable("RODEL_SEED_TEST_PROFILE");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set RODEL_SEED_TEST_PROFILE to opt into installed original-JVM integration.");
        return JsonSerializer.Deserialize(File.ReadAllText(path!), SeedJsonContext.Default.SeedRuntimeProfile)
            ?? throw new InvalidDataException("Missing original runtime profile.");
    }

    private static SeedMapQuery Map(SeedRuntimeProfile profile) => new()
    {
        Profile = profile,
        Seed = "-5495201639033488759",
        MinX = -800,
        MinZ = -1100,
        Step = 16,
        Side = 16,
        Structures = ["desert_pyramid"],
    };

    private sealed class CancelAtProgress(CancellationTokenSource cancellation) : IProgress<SeedSearchState>
    {
        /// <summary>Cancels only after a native candidate cursor has actually been published.</summary>
        public void Report(SeedSearchState value) { if (value.Result.Status == "running" && value.Result.Checked > 0) cancellation.Cancel(); }
    }

    private sealed class CancelNativeAtProgress(CancellationTokenSource cancellation) : IProgress<SeedSearchResult>
    {
        /// <summary>Cancels once the actual native loop reports examined candidates.</summary>
        public void Report(SeedSearchResult value) { if (value.Status == "running" && value.Checked > 0) cancellation.Cancel(); }
    }

    /// <summary>Receives the test runner's cancellation and diagnostic context for the real-JVM cases.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] OtherDimensions = ["the_nether", "the_end"];
    private static readonly string[] SurfaceDimensions = ["overworld", "the_nether", "the_end"];
}