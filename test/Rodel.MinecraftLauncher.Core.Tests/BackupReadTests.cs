using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects actual archive validation and lossless reporting independently of write-operation summaries.</summary>
[TestClass]
public sealed class BackupReadTests
{
    /// <summary>
    /// Inspect must validate real archived content while accepting unfamiliar manifest metadata; List must retain individual failures.
    /// No prior suite covers backup reads. Full persisted-manifest equality rejects dropped unknown/null values,
    /// and a same-length content edit independently proves that inspection still verifies digests rather than just reporting metadata.
    /// </summary>
    [TestMethod]
    public async Task Inspect_ExtensibleManifestAndDamagedArchive_PreservesMetadataAndRejectsChangedBytes()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("backup-read", "synthetic", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(instance.GameDirectory, "player-setting.txt"), "before", TestContext.CancellationToken);
            var service = new BackupService(paths);
            var created = await service.CreateAsync(instance.Id, excludeRedownloadable: false, cancellationToken: TestContext.CancellationToken);
            var id = created.Id!;
            var archivePath = Path.Combine(paths.BackupsDirectory, id + ".zip");
            JsonObject original;
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("manifest.json")!;
                using (var input = entry.Open())
                    original = JsonNode.Parse(input)!.AsObject();
                original["createdAt"] = null;
                original["consistent"] = new JsonObject { ["externalAdvisory"] = true };
                original["vendor"] = new JsonArray("retained", null, 3);
                original["files"]![0]!["vendorFile"] = "also retained";
                entry.Delete();
                using var output = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                output.Write(original.ToJsonString());
            }

            var inspected = service.Inspect(id);
            Assert.IsNotNull(inspected.Files);
            Assert.Contains(file => file.Path == "player-setting.txt", inspected.Files);
            Assert.IsTrue(JsonNode.DeepEquals(original, JsonSerializer.SerializeToNode(inspected, BackupJsonContext.Default.BackupManifest)));
            await File.WriteAllTextAsync(Path.Combine(paths.BackupsDirectory, "broken.zip"), "not a ZIP", TestContext.CancellationToken);
            var listed = service.List(instance.Id);
            Assert.HasCount(2, listed);
            Assert.IsFalse(string.IsNullOrEmpty(listed.Single(item => item.Id == "broken").Error));
            Assert.IsTrue(JsonNode.DeepEquals(original, JsonSerializer.SerializeToNode(listed.Single(item => item.Id == id), BackupJsonContext.Default.BackupManifest)));

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("files/player-setting.txt")!;
                entry.Delete();
                using var output = new StreamWriter(archive.CreateEntry("files/player-setting.txt").Open());
                output.Write("after!");
            }

            Assert.ThrowsExactly<InvalidDataException>(() => service.Inspect(id));
            Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(instance.GameDirectory, "player-setting.txt"), TestContext.CancellationToken));
        });
    }

    /// <summary>Gets or sets the test context supplying cancellation for filesystem and archive work.</summary>
    public TestContext TestContext { get; set; }
}