// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects saved 3D/analysis/inventory provenance separately from 2D map sampling and native generation.</summary>
[TestClass]
public sealed class SavedRegionReadTests
{
    /// <summary>Review: native void chunks contain an empty section list, whereas missing block storage is unreadable. Existing fixtures cover missing chunks but not this distinction; treating both as unknown hides valid End terrain and breaks generated inventories.</summary>
    [TestMethod]
    [DataRow("Sections")]
    [DataRow("sections")]
    public void SavedVoidChunk_EmptySectionsRemainReadableAirWhileMissingStorageIsRejected(string sectionName)
    {
        var data = new Dictionary<string, object> { ["xPos"] = 0, ["zPos"] = 0, [sectionName] = new List<object>() };
        var chunk = new SavedChunk(data, 0, 0);
        Assert.AreEqual("minecraft:air", chunk.State(4, 70, 8).Id);
        Assert.AreEqual(-1, chunk.Sample(4, 8, null).Y, "A saved void column ends below its minimum height and must not become unknown terrain.");
        data.Remove(sectionName);
        Assert.ThrowsExactly<InvalidDataException>(() => new SavedChunk(data, 0, 0));
    }

    /// <summary>Gets or sets the runner's cancellation and test lifecycle context.</summary>
    public TestContext TestContext { get; set; }

