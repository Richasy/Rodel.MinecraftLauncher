using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Opt-in original shared-spawn integration, independent saved-world coordinates and live cancellation boundaries.</summary>
[TestClass]
[TestCategory("Live")]
public sealed class SeedSpawnGenerationTests
{
    /// <summary>
    /// Review: an original climate candidate or (0,80,0) must not masquerade as a finalized world spawn. The literal
    /// seed-2 and seed-0 coordinates come from independently run, unmodified 26.3 dedicated servers' saved level.dat.
    /// Reopening another real session protects durable reuse without assuming a warmed in-memory child cache.
    /// </summary>
    [TestMethod]
    [DataRow("2", -32, 66, 0)]
    [DataRow("0", -32, 69, 0)]
    public async Task Spawn_MatchesIndependentOriginalSaveAndReopensCompletedCache(string seed, int x, int y, int z)
    {
        var query = new SeedSpawnQuery { Profile = ReadProfile(), Seed = seed };
        await ReportStorage.RunAsync(async paths =>
        {
            var service = new SeedExplorerService(paths);
            await using (var session = await service.OpenAsync(query.Profile, TestContext.CancellationToken))
            {
                var result = await session.SpawnAsync(query, TestContext.CancellationToken);
                Assert.IsFalse(result.Cached);
                Assert.AreEqual("spawn", result.Position.Kind);
                Assert.AreEqual(x, result.Position.X);
                Assert.AreEqual(y, result.Position.Y);
                Assert.AreEqual(z, result.Position.Z);
            }

            await using var reopened = await service.OpenAsync(query.Profile, TestContext.CancellationToken);
            var cached = await reopened.SpawnAsync(query, TestContext.CancellationToken);
            Assert.IsTrue(cached.Cached);
            Assert.AreEqual(x, cached.Position.X);
            Assert.AreEqual(y, cached.Position.Y);
            Assert.AreEqual(z, cached.Position.Z);
            Assert.IsFalse(Directory.EnumerateDirectories(Path.Combine(paths.CacheDirectory, "seed-explorer", "workers"), "worker-*", SearchOption.AllDirectories).Any(),
                "A cache hit must not start a spawn child.");
        });
    }

    /// <summary>
    /// Review: observe the unmodified listener's actual global-spawn selection log in a fixed-ocean world before
    /// cancelling; merely cancelling during JVM startup would miss the unsafe synchronous initialization defect.
    /// The same primary session must then answer uncached maps, and a short spawn-based scan must retain its cursor.
    /// </summary>
    [TestMethod]
    public async Task Spawn_CancelDuringOriginalSelectionAndDeadlinePreserveThePrimarySession()
    {
        var profile = ReadProfile();
        await ReportStorage.RunAsync(async paths =>
        {
            await using var session = await new SeedExplorerService(paths).OpenAsync(profile, TestContext.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(60));
            var world = new SeedWorldSettings { Preset = "single_biome", Biome = "ocean" };
            var work = session.SpawnAsync(new() { Profile = profile, Seed = "73017", World = world }, cancellation.Token);
            await ObserveOriginalSelectionAsync(paths.CacheDirectory, work, cancellation.Token);
            Assert.IsFalse(work.IsCompleted, "The observed selection must still be active when cancellation is requested.");
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => work);
            var tile = await session.MapAsync(new() { Profile = profile, Seed = "73018", Y = 64, Side = 1, Structures = [] }, TestContext.CancellationToken);
            Assert.IsFalse(tile.Cached);
            Assert.AreEqual("73018", tile.Seed);
            var result = await session.SearchAsync(new()
            {
                Profile = profile,
                World = world,
                StartSeed = "73019",
                MaxSeeds = 10_000,
                MaxResults = 100,
                TimeLimitSeconds = 1,
                Conditions = [new() { Id = "water", Kind = "biome", Targets = ["ocean"], Reference = "spawn", Radius = 0, Y = 64 }],
            }, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("time_limit", result.Status);
            Assert.AreEqual(result.Checked, result.NextOffset, "An unfinished spawn must not advance the durable candidate cursor.");
            Assert.IsFalse(Directory.EnumerateDirectories(Path.Combine(paths.CacheDirectory, "seed-explorer", "workers"), "worker-*", SearchOption.AllDirectories).Any());
            var afterDeadline = await session.MapAsync(new() { Profile = profile, Seed = "73020", Y = 64, Side = 1, Structures = [] }, TestContext.CancellationToken);
            Assert.IsFalse(afterDeadline.Cached);
            Assert.AreEqual("73020", afterDeadline.Seed);
        });
    }

    private static async Task ObserveOriginalSelectionAsync(string cache, Task work, CancellationToken ct)
    {
        while (!work.IsCompleted)
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(cache, "seed-explorer", "workers"), "spawn.log", SearchOption.AllDirectories))
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                if (reader.ReadToEnd().Contains("Selecting global world spawn...", StringComparison.Ordinal)) return;
            }

            await Task.Delay(25, ct);
        }

        await work;
        Assert.Fail("The requested cold original selection was not observed.");
    }

    private static SeedRuntimeProfile ReadProfile()
    {
        var path = Environment.GetEnvironmentVariable("RODEL_SEED_TEST_PROFILE");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set RODEL_SEED_TEST_PROFILE to opt into installed original-JVM integration.");
        return JsonSerializer.Deserialize(File.ReadAllText(path!), SeedJsonContext.Default.SeedRuntimeProfile)
            ?? throw new InvalidDataException("Missing original runtime profile.");
    }

    /// <summary>Receives the runner's cancellation context for original processes and isolated storage.</summary>
    public TestContext TestContext { get; set; }
}