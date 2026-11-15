// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects cheap revision discovery independently of map decoding and native generation.</summary>
[TestClass]
public sealed class WorldMapRevisionTests
{
    /// <summary>
    /// Existing unqueried legacy chunks must establish the baseline; new chunk/player/metadata files revoke it.
    /// Opaque file contents deliberately cannot pass NBT decoding, so success establishes a stamp inventory rather
    /// than reopening the world or silently ignoring newly saved data. Existing cached-read tests cover only changed captured files.
    /// </summary>
    [TestMethod]
    [DataRow("legacy_existing")]
    [DataRow("legacy_new")]
    [DataRow("region_new")]
    [DataRow("external_new")]
    [DataRow("player_new")]
    [DataRow("metadata_new")]
    [DataRow("namespaced_new")]
    public async Task RevisionProbe_DetectsNewFilesAndUnqueriedLegacyChanges(string change)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("Revision fixture", "1.12.2", TestContext.CancellationToken);
            var directory = Path.Combine(instance.GameDirectory, "saves", "Synthetic"); Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", "legacy-level.dat.fixture"), Path.Combine(directory, "level.dat"));
            var existing = Path.Combine(directory, "0", "0", "c.0.0.dat"); Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
            await File.WriteAllTextAsync(existing, "opaque existing chunk", TestContext.CancellationToken);
            var selection = new WorldMapSelection(instance.Id, "Synthetic");
            var service = new WorldMapService(paths);
            using var session = await service.OpenAsync(selection, TestContext.CancellationToken);
            Assert.IsFalse(await session.HasChangedAsync(TestContext.CancellationToken), "An unqueried existing legacy file is not a newly saved chunk.");
            if (change == "legacy_existing")
            {
                File.SetLastWriteTimeUtc(existing, File.GetLastWriteTimeUtc(existing).AddSeconds(5));
            }
            else
            {
                var relative = change switch
                {
                    "legacy_new" => Path.Combine("1", "2", "c.1.2.dat"),
                    "region_new" => Path.Combine("region", "r.1.0.mca"),
                    "external_new" => Path.Combine("region", "c.99.0.mcc"),
                    "player_new" => Path.Combine("players", "data", "new-player.dat"),
                    "metadata_new" => Path.Combine("data", "minecraft", "world_gen_settings.dat"),
                    _ => Path.Combine("dimensions", "minecraft", "the_end", "region", "r.0.0.mca"),
                };
                var file = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, "opaque newly saved data", TestContext.CancellationToken);
            }

            Assert.IsTrue(await session.HasChangedAsync(TestContext.CancellationToken));
            session.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.HasChangedAsync(TestContext.CancellationToken));
        });
    }

    /// <summary>Gets or sets the cancellation and execution context supplied by MSTest.</summary>
    public TestContext TestContext { get; set; }
}