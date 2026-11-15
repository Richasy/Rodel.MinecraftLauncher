// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects read-only save interpretation; frozen cross-language fixtures are independent of the C# parser and renderer.</summary>
[TestClass]
public sealed class WorldMapReadTests
{
    private static readonly bool[] KnownSamples = [true, true, true, true, true, false];
    private static readonly int[] SurfaceHeights = [70, 80, 66, 44, 91];
    private static readonly string[] ExpectedDimensions = ["overworld", "the_nether", "the_end"];
    private static readonly int[] SavedVillageBounds = [10, 60, 10, 30, 90, 40];

    /// <summary>Mixed packing and compound/string states retain surfaces below air; a deflated client texture must decode through the same read path rather than failing on a nonseekable ZIP stream.</summary>
    [TestMethod]
    [DataRow("compound")]
    [DataRow("string")]
    [DataRow("mixed")]
    [DataRow("texture")]
    public async Task SavedMap_MixedPackingAndCompressionRetainsActualSurfacesAndUnknownChunks(string paletteShape)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, "anvil");
            if (paletteShape != "compound")
            {
                var region = Path.Combine(new InstanceStore(paths).Get(selection.InstanceId).GameDirectory, "saves", selection.World, "region", "r.0.0.mca");
                var fixture = paletteShape is "mixed" or "texture" ? "mixed-palette.mca.fixture" : "string-palette.mca.fixture";
                File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", fixture), region, true);
            }

            if (paletteShape == "texture")
                await AddOriginalResourcesAsync(paths, selection, TestContext.CancellationToken);

            var service = new WorldMapService(paths);
            var before = SourceHashes(paths, selection);
            using var session = await service.OpenAsync(selection, TestContext.CancellationToken);
            var tile = await session.MapAsync(new() { Selection = selection, MinX = 12, Side = 6, Step = 16 }, TestContext.CancellationToken);
            CollectionAssert.AreEqual(KnownSamples, tile.Known.Take(6).ToArray());
            CollectionAssert.AreEqual(SurfaceHeights, tile.Heights.Take(5).ToArray());
            var firstColor = paletteShape switch { "texture" => 0x1188aau, "string" or "mixed" => 0x866343u, _ => 0xe6c748u };
            CollectionAssert.AreEqual(new uint[] { firstColor, 0x58d1c7, 0x995e52, 0x995e52, 0xdde2df }, tile.SurfaceColors.Take(5).ToArray());
            Assert.IsTrue(tile.Known.Skip(6).All(known => !known));
            Assert.IsEmpty(tile.Warnings);
            var pixels = SeedMapImage.Pixels(tile);
            Assert.AreEqual(0, pixels[(5 * 4) + 3], "Missing chunks must remain transparent, including in PNG exports.");
            Assert.AreEqual(255, pixels[3]);
            // Surface compaction must reload real section data when the player switches to a Y slice.
            var slice = await session.MapAsync(new() { Selection = selection, MinX = 12, Side = 1, Step = 1, Y = 70 }, TestContext.CancellationToken);
            Assert.AreEqual(firstColor, slice.SurfaceColors.Single());
            var below = await session.MapAsync(new() { Selection = selection, MinX = 12, Side = 1, Step = 1, Y = 69 }, TestContext.CancellationToken);
            Assert.AreEqual(0x303944u, below.SurfaceColors.Single());
            CollectionAssert.AreEqual(before, SourceHashes(paths, selection), "Reading or rendering must not upgrade or write the save.");
        });
    }

    /// <summary>McRegion and alpha per-chunk files share their stored byte-array shape; no 26.3 gate or biome guess is allowed.</summary>
    [TestMethod]
    [DataRow("mcregion")]
    [DataRow("alpha")]
    public async Task SavedMap_LegacyByteArraysReadWithoutAnvilOrRuntime(string format)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, format);
            var tile = await new WorldMapService(paths).ReadMapAsync(new() { Selection = selection, MinX = 12, Side = 1, Step = 1 }, TestContext.CancellationToken);
            Assert.IsTrue(tile.Known[0]);
            Assert.AreEqual(62, tile.Heights[0]);
            Assert.AreEqual(0xb3915bu, tile.SurfaceColors[0]);
            Assert.AreEqual("unknown", tile.Palette[tile.Biomes[0]], "A format that did not save biomes must not fabricate a biome observation.");
        });
    }

    /// <summary>Seeds exceed JavaScript's exact integer range, saved players have real dimensions and doubles, and neither missing runtime files nor absent live games imply an origin marker.</summary>
    [TestMethod]
    public async Task WorldMetadata_PreservesLosslessSeedSavedPositionAndSelectionScope()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, "anvil");
            var service = new WorldMapService(paths);
            var snapshot = await service.DescribeAsync(selection, TestContext.CancellationToken);
            Assert.AreEqual("-9223372036854775797", snapshot.Seed);
            var player = snapshot.Players.Single();
            Assert.AreEqual("the_nether", player.Dimension);
            Assert.AreEqual(123.25, player.X); Assert.AreEqual(-88.75, player.Z);
            Assert.AreEqual(45f, player.Yaw);
            Assert.AreEqual(-32, snapshot.Spawn!.X);
            var json = JsonSerializer.Serialize(snapshot, WorldMapJsonContext.Default.WorldMapSnapshot);
            Assert.Contains("\"seed\":\"-9223372036854775797\"", json);
            var observed = await service.ObservePlayerAsync(selection, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("saved", observed.State); Assert.AreEqual(player.X, observed.Player!.X);
            var absent = await service.ObservePlayerAsync(selection, "another-player", TestContext.CancellationToken);
            Assert.AreEqual("unavailable", absent.State); Assert.IsNull(absent.Player);
            await Assert.ThrowsAsync<ArgumentException>(() => service.DescribeAsync(selection with { World = "../outside" }, TestContext.CancellationToken));
        });
    }

    /// <summary>A finished tile does not authorize reusing decoded chunks after a region write; refresh creates a new independent revision.</summary>
    [TestMethod]
    public async Task SavedMap_SourceRevisionChangeRejectsCachedReadsAndReopenRecovers()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, "anvil");
            var service = new WorldMapService(paths);
            using var session = await service.OpenAsync(selection, TestContext.CancellationToken);
            var request = new WorldMapQuery { Selection = selection, MinX = 12, Side = 1, Step = 1 };
            Assert.IsTrue((await session.MapAsync(request, TestContext.CancellationToken)).Known[0]);
            var file = Path.Combine(new InstanceStore(paths).Get(selection.InstanceId).GameDirectory, "saves", selection.World, "region", "r.0.0.mca");
            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(7));
            await Assert.ThrowsAsync<IOException>(() => session.MapAsync(request, TestContext.CancellationToken));
            using var refreshed = await service.OpenAsync(selection, TestContext.CancellationToken);
            Assert.AreNotEqual(session.Snapshot.Revision, refreshed.Snapshot.Revision);
            Assert.IsTrue((await refreshed.MapAsync(request, TestContext.CancellationToken)).Known[0]);
        });
    }

    /// <summary>Full legends precede chunk sampling; saved child bounds supply actual markers, while an observed unregistered biome must survive the registry merge.</summary>
    [TestMethod]
    public async Task SavedMap_LegendsUseInstalledResourcesAndMarkersUseSavedChildBounds()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, "anvil");
            await AddOriginalResourcesAsync(paths, selection, TestContext.CancellationToken);
            var region = Path.Combine(new InstanceStore(paths).Get(selection.InstanceId).GameDirectory, "saves", selection.World, "region", "r.0.0.mca");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", "structure.mca.fixture"), region, true);
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            var before = session.GetCatalog();
            Assert.HasCount(3, before.Biomes); Assert.HasCount(2, before.Structures);
            Assert.AreEqual("Synthetic Forest", before.Names["forest"]);
            Assert.AreEqual("the_nether", before.Structures.Single(structure => structure.Id == "fortress").Dimensions.Single());
            var dimension = before.Dimensions.Single(item => item.Id == "overworld");
            Assert.AreEqual(-64, dimension.MinY); Assert.AreEqual(384, dimension.Height);
            var tile = await session.MapAsync(new() { Selection = selection, MinX = 12, Side = 1, Step = 1 }, TestContext.CancellationToken);
            var marker = tile.Structures.Single();
            Assert.AreEqual("village_plains", marker.Id); Assert.AreEqual(20, marker.X); Assert.AreEqual(60, marker.Y); Assert.AreEqual(25, marker.Z);
            CollectionAssert.AreEqual(SavedVillageBounds, marker.Bounds, "The preview must retain the full saved child bounds.");
            // A radius search must use recorded starts even when the selected point tile did not sample their start chunk.
            var found = await session.FindStructuresAsync(new() { Selection = selection, CenterX = 20, CenterZ = 25, Radius = 32, Targets = ["village_plains"] }, TestContext.CancellationToken);
            Assert.AreEqual("saved", found.Source); Assert.HasCount(1, found.Positions);
            Assert.AreEqual(marker.X, found.Positions[0].X); Assert.AreEqual(marker.Z, found.Positions[0].Z);
            Assert.IsEmpty((await session.FindStructuresAsync(new() { Selection = selection, CenterX = 20, CenterZ = 25, Radius = 32, Targets = ["fortress"] }, TestContext.CancellationToken)).Positions);
            Assert.AreEqual("saved", tile.Source);
            Assert.HasCount(2, session.GetCatalog().Structures);
            Assert.Contains(biome => biome.Id == "example:glowing_grove" && biome.Dimensions.Contains("overworld"), session.GetCatalog().Biomes);
            Assert.Contains(biome => biome.Id == "forest", session.GetCatalog().Biomes, "An unsampled registry entry must remain in the complete legend.");
        });
    }

    private static async Task AddOriginalResourcesAsync(LauncherPaths paths, WorldMapSelection selection, CancellationToken ct)
    {
        var runtime = new InstanceStore(paths).Get(selection.InstanceId).MinecraftDirectory;
        var version = Path.Combine(runtime, "versions", "1.12.2"); Directory.CreateDirectory(version);
        await File.WriteAllTextAsync(Path.Combine(version, "1.12.2.json"), """{"id":"1.12.2","mainClass":"original.Main"}""", ct);
        Copy("1.12.2.jar", version, "textures.jar");
    }

    private static async Task<WorldMapSelection> PrepareAsync(LauncherPaths paths, string format)
    {
        var instance = await new InstanceStore(paths).CreateAsync("Synthetic map instance", "1.12.2");
        var directory = Path.Combine(instance.GameDirectory, "saves", "Synthetic");
        Directory.CreateDirectory(directory);
        Copy("level.dat", directory, format == "anvil" ? "level.dat" : "legacy-level.dat");
        if (format == "alpha")
        {
            var chunkDirectory = Path.Combine(directory, "0", "0");
            Directory.CreateDirectory(chunkDirectory); Copy("c.0.0.dat", chunkDirectory);
        }
        else
        {
            var region = Path.Combine(directory, "region"); Directory.CreateDirectory(region);
            Copy(format == "anvil" ? "r.0.0.mca" : "r.0.0.mcr", region);
            if (format == "anvil") Copy("c.4.0.mcc", region);
        }

        return new(instance.Id, "Synthetic");
    }

    /// <summary>Protects the observed split-storage contract: metadata, local identity and namespaced region files belong to one save even without inline generation/player tags.</summary>
    [TestMethod]
    public async Task SavedMap_SplitStorageReadsSeedOwnPlayerAndNamespacedChunks()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, "anvil");
            var directory = Path.Combine(new InstanceStore(paths).Get(selection.InstanceId).GameDirectory, "saves", selection.World);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", "split-level.dat.fixture"), Path.Combine(directory, "level.dat"), true);
            var data = Path.Combine(directory, "data", "minecraft"); Directory.CreateDirectory(data); Copy("world_gen_settings.dat", data);
            var dimension = Path.Combine(directory, "dimensions", "minecraft", "overworld"); Directory.CreateDirectory(dimension);
            Directory.Move(Path.Combine(directory, "region"), Path.Combine(dimension, "region"));
            var players = Path.Combine(directory, "players", "data"); Directory.CreateDirectory(players);
            Copy("00000001-0000-0002-0000-000300000004.dat", players, "split-player.dat");
            Copy("00000009-0000-0008-0000-000700000006.dat", players, "other-player.dat");
            var before = SourceHashes(paths, selection);
            var service = new WorldMapService(paths);
            using var session = await service.OpenAsync(selection, TestContext.CancellationToken);
            Assert.AreEqual("-9223372036854775797", session.Snapshot.Seed);
            CollectionAssert.AreEquivalent(ExpectedDimensions, session.Snapshot.Dimensions);
            var observation = await service.ObservePlayerAsync(session.Snapshot, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("saved", observation.State); Assert.AreEqual("local", observation.Player!.Id);
            Assert.AreEqual(123.25, observation.Player.X, "The save's local UUID must win over another player's newer record.");
            Assert.AreEqual("the_nether", observation.Player.Dimension);
            var query = new WorldMapQuery { Selection = selection, MinX = 12, Side = 1, Step = 1 };
            var tile = await session.MapAsync(query, TestContext.CancellationToken);
            Assert.IsTrue(tile.Known.Single()); Assert.AreEqual(70, tile.Heights.Single());
            CollectionAssert.AreEqual(before, SourceHashes(paths, selection));
            var generation = Path.Combine(data, "world_gen_settings.dat");
            File.SetLastWriteTimeUtc(generation, File.GetLastWriteTimeUtc(generation).AddSeconds(5));
            await Assert.ThrowsAsync<IOException>(() => session.MapAsync(query, TestContext.CancellationToken));
        });
    }

    /// <summary>Protects the real JSON request boundary: omitted optional properties must retain defaults under source generation and NativeAOT.</summary>
    [TestMethod]
    public async Task SavedMap_JsonRequestRetainsDefaultsAndExecutesCoreRead()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var selection = await PrepareAsync(paths, "anvil");
            var json = $$"""{"selection":{"instanceId":"{{selection.InstanceId}}","world":"{{selection.World}}"},"minX":12,"side":1}""";
            var query = JsonSerializer.Deserialize(json, WorldMapJsonContext.Default.WorldMapQuery)!;
            var tile = await new WorldMapService(paths).ReadMapAsync(query, TestContext.CancellationToken);
            Assert.AreEqual("overworld", tile.World.Dimension); Assert.AreEqual(16, tile.Step);
            Assert.IsTrue(tile.Known.Single()); Assert.AreEqual(70, tile.Heights.Single());
        });
    }

    /// <summary>Noncubic Indev and Java-serialized Classic worlds preserve axes and map bounds; absent seeds and player observations must stay absent.</summary>
    [TestMethod]
    [DataRow("indev.mclevel", 1, 6)]
    [DataRow("classic.mine", 1, 3)]
    public async Task SavedMap_FiniteWorldReadsNativeDataWithoutDeserializingClasses(string file, int z, int expectedHeight)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("Finite map", "old_alpha", TestContext.CancellationToken);
            var directory = Path.Combine(instance.GameDirectory, "saves"); Directory.CreateDirectory(directory); Copy(file, directory);
            var service = new WorldMapService(paths);
            Assert.AreEqual(file, (await service.ListAsync(instance.Id, TestContext.CancellationToken)).Single().Folder);
            var selection = new WorldMapSelection(instance.Id, file);
            using var session = await service.OpenAsync(selection, TestContext.CancellationToken);
            Assert.IsNull(session.Snapshot.Seed); Assert.IsEmpty(session.Snapshot.Players);
            var tile = await session.MapAsync(new() { Selection = selection, MinX = 2, MinZ = z, Step = 3, Side = 2 }, TestContext.CancellationToken);
            Assert.IsTrue(tile.Known[0]); Assert.AreEqual(expectedHeight, tile.Heights[0]);
            Assert.IsTrue(tile.Known.Skip(1).All(known => !known), "Finite-map borders must stay unknown instead of wrapping into another row.");
            Assert.IsNull((await service.ObservePlayerAsync(session.Snapshot, cancellationToken: TestContext.CancellationToken)).Player);
        });
    }

    private static void Copy(string name, string directory, string? fixture = null) =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", (fixture ?? name) + ".fixture"), Path.Combine(directory, name));

    private static string[] SourceHashes(LauncherPaths paths, WorldMapSelection selection)
    {
        var root = Path.Combine(new InstanceStore(paths).Get(selection.InstanceId).GameDirectory, "saves");
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))];
    }

    public TestContext TestContext { get; set; }
}