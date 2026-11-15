using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects save discovery degradation and interrupted-recovery visibility across actual persisted records.</summary>
[TestClass]
public sealed class WorldReadTests
{
    /// <summary>
    /// A broken level.dat must not hide a save; interrupted retained folders and unreadable history must remain visible.
    /// No existing Core suite covers these paths. Comparing the serialized record with the original also detects
    /// lossy projection of unknown fields, explicit nulls and noncanonical metadata during typed reporting.
    /// </summary>
    [TestMethod]
    public async Task List_DamagedSaveAndInterruptedRecovery_PreservesUsableFoldersAndRecords()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("world-read", "synthetic", TestContext.CancellationToken);
            var saves = Path.Combine(instance.GameDirectory, "saves");
            var good = Path.Combine(saves, "good");
            var broken = Path.Combine(saves, "broken");
            Directory.CreateDirectory(good);
            Directory.CreateDirectory(broken);
            // Uncompressed NBT: root/Data/{LevelName:"Named",GameType:1,hardcore:0}.
            await File.WriteAllBytesAsync(Path.Combine(good, "level.dat"), Convert.FromHexString(
                "0A00000A0004446174610800094C6576656C4E616D6500054E616D656403000847616D65547970650000000101000868617264636F7265000000"), TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(broken, "level.dat"), "truncated", TestContext.CancellationToken);
            var service = new WorldService(paths);
            var worlds = await service.ListAsync(instance.Id, TestContext.CancellationToken);
            Assert.HasCount(2, worlds);
            var healthy = worlds.Single(world => world.Folder == "good");
            Assert.AreEqual("Named", healthy.Name);
            Assert.AreEqual(1, healthy.GameType);
            Assert.IsFalse(healthy.Hardcore);
            Assert.IsNull(healthy.Warning);
            var damaged = worlds.Single(world => world.Folder == "broken");
            Assert.AreEqual("broken", damaged.Name);
            Assert.IsFalse(string.IsNullOrEmpty(damaged.Warning));
            var saveWire = JsonSerializer.SerializeToNode(worlds, WorldJsonContext.Default.IReadOnlyListWorldSummary)!.AsArray();
            Assert.IsFalse(saveWire.Single(row => row!["folder"]!.GetValue<string>() == "good")!.AsObject().ContainsKey("warning"));

            var history = Path.Combine(instance.GameDirectory, ".rodel", "world-history");
            var recoveryId = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(history, recoveryId));
            var record = new JsonObject
            {
                ["id"] = recoveryId,
                ["instanceId"] = instance.Id,
                ["world"] = "good",
                ["state"] = "preparing",
                ["name"] = null,
                ["createdAt"] = new JsonObject { ["vendorTimestamp"] = 17 },
                ["vendor"] = new JsonArray("preserve", null, 7)
            };
            await File.WriteAllTextAsync(Path.Combine(history, recoveryId + ".json"), record.ToJsonString(), TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(history, "broken.json"), "{", TestContext.CancellationToken);
            var recovery = service.ListRecovery(instance.Id);
            Assert.HasCount(2, recovery);
            Assert.IsFalse(string.IsNullOrEmpty(recovery.Single(row => row.Id == "broken").Error));
            var wire = JsonSerializer.SerializeToNode(recovery, WorldJsonContext.Default.IReadOnlyListWorldArchiveRecord)!.AsArray();
            var retained = wire.Single(row => row!["id"]!.GetValue<string>() == recoveryId)!.AsObject();
            Assert.IsFalse(string.IsNullOrEmpty(retained["warning"]!.GetValue<string>()));
            retained.Remove("warning");
            Assert.IsTrue(JsonNode.DeepEquals(record, retained), "Reading interrupted recovery must preserve the original record's data.");
            Assert.IsTrue(Directory.Exists(Path.Combine(history, recoveryId)));
            Assert.AreEqual(record.ToJsonString(), await File.ReadAllTextAsync(Path.Combine(history, recoveryId + ".json"), TestContext.CancellationToken));
        });
    }

    /// <summary>Gets or sets the test context supplying cancellation for save discovery and fixture writes.</summary>
    public TestContext TestContext { get; set; }
}