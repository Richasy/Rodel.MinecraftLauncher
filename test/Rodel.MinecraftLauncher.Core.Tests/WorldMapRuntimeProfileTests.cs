// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects effective Java transfer through saved-world snapshots, independently of manual seed inventory and JVM generation.</summary>
[TestClass]
public sealed class WorldMapRuntimeProfileTests
{
    /// <summary>Gets or sets the runner's cancellation context for isolated storage and snapshot reads.</summary>
    public TestContext TestContext { get; set; }

    /// <summary>
    /// Review: manual inventory tests missed the separate region/finite snapshot constructors, leaving configured Java
    /// unusable for world previews. Exercise actual settings and saved readers against explicit fixture paths;
    /// no Java probe is needed to distinguish the old top-level-only selection from effective settings.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorldSnapshot_TransfersEffectiveJavaAndResolvesRelativePaths(bool finite)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, finite);
            var instance = new InstanceStore(paths).Get(selection.InstanceId);
            var settings = new SettingsService(paths);
            var maps = new WorldMapService(paths);
            var before = Hashes(instance);
            var global = Path.Combine(paths.Root, "global-java", "bin", "java.exe");
            await settings.SetAsync("javaPath", JsonValue.Create(global), cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual(global, (await maps.DescribeAsync(selection, TestContext.CancellationToken)).Profile.JavaPath);

            await settings.SetAsync("javaPath", JsonValue.Create("tools/java.exe"), instance, TestContext.CancellationToken);
            Assert.AreEqual(Path.Combine(instance.GameDirectory, "tools", "java.exe"), (await maps.DescribeAsync(selection, TestContext.CancellationToken)).Profile.JavaPath);
            var managed = Path.Combine(paths.RuntimesDirectory, "world-runtime");
            Directory.CreateDirectory(managed);
            await File.WriteAllTextAsync(Path.Combine(managed, "registration.json"), """{"javaPath":"runtime/bin/java.exe"}""", TestContext.CancellationToken);
            await settings.SetAsync("javaRuntimeId", JsonValue.Create("world-runtime"), instance, TestContext.CancellationToken);
            Assert.AreEqual(Path.Combine(managed, "runtime", "bin", "java.exe"), (await maps.DescribeAsync(selection, TestContext.CancellationToken)).Profile.JavaPath);

            await settings.RemoveAsync("javaRuntimeId", instance, TestContext.CancellationToken);
            Assert.AreEqual(global, (await maps.DescribeAsync(selection, TestContext.CancellationToken)).Profile.JavaPath);
            await settings.RemoveAsync("javaPath", cancellationToken: TestContext.CancellationToken);
            Assert.IsNull((await maps.DescribeAsync(selection, TestContext.CancellationToken)).Profile.JavaPath);
            CollectionAssert.AreEqual(before, Hashes(instance), "Resolving generation preferences cannot write the save.");
        });
    }

    /// <summary>
    /// Java is optional for saved pixels. Reading new configuration must not turn a malformed Java value into a
    /// saved-world admission gate; preview-failure tests exercise a later boundary and cannot establish this.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorldSnapshot_InvalidJavaKeepsSavedPixelsReadable(bool finite)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, finite);
            var instance = new InstanceStore(paths).Get(selection.InstanceId);
            await new SettingsService(paths).SetAsync("javaPath", JsonValue.Create(42), instance, TestContext.CancellationToken);
            var before = Hashes(instance);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            var tile = await session.MapAsync(new() { Selection = selection, MinX = finite ? 2 : 12, MinZ = finite ? 1 : 0, Side = 1 }, TestContext.CancellationToken);
            Assert.IsTrue(tile.Known.Single());
            Assert.AreEqual(finite ? 6 : 70, tile.Heights.Single());
            Assert.Contains(warning => warning.StartsWith("WORLD_RUNTIME_SETTINGS:", StringComparison.Ordinal), session.Snapshot.Warnings);
            CollectionAssert.AreEqual(before, Hashes(instance));
        });
    }

    private async Task<WorldMapSelection> PrepareAsync(LauncherPaths paths, bool finite)
    {
        var instance = await new InstanceStore(paths).CreateAsync("World Java configuration", "1.12.2", TestContext.CancellationToken);
        var saves = Path.Combine(instance.GameDirectory, "saves");
        Directory.CreateDirectory(saves);
        if (finite)
        {
            Copy("indev.mclevel", saves);
            return new(instance.Id, "indev.mclevel");
        }

        var directory = Path.Combine(saves, "Synthetic");
        Directory.CreateDirectory(Path.Combine(directory, "region"));
        Copy("level.dat", directory);
        Copy("r.0.0.mca", Path.Combine(directory, "region"));
        return new(instance.Id, "Synthetic");
    }

    private static void Copy(string name, string directory) => File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", name + ".fixture"), Path.Combine(directory, name));

    private static string[] Hashes(InstanceRecord instance) => [.. Directory.EnumerateFiles(Path.Combine(instance.GameDirectory, "saves"), "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))];
}