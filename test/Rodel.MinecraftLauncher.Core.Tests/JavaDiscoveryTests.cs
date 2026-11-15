using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects Core discovery, process probing and change-aware reuse; CLI-host contracts and JVM compatibility are separate.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JavaDiscoveryTests
{
    /// <summary>Full properties need one worker; a fresh client must reuse disk observations and invalidate same-size, same-time release edits.</summary>
    [TestMethod]
    public async Task Probe_CacheReuseAndRuntimeUpdate_ReturnsCurrentPropertiesAcrossClients()
    {
        using var fixture = new JavaProbeFixture();
        var first = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.IsTrue(first.IsUsable);
        Assert.AreEqual("25.0.1", first.FullVersion);
        Assert.AreEqual(25, first.MajorVersion);
        Assert.AreEqual("x64", first.Architecture);
        Assert.AreEqual(64, first.Bitness);
        Assert.AreEqual("UTF-8", first.FileEncoding);
        Assert.AreEqual("UTF-8", first.NativeEncoding);
        Assert.AreEqual(fixture.Home, first.JavaHome);
        Assert.AreEqual(1, fixture.Calls, "A complete properties response needs only one subprocess.");
        var cacheDirectory = Path.Combine(fixture.Paths.CacheDirectory, "java-probes");
        var cacheFile = Directory.GetFiles(cacheDirectory).Single();
        var reused = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.AreEqual(first.FullVersion, reused.FullVersion);
        Assert.AreEqual(1, fixture.Calls, "A new client must reuse the validated observation.");
        CollectionAssert.AreEquivalent(new[] { cacheFile }, Directory.GetFiles(cacheDirectory));

        var release = Path.Combine(fixture.Home, "release");
        var timestamp = File.GetLastWriteTimeUtc(release);
        fixture.SetVersion("25.0.2");
        File.SetLastWriteTimeUtc(release, timestamp);
        var updated = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.AreEqual("25.0.2", updated.FullVersion);
        Assert.AreEqual(2, fixture.Calls, "Content changes must invalidate even when size and timestamp are unchanged.");
        CollectionAssert.AreEquivalent(new[] { cacheFile }, Directory.GetFiles(cacheDirectory),
            "Updating one executable must replace its record, not append a version history.");
    }

    /// <summary>Overlapping requests reach an unfinished worker and must share its validated result instead of starting duplicate JVMs.</summary>
    [TestMethod]
    public async Task Probe_ConcurrentRequests_ReuseSingleSuccessfulWorker()
    {
        using var fixture = new JavaProbeFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        File.WriteAllText(Path.Combine(fixture.Home, "wait"), string.Empty);
        var first = fixture.Discovery.ProbeAsync(fixture.Executable, cancellation.Token);
        Task? follower = null;
        try
        {
            await fixture.WaitForStartAsync(cancellation.Token);
            var second = fixture.Discovery.ProbeAsync(fixture.Executable, cancellation.Token);
            follower = second;
            Assert.IsFalse(second.IsCompleted, "The second caller must wait for the unfinished observation.");
            File.Delete(Path.Combine(fixture.Home, "wait"));
            var results = await Task.WhenAll(first, second);
            Assert.IsTrue(results.All(result => result.IsUsable && result.FullVersion == "25.0.1"));
            Assert.AreEqual(1, fixture.Calls);
        }
        finally { await JavaProbeFixture.CancelAndDrainAsync(cancellation, first, follower); }
    }

    /// <summary>A broken executable must not become a persistent negative cache that hides a subsequent repair.</summary>
    [TestMethod]
    public async Task Probe_RuntimeFailureThenRepair_RechecksWithoutRestartingClient()
    {
        using var fixture = new JavaProbeFixture();
        File.WriteAllText(Path.Combine(fixture.Home, "fail"), string.Empty);
        var discovery = fixture.Discovery;
        var failed = await discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.IsFalse(failed.IsUsable);
        Assert.Contains("Runtime could not initialize.", failed.VersionOutput);
        File.Delete(Path.Combine(fixture.Home, "fail"));
        var repaired = await discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.IsTrue(repaired.IsUsable);
        Assert.AreEqual("25.0.1", repaired.FullVersion);
        Assert.AreEqual(3, fixture.Calls, "Two failed fallback probes must not suppress the repaired properties probe.");
    }

    /// <summary>Deleted executables cannot remain usable through a cache; a later replacement must be probed again.</summary>
    [TestMethod]
    public async Task Probe_ExecutableRemovedAndReplaced_DoesNotReturnStaleUsability()
    {
        using var fixture = new JavaProbeFixture();
        Assert.IsTrue((await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).IsUsable);
        File.Move(fixture.Executable, fixture.Executable + ".old");
        Assert.IsFalse((await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).IsUsable);
        File.Move(fixture.Executable + ".old", fixture.Executable);
        fixture.SetVersion("26.0.1");
        Assert.AreEqual(26, (await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).MajorVersion);
        Assert.AreEqual(2, fixture.Calls);
    }

    /// <summary>Runtime-library and launcher updates invalidate observations even when the release declaration stays unchanged.</summary>
    [TestMethod]
    [DataRow("bin\\server\\jvm.dll")]
    [DataRow("lib\\modules")]
    [DataRow("bin\\java.exe")]
    public async Task Probe_RuntimeFilesChanged_RefreshesObservedVersion(string relative)
    {
        using var fixture = new JavaProbeFixture();
        var file = Path.Combine(fixture.Home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file))
            File.WriteAllText(file, "original");
        Assert.AreEqual("25.0.1", (await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).FullVersion);
        File.WriteAllText(Path.Combine(fixture.Home, "reported-version"), "25.0.2");
        using (var changed = new FileStream(file, FileMode.Append))
            changed.WriteByte(0);
        Assert.AreEqual("25.0.2", (await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).FullVersion);
        Assert.AreEqual(2, fixture.Calls);
    }

    /// <summary>Retargeting an unchanged alias resolves the new runtime instead of reusing the previous target's identity.</summary>
    [TestMethod]
    public async Task Probe_JunctionRetargeted_FollowsCurrentJavaHome()
    {
        using var first = new JavaProbeFixture();
        using var second = new JavaProbeFixture();
        second.SetVersion("26.0.1");
        var alias = Path.Combine(first.Root, "current");
        await JavaProbeFixture.CreateJunctionAsync(alias, first.Home, TestContext.CancellationToken);
        try
        {
            var executable = Path.Combine(alias, "bin", "java.exe");
            Assert.AreEqual(25, (await first.Discovery.ProbeAsync(executable, TestContext.CancellationToken)).MajorVersion);
            Directory.Delete(alias);
            await JavaProbeFixture.CreateJunctionAsync(alias, second.Home, TestContext.CancellationToken);
            var updated = await first.Discovery.ProbeAsync(executable, TestContext.CancellationToken);
            Assert.AreEqual(26, updated.MajorVersion);
            Assert.AreEqual(second.Executable, updated.Path);
        }
        finally { Directory.Delete(alias); }
    }

    /// <summary>Optional property failure keeps legacy Java usable, but incomplete observations must not be cached.</summary>
    [TestMethod]
    public async Task Probe_LegacyPropertiesUnsupported_RetainsVersionAndReportsFallback()
    {
        using var fixture = new JavaProbeFixture();
        File.WriteAllText(Path.Combine(fixture.Home, "legacy"), string.Empty);
        var legacy = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.IsTrue(legacy.IsUsable);
        Assert.AreEqual("25.0.1", legacy.FullVersion);
        Assert.IsNotEmpty(legacy.Warnings);
        Assert.AreEqual(2, fixture.Calls);
        File.Delete(Path.Combine(fixture.Home, "legacy"));
        var complete = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.AreEqual("x64", complete.Architecture);
        Assert.IsEmpty(complete.Warnings);
        Assert.AreEqual(3, fixture.Calls);
    }

    /// <summary>A corrupt optional cache cannot hide Java or poison the next successful persisted observation.</summary>
    [TestMethod]
    public async Task Probe_CorruptCache_ReprobesAndRepairsWithoutLosingJava()
    {
        using var fixture = new JavaProbeFixture();
        await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        var cache = Directory.GetFiles(Path.Combine(fixture.Paths.CacheDirectory, "java-probes"), "*.json").Single();
        File.WriteAllText(cache, "{broken");
        var recovered = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        Assert.IsTrue(recovered.IsUsable);
        Assert.IsNotEmpty(recovered.Warnings);
        Assert.AreEqual(2, fixture.Calls);
        Assert.IsEmpty((await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).Warnings);
        Assert.AreEqual(2, fixture.Calls);
    }

    /// <summary>A read-only stale cache must not block an updated Java or masquerade as a successful cache write.</summary>
    [TestMethod]
    public async Task Probe_CacheWriteDenied_ReportsWarningAndAllowsRecovery()
    {
        using var fixture = new JavaProbeFixture();
        await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
        var cache = Directory.GetFiles(Path.Combine(fixture.Paths.CacheDirectory, "java-probes"), "*.json").Single();
        fixture.SetVersion("25.0.2");
        File.SetAttributes(cache, FileAttributes.ReadOnly);
        try
        {
            var updated = await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken);
            Assert.IsTrue(updated.IsUsable);
            Assert.AreEqual("25.0.2", updated.FullVersion);
            Assert.IsNotEmpty(updated.Warnings);
        }
        finally { File.SetAttributes(cache, FileAttributes.Normal); }

        CollectionAssert.AreEquivalent(new[] { cache }, Directory.GetFiles(Path.GetDirectoryName(cache)!),
            "A failed atomic cache write must not leave temporary files.");
        Assert.IsEmpty((await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).Warnings);
        Assert.AreEqual(3, fixture.Calls, "A failed write must be retried rather than treated as a persisted observation.");
        CollectionAssert.AreEquivalent(new[] { cache }, Directory.GetFiles(Path.GetDirectoryName(cache)!));
    }

    /// <summary>An update at the reporting barrier forces a stable retry instead of publishing a cache under the old fingerprint.</summary>
    [TestMethod]
    public async Task Probe_RuntimeUpdatedDuringReporting_RetriesBeforeCaching()
    {
        using var fixture = new JavaProbeFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        File.WriteAllText(Path.Combine(fixture.Home, "wait"), string.Empty);
        var probing = fixture.Discovery.ProbeAsync(fixture.Executable, cancellation.Token);
        try
        {
            await fixture.WaitForStartAsync(cancellation.Token);
            fixture.SetVersion("26.0.1");
            File.Delete(Path.Combine(fixture.Home, "wait"));
            var updated = await probing;
            Assert.IsTrue(updated.IsUsable);
            Assert.AreEqual(26, updated.MajorVersion);
            Assert.AreEqual(2, fixture.Calls);
            Assert.AreEqual(26, (await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).MajorVersion);
            Assert.AreEqual(2, fixture.Calls);
        }
        finally { await JavaProbeFixture.CancelAndDrainAsync(cancellation, probing); }
    }

    /// <summary>Discovery honors arbitrary registered executable paths and excludes recoverable removed registrations.</summary>
    [TestMethod]
    [TestCategory("HostDiscovery")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Discover_RegisteredExternalJava_IncludesActiveButNotRemovedRuntime(bool legacyLayout)
    {
        using var active = new JavaProbeFixture();
        using var removed = new JavaProbeFixture();
        foreach (var (relative, executable) in new[] { ("custom", active.Executable), (".removed\\old", removed.Executable) })
        {
            var directory = Path.Combine(legacyLayout ? Path.Combine(active.Root, "runtimes") : active.Paths.RuntimesDirectory, relative);
            Directory.CreateDirectory(directory);
            var registration = new JsonObject { ["javaPath"] = Path.GetRelativePath(directory, executable) };
            await JsonFiles.WriteAsync(Path.Combine(directory, "registration.json"), registration, TestContext.CancellationToken);
        }

        var corrupt = Path.Combine(active.Paths.RuntimesDirectory, "unreadable");
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "registration.json"), "{broken");
        var results = await active.Discovery.DiscoverAsync(ct: TestContext.CancellationToken);
        Assert.HasCount(1, results.Where(result => result.Path == active.Executable).ToArray());
        Assert.IsTrue(results.Single(result => result.Path == active.Executable).IsUsable);
        Assert.DoesNotContain(result => result.Path == removed.Executable, results);
        Assert.AreEqual(0, removed.Calls);
    }

    /// <summary>Known nested Mojang runtime homes remain discoverable without recursively treating arbitrary deeper tools as Java installations.</summary>
    [TestMethod]
    [TestCategory("HostDiscovery")]
    public async Task Discover_BundledLayout_FindsJavaWithoutUnboundedRecursion()
    {
        using var fixture = new JavaProbeFixture();
        var minecraft = Path.Combine(fixture.Root, "minecraft");
        var home = Path.Combine(minecraft, "runtime", "component", "windows-x64", "component");
        Directory.CreateDirectory(Path.GetDirectoryName(home)!);
        Directory.Move(fixture.Home, home);
        var unrelated = Path.Combine(home, "tools", "nested", "bin", "java.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        File.WriteAllText(unrelated, "Not an executable; discovery must not descend here.");
        var results = await fixture.Discovery.DiscoverAsync(minecraftDirectory: minecraft, ct: TestContext.CancellationToken);
        var bundled = results.Single(result => result.Path == Path.Combine(home, "bin", "java.exe"));
        Assert.IsTrue(bundled.IsUsable);
        Assert.AreEqual(home, bundled.JavaHome);
        Assert.DoesNotContain(result => result.Path == unrelated, results);
    }

    /// <summary>An unchanged wrapper can switch JVM homes; discovery must re-probe it and return the newly reported real executable.</summary>
    [TestMethod]
    [TestCategory("HostDiscovery")]
    public async Task Discover_WrapperTargetChanged_FollowsCurrentHomeInsteadOfCachingWrapper()
    {
        using var wrapper = new JavaProbeFixture();
        using var first = new JavaProbeFixture();
        using var second = new JavaProbeFixture();
        second.SetVersion("26.0.1");
        File.WriteAllText(Path.Combine(wrapper.Home, "reported-home"), first.Home);
        var initial = await wrapper.Discovery.DiscoverAsync(explicitPath: wrapper.Executable, ct: TestContext.CancellationToken);
        Assert.AreEqual(wrapper.Executable, initial.Single(result => result.Path == first.Executable).DiscoveryPath);

        // Neither file participates in the wrapper fingerprint: a cached wrapper would keep returning the old home.
        File.WriteAllText(Path.Combine(wrapper.Home, "reported-home"), second.Home);
        File.WriteAllText(Path.Combine(wrapper.Home, "reported-version"), "26.0.1");
        var changed = await wrapper.Discovery.DiscoverAsync(explicitPath: wrapper.Executable, ct: TestContext.CancellationToken);
        var selected = changed.Single(result => result.Path == second.Executable);
        Assert.AreEqual(26, selected.MajorVersion);
        Assert.AreEqual(wrapper.Executable, selected.DiscoveryPath);
        Assert.DoesNotContain(result => result.Path == first.Executable || result.Path == wrapper.Executable, changed);
        Assert.AreEqual(2, wrapper.Calls);
    }

    /// <summary>Cancellation reaches a known running worker, terminates it, drains streams, and permits a clean retry.</summary>
    [TestMethod]
    public async Task Probe_CanceledDuringReporting_StopsWorkerAndAllowsRetry()
    {
        using var fixture = new JavaProbeFixture();
        File.WriteAllText(Path.Combine(fixture.Home, "wait"), string.Empty);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var probing = fixture.Discovery.ProbeAsync(fixture.Executable, cancellation.Token);
        try
        {
            var id = await fixture.WaitForStartAsync(cancellation.Token);
            using var process = Process.GetProcessById(id);
            await cancellation.CancelAsync();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await probing);
            Assert.IsTrue(process.HasExited, "Cancellation must not leave an orphan reporting worker.");
            File.Delete(Path.Combine(fixture.Home, "wait"));
            Assert.IsTrue((await fixture.Discovery.ProbeAsync(fixture.Executable, TestContext.CancellationToken)).IsUsable);
            Assert.AreEqual(2, fixture.Calls);
        }
        finally { await JavaProbeFixture.CancelAndDrainAsync(cancellation, probing); }
    }

    /// <summary>Gets or sets the runner context used to cancel subprocess and file operations when the test ends.</summary>
    public TestContext TestContext { get; set; }
}