    /// <summary>Review: a seed preview cannot preserve player edits, state orientation, saved stacks or unknown chunks. An independent NBT oracle crosses real file/region/model readers without a runnable generation profile.</summary>
    [TestMethod]
    public async Task Region_ReadsSavedStatesInventoriesUnknownCoverageAndOriginalModelsWithoutGeneratingLoot()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("Saved inspection", "1.12.2", TestContext.CancellationToken);
            var save = Path.Combine(instance.GameDirectory, "saves", "Synthetic"); Directory.CreateDirectory(Path.Combine(save, "region"));
            Copy("split-level.dat", Path.Combine(save, "level.dat"));
            Copy("saved-region.mca", Path.Combine(save, "region", "r.0.0.mca"));
            var version = Path.Combine(instance.MinecraftDirectory, "versions", "1.12.2"); Directory.CreateDirectory(version);
            await File.WriteAllTextAsync(Path.Combine(version, "1.12.2.json"), """{"id":"1.12.2","mainClass":"not.a.runnable.GenerationProfile"}""", TestContext.CancellationToken);
            Copy("textures.jar", Path.Combine(version, "1.12.2.jar"));
            var hashes = Hashes(save);
            var selection = new WorldMapSelection(instance.Id, "Synthetic");
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            Assert.IsNull(session.Snapshot.Seed, "Saved inspection must remain usable when no numeric seed can be read.");
            var query = new WorldRegionQuery { Selection = selection, IncludeBlocks = true, Bounds = new() { MinX = 12, MaxX = 16, MinZ = 0, MaxZ = 0, MinY = 70, MaxY = 71 } };
            var result = await session.RegionAsync(query, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("saved_snapshot_v1", result.GenerationPolicy);
            Assert.AreEqual(2L, result.UnknownBlocks); Assert.AreEqual(8L, result.Materials.Sum(item => item.Count));
            Assert.AreEqual(4L, result.Materials.Single(item => item.Id == "minecraft:air").Count, "Missing chunks cannot be silently counted as air.");
            Assert.AreEqual(1L, result.Materials.Single(item => item.Id == "minecraft:diamond_block").Count);
            Assert.AreEqual("x", result.Palette.Single(state => state.Id == "minecraft:oak_log").Properties["axis"]);
            Assert.HasCount(1, result.BiomePalette);
            Assert.AreEqual("rodel:unknown", result.BiomePalette[0], "Absent historical biome storage cannot become guessed plains.");
            Assert.HasCount(result.Blocks.Length, result.Biomes);
            var inventory = result.Containers.Single(container => container.X == 13);
            Assert.AreEqual("saved", inventory.State);
            var item = inventory.Items.Single(); Assert.AreEqual(3, item.Slot); Assert.AreEqual(5, item.Count); Assert.AreEqual("minecraft:diamond", item.Id);
            Assert.AreEqual(9223372036854775797L, item.Components["example:exact"].GetInt64());
            var unopened = result.Containers.Single(container => container.X == 14);
            Assert.AreEqual("unevaluated", unopened.State); Assert.IsEmpty(unopened.Items); Assert.AreEqual("123456", unopened.LootSeed);
            var scene = await session.BuildSceneAsync(result, TestContext.CancellationToken);
            Assert.IsGreaterThan(0, scene.CreateMesh(70, 71, TestContext.CancellationToken).Blocks);
            Assert.HasCount(1, await session.ReadItemImagesAsync([item.Id], TestContext.CancellationToken));
            CollectionAssert.AreEqual(hashes, Hashes(save), "Analysis, models and item artwork must not upgrade the save or fill the unopened chest.");
        });
    }

    /// <summary>Review: frozen 3D previously discarded stored biomes. Modern section biomes and legacy numeric columns must survive beside the exact states, while genuinely absent biome/chunk data stays unknown; no native color producer or runnable game is involved.</summary>
    [TestMethod]
    public async Task Region_PreservesSavedSectionAndLegacyBiomesWithoutInventingMissingData()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("Saved biome inspection", "1.12.2", TestContext.CancellationToken);
            var save = Path.Combine(instance.GameDirectory, "saves", "Synthetic");
            Directory.CreateDirectory(Path.Combine(save, "region"));
            Copy("level.dat", Path.Combine(save, "level.dat"));
            Copy("r.0.0.mca", Path.Combine(save, "region", "r.0.0.mca"));
            Copy("c.4.0.mcc", Path.Combine(save, "region", "c.4.0.mcc"));
            var before = Hashes(save);
            var selection = new WorldMapSelection(instance.Id, "Synthetic");
            using var session = await new WorldMapService(paths).OpenAsync(selection, TestContext.CancellationToken);
            var result = await session.RegionAsync(new() { Selection = selection, IncludeBlocks = true, Bounds = new() { MinX = 12, MaxX = 80, MinY = 70, MaxY = 70 } }, cancellationToken: TestContext.CancellationToken);
            string Biome(int x) => result.BiomePalette[BinaryPrimitives.ReadUInt16LittleEndian(result.Biomes.AsSpan((x - 12) * 2))];
            Assert.AreEqual("minecraft:plains", Biome(12));
            Assert.AreEqual("minecraft:forest", Biome(16), "The legacy numeric biome ID 4 is forest even where this height is saved air.");
            Assert.AreEqual("rodel:unknown", Biome(32), "A readable legacy block volume without biome storage remains usable without a guessed biome.");
            Assert.AreEqual("rodel:unknown", Biome(80), "A missing chunk must not inherit the previous chunk's biome.");
            Assert.HasCount(result.Blocks.Length, result.Biomes);
            CollectionAssert.AreEqual(before, Hashes(save));
        });
    }

    /// <summary>Review: worlds may share a seed or lack one; bookmarks must restore source identity and survive a new saved revision without merging distinct worlds.</summary>
    [TestMethod]
    public async Task SavedBookmarks_PreserveWorldIdentityAcrossRevisionsAndJsonRoundTrips()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new SeedBookmarkStore(paths);
            var first = new SeedBookmark
            {
                Kind = "seed",
                Name = "First saved view",
                SavedWorld = new("instance-a", "World"),
                Profile = new() { MinecraftDirectory = paths.Root, VersionId = "historical" },
                Fingerprint = "revision-a",
                Seed = string.Empty
            };
            var a = await store.SaveAsync(first, TestContext.CancellationToken);
            var b = await store.SaveAsync(first with { Name = "Other world", SavedWorld = new("instance-b", "World") }, TestContext.CancellationToken);
            Assert.AreNotEqual(a.Id, b.Id);
            var refreshed = await store.SaveAsync(first with { Fingerprint = "revision-new" }, TestContext.CancellationToken);
            Assert.AreEqual(a.Id, refreshed.Id);
            var loaded = store.List(); Assert.HasCount(2, loaded);
            Assert.AreEqual(first.SavedWorld, loaded.Single(item => item.Id == a.Id).SavedWorld);
            Assert.AreEqual("revision-new", loaded.Single(item => item.Id == a.Id).Fingerprint);
        });
    }

    private static void Copy(string fixture, string destination) => File.Copy(Path.Combine(AppContext.BaseDirectory, "world-map-fixtures", fixture + ".fixture"), destination);
    private static string[] Hashes(string root) => [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))];
}