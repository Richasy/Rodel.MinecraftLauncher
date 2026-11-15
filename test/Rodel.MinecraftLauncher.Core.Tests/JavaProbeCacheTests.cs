using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects per-executable record ownership, lightweight reclamation and host-selected cache locations.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JavaProbeCacheTests
{
    /// <summary>Equal version strings do not identify a runtime; a misplaced record must be rejected rather than associated with another executable.</summary>
    [TestMethod]
    public async Task Probe_RecordBelongsToAnotherJava_ReprobesCorrectExecutable()
    {
        using var first = new JavaProbeFixture();
        using var second = new JavaProbeFixture();
        var discovery = new JavaDiscovery(first.Paths);
        var cacheDirectory = Path.Combine(first.Paths.CacheDirectory, "java-probes");
        Assert.IsTrue((await discovery.ProbeAsync(first.Executable, TestContext.CancellationToken)).IsUsable);
        var firstRecord = Directory.GetFiles(cacheDirectory, "*.json").Single();
        var other = await discovery.ProbeAsync(second.Executable, TestContext.CancellationToken);
        Assert.AreEqual(second.Executable, other.Path);
        var secondRecord = Directory.GetFiles(cacheDirectory, "*.json").Single(file => file != firstRecord);

        File.Copy(secondRecord, firstRecord, overwrite: true);
        var recovered = await discovery.ProbeAsync(first.Executable, TestContext.CancellationToken);
        Assert.IsTrue(recovered.IsUsable);
        Assert.AreEqual(first.Executable, recovered.Path);
        Assert.AreEqual(first.Home, recovered.JavaHome);
        Assert.AreEqual(2, first.Calls, "A mismatched record must trigger a real probe.");
        Assert.AreEqual(1, second.Calls);
        Assert.HasCount(2, Directory.GetFiles(cacheDirectory, "*.json"));
        Assert.AreEqual(second.Executable, (await discovery.ProbeAsync(second.Executable, TestContext.CancellationToken)).Path);
        Assert.AreEqual(1, second.Calls, "Repairing one record must not invalidate the other Java.");
    }

    /// <summary>Host cache placement does not move instance storage; reclamation removes only aged recognized probe records, not fresh or unknown files.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Prune_ExpiredProbeRecord_RespectsCacheLocationAndPreservesOtherFiles(bool separateCache)
    {
        using var fixture = new JavaProbeFixture();
        var root = Path.Combine(fixture.Root, "data");
        var expectedCache = separateCache ? Path.Combine(fixture.Root, "package-cache") : Path.Combine(root, "cache");
        var paths = new LauncherPaths(root, cacheDirectory: separateCache ? expectedCache : null);
        Assert.IsTrue((await new JavaDiscovery(paths).ProbeAsync(fixture.Executable, TestContext.CancellationToken)).IsUsable);
        var directory = Path.Combine(expectedCache, "java-probes");
        var expired = Directory.GetFiles(directory, "*.json").Single();
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-40));
        var fresh = Path.Combine(directory, new string('F', 64) + ".json");
        File.Copy(expired, fresh);
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
        var unknown = Path.Combine(directory, "notes.json");
        File.WriteAllText(unknown, "Unowned data must be retained.");
        File.SetLastWriteTimeUtc(unknown, DateTime.UtcNow.AddDays(-40));
        var nested = Path.Combine(directory, "unowned", new string('E', 64) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "A matching file name does not make an unknown subdirectory owned.");
        File.SetLastWriteTimeUtc(nested, DateTime.UtcNow.AddDays(-40));

        var cache = new CacheService(paths);
        var plan = await cache.PruneAsync(ct: TestContext.CancellationToken);
        Assert.IsTrue(File.Exists(expired), "A plan must not remove files.");
        CollectionAssert.AreEquivalent(new[] { expired }, plan.Candidates.ToArray());
        var applied = await cache.PruneAsync(plan: false, ct: TestContext.CancellationToken);
        CollectionAssert.AreEquivalent(new[] { expired }, applied.Removed.ToArray());
        Assert.IsFalse(File.Exists(expired));
        Assert.IsTrue(File.Exists(fresh));
        Assert.AreEqual("Unowned data must be retained.", File.ReadAllText(unknown));
        Assert.AreEqual("A matching file name does not make an unknown subdirectory owned.", File.ReadAllText(nested));
        Assert.IsTrue(File.Exists(fixture.Executable), "Cache maintenance must never remove Java itself.");
        Assert.AreEqual(Path.Combine(root, "instances"), paths.InstancesDirectory);
        if (separateCache)
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "cache")), "An explicit cache location must not also create the default cache.");
    }

    /// <summary>Cleanup waiting on an expired record's lease must recheck its age after a probe publishes a fresh observation.</summary>
    [TestMethod]
    public async Task Prune_ProbeRefreshesRecordWhileCleanupWaits_PreservesUpdatedObservation()
    {
        using var fixture = new JavaProbeFixture();
        await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        var record = Directory.GetFiles(Path.Combine(fixture.Paths.CacheDirectory, "java-probes")).Single();
        File.SetLastWriteTimeUtc(record, DateTime.UtcNow.AddDays(-40));
        fixture.SetVersion("25.0.2");
        File.WriteAllText(Path.Combine(fixture.Home, "wait"), string.Empty);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var probing = fixture.Discovery.ProbeAsync(fixture.Executable, cancellation.Token);
        Task? cleanup = null;
        try
        {
            await fixture.WaitForStartAsync(cancellation.Token);
            // There is exactly one aged record and no jobs, versions or other candidates.
            // Calling directly reaches its held file lease before yielding; Task.Run would not establish this boundary.
            var pruning = new CacheService(fixture.Paths).PruneAsync(plan: false, ct: cancellation.Token);
            cleanup = pruning;
            Assert.IsFalse(pruning.IsCompleted, "Cleanup must reach the record lease while reporting is blocked.");
            File.Delete(Path.Combine(fixture.Home, "wait"));
            var updated = await probing;
            var result = await pruning;
            Assert.IsTrue(updated.IsUsable);
            Assert.AreEqual("25.0.2", updated.FullVersion);
            Assert.IsEmpty(result.Removed);
            Assert.IsEmpty(result.Warnings);
            Assert.IsTrue(File.Exists(record), "The post-lease age check must retain the newly written record.");
            Assert.AreEqual("25.0.2", (await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).FullVersion);
            Assert.AreEqual(2, fixture.Calls, "The refreshed observation must remain available without another reporting process.");
        }
        finally { await JavaProbeFixture.CancelAndDrainAsync(cancellation, probing, cleanup); }
    }

    /// <summary>Gets or sets the runner context for cancellation of real file and process operations.</summary>
    public TestContext TestContext { get; set; }
}