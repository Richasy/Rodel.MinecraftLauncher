// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects instance-based version availability independently of any generation adapter or downloaded catalog.</summary>
[TestClass]
public sealed class SeedVersionAvailabilityTests
{
    /// <summary>Gets or sets the runner's cancellation and test lifecycle context.</summary>
    public TestContext TestContext { get; set; }
    /// <summary>Old versions and inherited profiles are enabled from installed instances; catalog/cache-only candidates and incomplete instances remain visible and disabled.</summary>
    [TestMethod]
    public async Task VersionInventory_UsesInstalledInstanceOriginalVersionsWithoutVersionGate()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            Directory.CreateDirectory(paths.CacheDirectory);
            await File.WriteAllTextAsync(Path.Combine(paths.CacheDirectory, "version_manifest_v2.json"),
                """{"versions":[{"id":"old-test","type":"old_alpha"},{"id":"26.3","type":"release"},{"id":"future-snapshot","type":"snapshot"}]}""", TestContext.CancellationToken);
            var store = new InstanceStore(paths);
            var first = await store.CreateAsync("Old installed", "old-test", TestContext.CancellationToken);
            var second = await store.CreateAsync("Old inherited", "loader-profile", TestContext.CancellationToken);
            var incomplete = await store.CreateAsync("Only metadata", "future-snapshot", TestContext.CancellationToken);
            await VersionAsync(first.MinecraftDirectory, "old-test", """{"id":"old-test","mainClass":"original.Main"}""", true);
            await VersionAsync(second.MinecraftDirectory, "old-test", """{"id":"old-test","mainClass":"original.Main"}""", true);
            await VersionAsync(second.MinecraftDirectory, "loader-profile", """{"id":"loader-profile","inheritsFrom":"old-test"}""", false);
            await VersionAsync(incomplete.MinecraftDirectory, "future-snapshot", """{"id":"future-snapshot","mainClass":"original.Main"}""", false);
            var options = await new SeedExplorerService(paths).ListVersionsAsync(cancellationToken: TestContext.CancellationToken);
            var old = options.Single(option => option.MinecraftVersion == "old-test");
            Assert.IsTrue(old.Installed); Assert.AreEqual("old_alpha", old.Type);
            CollectionAssert.AreEquivalent(new[] { first.Id, second.Id }, old.InstanceIds);
            Assert.IsFalse(options.Single(option => option.MinecraftVersion == "26.3").Installed);
            Assert.IsFalse(options.Single(option => option.MinecraftVersion == "future-snapshot").Installed);
            Assert.DoesNotContain(option => option.MinecraftVersion == "loader-profile", options);
        });
    }

    /// <summary>
    /// Review: a Java choice saved through normal settings must reach the seed profile, including managed references;
    /// otherwise a usable nonstandard installation is silently ignored. The availability case above does not cross
    /// this settings-to-generator boundary. Paths and registration are synthetic; no Java discovery or JVM is needed.
    /// </summary>
    [TestMethod]
    public async Task VersionInventory_UsesEffectiveJavaSelectionAndReturnsToAutomatic()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            await File.WriteAllTextAsync(Path.Combine(paths.CacheDirectory, "version_manifest_v2.json"), """{"versions":[]}""", TestContext.CancellationToken);
            var instance = await new InstanceStore(paths).CreateAsync("Configured seed runtime", "seed-test", TestContext.CancellationToken);
            await VersionAsync(instance.MinecraftDirectory, "seed-test", """{"id":"seed-test","mainClass":"original.Main"}""", true);
            var settings = new SettingsService(paths);
            var seeds = new SeedExplorerService(paths);
            Assert.IsNull((await seeds.ListVersionsAsync(TestContext.CancellationToken)).Single().Profile.JavaPath);

            var global = Path.Combine(paths.Root, "global-java", "bin", "java.exe");
            await settings.SetAsync("javaPath", JsonValue.Create(global), cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual(global, (await seeds.ListVersionsAsync(TestContext.CancellationToken)).Single().Profile.JavaPath);
            var local = Path.Combine(paths.Root, "instance-java", "bin", "java.exe");
            await settings.SetAsync("javaPath", JsonValue.Create(local), instance, TestContext.CancellationToken);
            Assert.AreEqual(local, (await seeds.ListVersionsAsync(TestContext.CancellationToken)).Single().Profile.JavaPath);

            var managed = Path.Combine(paths.RuntimesDirectory, "seed-runtime");
            Directory.CreateDirectory(managed);
            await File.WriteAllTextAsync(Path.Combine(managed, "registration.json"), """{"javaPath":"runtime/bin/java.exe"}""", TestContext.CancellationToken);
            await settings.SetAsync("javaRuntimeId", JsonValue.Create("seed-runtime"), instance, TestContext.CancellationToken);
            Assert.AreEqual(Path.Combine(managed, "runtime", "bin", "java.exe"), (await seeds.ListVersionsAsync(TestContext.CancellationToken)).Single().Profile.JavaPath);

            await settings.RemoveAsync("javaRuntimeId", instance, TestContext.CancellationToken);
            Assert.AreEqual(global, (await seeds.ListVersionsAsync(TestContext.CancellationToken)).Single().Profile.JavaPath);
            await settings.RemoveAsync("javaPath", cancellationToken: TestContext.CancellationToken);
            Assert.IsNull((await seeds.ListVersionsAsync(TestContext.CancellationToken)).Single().Profile.JavaPath);
        });
    }

    private async Task VersionAsync(string minecraft, string id, string metadata, bool client)
    {
        var directory = Path.Combine(minecraft, "versions", id); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, id + ".json"), metadata, TestContext.CancellationToken);
        if (client) await File.WriteAllBytesAsync(Path.Combine(directory, id + ".jar"), [1, 2, 3], TestContext.CancellationToken);
    }
}