using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects durable seed/place ownership and search request boundaries independently of JVM generation correctness.</summary>
[TestClass]
public sealed class SeedExplorationStorageTests
{
    /// <summary>
    /// CLI users omit default sampling and search settings. The source-generated JSON constructor must retain those
    /// defaults while still rejecting absent required inputs. The original C#-constructed JVM cases did not cover
    /// required-member source generation overwriting every omitted init property with null, false or zero.
    /// </summary>
    [TestMethod]
    public void RequestJson_OmittedOptionalFields_RetainsDefaultsAndRejectsMissingRequiredInputs()
    {
        const string profile = "\"profile\":{\"minecraftDirectory\":\"runtime\",\"versionId\":\"26.3\"}";
        var map = JsonSerializer.Deserialize("{" + profile + ",\"seed\":\"-9223372036854775808\"}", SeedJsonContext.Default.SeedMapQuery)!;
        Assert.AreEqual(new SeedWorldSettings(), map.World);
        Assert.AreEqual(16, map.Step);
        Assert.AreEqual(64, map.Side);
        Assert.IsTrue(map.Terrain);
        var search = JsonSerializer.Deserialize("{" + profile + ",\"conditions\":[{\"id\":\"village\",\"kind\":\"structure\",\"targets\":[\"village_plains\"]}]}", SeedJsonContext.Default.SeedSearchQuery)!;
        Assert.AreEqual(new SeedWorldSettings(), search.World);
        Assert.AreEqual(10_000, search.MaxSeeds);
        Assert.AreEqual(60, search.TimeLimitSeconds);
        Assert.AreEqual(1, search.Conditions[0].Minimum);
        Assert.AreEqual(512, search.Conditions[0].Radius);
        Assert.AreEqual("origin", search.Conditions[0].Reference);
        var spawn = JsonSerializer.Deserialize("{" + profile + ",\"seed\":\"2\"}", SeedJsonContext.Default.SeedSpawnQuery)!;
        Assert.AreEqual(new SeedWorldSettings(), spawn.World);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{" + profile + "}", SeedJsonContext.Default.SeedSpawnQuery));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{" + profile + "}", SeedJsonContext.Default.SeedMapQuery));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"minecraftDirectory\":\"runtime\"}", SeedJsonContext.Default.SeedRuntimeProfile));
    }

    /// <summary>
    /// Independent windows can save places concurrently while a seed bookmark is edited. Real store instances and
    /// cross-process leases must retain every record; reopening and removing one place must preserve the seed and
    /// other places. This catches lost collection updates and accidental conflation of the two bookmark segments.
    /// </summary>
    [TestMethod]
    public async Task Bookmarks_IndependentWriters_PreserveSeedAndPlaceIdentities()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new SeedBookmarkStore(paths);
            var seed = await store.SaveAsync(Bookmark("seed", 0), TestContext.CancellationToken);
            var places = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => new SeedBookmarkStore(paths).SaveAsync(Bookmark("place", index), TestContext.CancellationToken)));
            var renamed = await store.SaveAsync(seed with { Name = "Renamed seed" }, TestContext.CancellationToken);
            var duplicate = await store.SaveAsync(Bookmark("place", 2) with { Name = "Updated place" }, TestContext.CancellationToken);
            Assert.AreEqual(seed.Id, renamed.Id);
            Assert.AreEqual(places[2].Id, duplicate.Id);
            await new SeedBookmarkStore(paths).RemoveAsync(places[0].Id, TestContext.CancellationToken);
            var reopened = new SeedBookmarkStore(paths).List();
            Assert.HasCount(6, reopened);
            Assert.AreEqual("Renamed seed", reopened.Single(record => record.Kind == "seed").Name);
            Assert.AreEqual("Updated place", reopened.Single(record => record.X == 2 && record.Kind == "place").Name);
            Assert.DoesNotContain(record => record.Id == places[0].Id, reopened);
        });
    }

    /// <summary>
    /// Review: the map accepts overview scales above 4096, but the store rejected them. Both bookmark kinds must
    /// roundtrip the literal upper map scale through real persistence; an invalid update must preserve prior bytes.
    /// Existing concurrent-writer cases use ordinary scales and cannot catch this producer/store contract mismatch.
    /// </summary>
    [TestMethod]
    [DataRow("seed")]
    [DataRow("place")]
    public async Task Bookmarks_OverviewScale_RoundtripsWithoutAllowingOutOfRangeUpdates(string kind)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new SeedBookmarkStore(paths);
            var saved = await store.SaveAsync(Bookmark(kind, 1) with { BlocksPerPixel = 65_536 }, TestContext.CancellationToken);
            Assert.AreEqual(65_536d, new SeedBookmarkStore(paths).List().Single().BlocksPerPixel);
            var file = Path.Combine(paths.Root, "seed-explorer", "bookmarks.json");
            var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(saved with { BlocksPerPixel = 65_537 }, TestContext.CancellationToken));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// Cancelled writes and malformed persisted collections must not turn existing bookmarks into a new empty
    /// collection. Inspecting the actual bytes after both failure paths covers preservation beyond DTO roundtrips;
    /// generic JSON storage tests do not exercise this store's read-before-write behavior.
    /// </summary>
    [TestMethod]
    public async Task Bookmarks_CancellationAndDamagedCollection_DoNotReplacePriorBytes()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new SeedBookmarkStore(paths);
            await store.SaveAsync(Bookmark("seed", 0), TestContext.CancellationToken);
            var file = Path.Combine(paths.Root, "seed-explorer", "bookmarks.json");
            var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(Bookmark("place", 1), cancellation.Token));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
            await File.WriteAllTextAsync(file, "{damaged", TestContext.CancellationToken);
            await Assert.ThrowsAsync<JsonException>(() => store.SaveAsync(Bookmark("place", 2), TestContext.CancellationToken));
            Assert.AreEqual("{damaged", await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// An invalid dependency graph or overflowing cursor must be rejected before any search is persisted, while
    /// a valid dependent query must retain its full signed-64-bit seed as text when independently reopened.
    /// This protects scheduling inputs; native generation and runtime transport have separate integration coverage.
    /// </summary>
    [TestMethod]
    public async Task Search_InvalidGraphAndCursor_RejectBeforePublicationAndPreserveLosslessSeed()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new SeedSearchStore(paths);
            var query = new SeedSearchQuery
            {
                Profile = Profile(),
                StartSeed = "-9223372036854775808",
                Conditions = [new() { Id = "village", Kind = "structure", Targets = ["village_plains"] }],
            };
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(query with { Conditions = [query.Conditions[0] with { Reference = "village" }] }, TestContext.CancellationToken));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.CreateAsync(query with { Offset = long.MaxValue }, TestContext.CancellationToken));
            // Spawn is a reserved original anchor, never a user condition or a coordinate in another dimension.
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(query with { Conditions = [query.Conditions[0] with { Id = "spawn" }] }, TestContext.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(query with
            {
                World = new() { Dimension = "the_nether" },
                Conditions = [query.Conditions[0] with { Reference = "spawn" }]
            }, TestContext.CancellationToken));
            Assert.IsEmpty(store.List());
            query = query with { Conditions = [query.Conditions[0] with { Reference = "spawn" }, new() { Id = "forest", Kind = "biome", Targets = ["forest"], Reference = "village" }] };
            var state = await store.CreateAsync(query, TestContext.CancellationToken);
            var reopened = new SeedSearchStore(paths).Read(state.Id);
            Assert.AreEqual("-9223372036854775808", reopened.Query.StartSeed);
            Assert.AreEqual("village", reopened.Query.Conditions[1].Reference);
            Assert.AreEqual("spawn", reopened.Query.Conditions[0].Reference);
            Assert.AreEqual(0, reopened.Result.NextOffset);
            Assert.AreEqual("queued", reopened.Result.Status);
        });
    }

    private static SeedRuntimeProfile Profile() => new() { MinecraftDirectory = Path.GetFullPath("original-runtime"), VersionId = "26.3" };
    private static SeedBookmark Bookmark(string kind, int x) => new()
    {
        Kind = kind,
        Name = kind + x,
        Profile = Profile(),
        Fingerprint = "fixture-runtime",
        Seed = "-5495201639033488759",
        X = x,
        Z = -16,
        Position = kind == "place" ? new() { Id = "village_plains", Kind = "structure", X = x, Z = -16, Pieces = 1 } : null,
    };

    public TestContext TestContext { get; set; }
}