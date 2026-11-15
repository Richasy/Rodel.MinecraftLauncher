using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects offline pack inspection/import against loss of optional choices and original manifest data.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PackWorkflowTests
{
    /// <summary>
    /// Inspection and offline import must agree about an excluded optional client file, retain unknown
    /// manifest values, and leave the source unchanged. No existing content fixture exercises a pack manifest,
    /// optional selection, or the inspection-to-persistent-lock boundary.
    /// </summary>
    [TestMethod]
    public async Task InspectThenImport_OfflineExcludedOptionalFile_PreservesManifestAndChoices()
    {
        using var cli = new CliSandbox();
        Directory.CreateDirectory(cli.Root);
        var source = Path.Combine(cli.Root, "optional.mrpack");
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open());
            writer.Write("""
                {
                  "formatVersion": 1,
                  "game": "minecraft",
                  "name": "Offline pack",
                  "versionId": "fixture-1",
                  "dependencies": { "minecraft": "synthetic-version", "future-loader": "unknown-version" },
                  "futureMetadata": { "nullable": null, "array": [true, 17, "retained"] },
                  "files": [{
                    "path": "mods/optional.jar",
                    "hashes": { "sha1": "0000000000000000000000000000000000000000" },
                    "downloads": ["https://example.invalid/optional.jar"],
                    "fileSize": 1,
                    "env": { "client": "optional", "server": "unsupported" },
                    "futureFileField": { "retained": true }
                  }]
                }
                """);
        }

        var original = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
        var inspection = (await cli.RunAsync("pack", "inspect", source)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("synthetic-version", inspection["minecraftVersion"]?.ToString());
        Assert.AreEqual("unknown-version", inspection["dependencies"]?["future-loader"]?.ToString());
        Assert.IsTrue(inspection["optionalFiles"]![0]?["futureFileField"]?["retained"]?.GetValue<bool>());

        var imported = (await cli.RunAsync("pack", "import", source, "--offline", "--content-only",
            "--optional-policy", "exclude-all")).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(imported["complete"]?.GetValue<bool>());
        var game = imported["instance"]!["gameDirectory"]!.GetValue<string>();
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "optional.jar")), "An excluded optional file must not become a required manual download.");
        var packLock = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(game, ".rodel", "pack-lock.json"), TestContext.CancellationToken))!;
        Assert.IsTrue(System.Text.Json.Nodes.JsonNode.DeepEquals(inspection["manifest"], packLock["manifest"]));
        Assert.AreEqual("exclude-all", packLock["optionalSelection"]?["policy"]?.ToString());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
    }

    /// <summary>
    /// Export borrows independently tracked content, whose metadata may be absent, null, an array or a scalar.
    /// An object-only ownership model would reject usable publisher references before writing the archive.
    /// Unlike import tests, this exercises content-lock provenance crossing into a real exported ZIP, while
    /// checking that neither player packages nor the original tracking document are rewritten.
    /// </summary>
    [TestMethod]
    public async Task Export_TrackedContentWithOpenMetadata_PreservesReferencesAndSource()
    {
        using var cli = new CliSandbox();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = await client.Instances.CreateAsync("Open metadata", "synthetic-version", TestContext.CancellationToken);
        var records = new JsonArray();
        JsonNode?[] metadata = [null, new JsonArray("legacy mod"), JsonValue.Create("future metadata"), new JsonObject { ["future"] = null }];
        for (var index = 0; index <= metadata.Length; index++)
        {
            var relative = $"mods/package-{index}.jar";
            var file = Path.Combine(instance.GameDirectory, "mods", $"package-{index}.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            ContentFixture.WritePackage(file, "exported package " + index);
            var bytes = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
            var record = new JsonObject
            {
                ["path"] = relative,
                ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(),
                ["platform"] = "modrinth",
                ["downloadUrl"] = $"https://example.invalid/package-{index}.jar",
                ["futureTracking"] = new JsonArray(null, 17)
            };
            if (index < metadata.Length) record["metadata"] = metadata[index]?.DeepClone();
            records.Add(record);
        }

        var trackingFile = Path.Combine(instance.GameDirectory, ".rodel", "content-lock.json");
        await JsonFiles.WriteAsync(trackingFile, new JsonObject { ["schemaVersion"] = 1, ["files"] = records }, TestContext.CancellationToken);
        var before = await File.ReadAllBytesAsync(trackingFile, TestContext.CancellationToken);
        var target = Path.Combine(cli.Root, "export.mrpack");

        var result = (await cli.RunAsync("pack", "export", instance.Id, target)).ReadEnvelope(0)["data"]!;

        Assert.AreEqual(5, result["referencedFiles"]!.GetValue<int>());
        Assert.IsTrue(result["complete"]!.GetValue<bool>());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(trackingFile, TestContext.CancellationToken));
        using var archive = ZipFile.OpenRead(target);
        using var manifestStream = archive.GetEntry("modrinth.index.json")!.Open();
        var manifest = JsonNode.Parse(manifestStream)!;
        Assert.HasCount(5, manifest["files"]!.AsArray());
        using var ownershipStream = archive.GetEntry("rodel.pack-lock.json")!.Open();
        var ownership = JsonNode.Parse(ownershipStream)!["files"]!.AsArray();
        for (var index = 0; index <= metadata.Length; index++)
        {
            var record = ownership.Single(item => item?["path"]?.ToString() == $"mods/package-{index}.jar")!;
            if (index < metadata.Length) Assert.IsTrue(JsonNode.DeepEquals(metadata[index], record["metadata"]));
            Assert.IsTrue(JsonNode.DeepEquals(records[index]!["futureTracking"], record["futureTracking"]));
            var file = Path.Combine(instance.GameDirectory, "mods", $"package-{index}.jar");
            var bytes = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
            Assert.AreEqual(records[index]!["sha1"]!.GetValue<string>(), Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant());
        }
    }

    /// <summary>
    /// Review: sharing must not accidentally become a personal-instance backup or local-mod redistribution.
    /// Existing export provenance tests do not cover this profile. A real CLI export and independently read
    /// archive distinguish retained shared configuration/reference hashes from personal and disabled files;
    /// the recipient must retain a hash-checked missing requirement and complete it with independently supplied
    /// bytes. A sender-only excluded receipt cannot establish that recovery boundary.
    /// </summary>
    [TestMethod]
    [DataRow("mrpack", "mods/local.jar", false)]
    [DataRow("mrpack", "mods/known.jar", true)]
    [DataRow("mrpack", "resourcepacks/local.zip", false)]
    [DataRow("curseforge", "shaderpacks/local.zip", false)]
    public async Task Export_ForMultiplayer_ExcludesPersonalAndUnreferencedFiles(string format, string requiredPath, bool knownSource)
    {
        using var cli = new CliSandbox();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = await client.Instances.CreateAsync("Share", "synthetic-version", TestContext.CancellationToken);
        foreach (var relative in new[] { "options.txt", "servers.dat", "saves/world/level.dat", "config/shared.cfg", requiredPath, "mods/disabled.jar.disabled" })
        {
            var path = Path.Combine(instance.GameDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, relative, TestContext.CancellationToken);
        }

        if (knownSource)
        {
            var hash = Convert.ToHexStringLower(SHA1.HashData(await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, requiredPath), TestContext.CancellationToken)));
            await JsonFiles.WriteAsync(Path.Combine(instance.GameDirectory, ".rodel/content-lock.json"), new JsonObject
            {
                ["files"] = new JsonArray(new JsonObject { ["path"] = requiredPath, ["sha1"] = hash, ["platform"] = "curseforge", ["projectId"] = "123", ["versionId"] = "456" })
            }, TestContext.CancellationToken);
        }

        var output = Path.Combine(cli.Root, "shared.zip");
        var result = (await cli.RunAsync("pack", "export", instance.Id, output, "--format", format, "--for-multiplayer", "--include-local-mods")).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(result["complete"]!.GetValue<bool>(), "The profile cannot silently include local binaries even when the ordinary export flag is supplied.");
        Assert.AreEqual(requiredPath, result["excluded"]![0]!["path"]?.ToString());
        using var archive = ZipFile.OpenRead(output);
        Assert.IsNotNull(archive.GetEntry("overrides/config/shared.cfg"));
        Assert.DoesNotContain(entry => entry.FullName.Contains("options.txt", StringComparison.Ordinal) || entry.FullName.Contains("servers.dat", StringComparison.Ordinal) || entry.FullName.Contains("saves/", StringComparison.Ordinal) || entry.FullName.StartsWith("overrides/mods/", StringComparison.Ordinal), archive.Entries);
        Assert.AreEqual(requiredPath, await File.ReadAllTextAsync(Path.Combine(instance.GameDirectory, requiredPath), TestContext.CancellationToken));
        Assert.IsNotNull(archive.GetEntry("multiplayer-missing-files.txt"));
        var indexName = format == "mrpack" ? "modrinth.index.json" : "manifest.json";
        var filesKey = format == "mrpack" ? "files" : "rodelMissingFiles";
        using var index = new StreamReader(archive.GetEntry(indexName)!.Open());
        var manifest = JsonNode.Parse(await index.ReadToEndAsync(TestContext.CancellationToken))!;
        var requirement = manifest[filesKey]!.AsArray().Single()!;
        Assert.AreEqual(requiredPath, requirement["path"]?.ToString());
        if (knownSource) Assert.AreEqual("123", requirement["rodelSource"]?["projectId"]?.ToString());
        Assert.HasCount(0, requirement["downloads"]!.AsArray());
        var expected = await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, requiredPath), TestContext.CancellationToken);
        Assert.AreEqual(Convert.ToHexStringLower(SHA512.HashData(expected)), requirement["hashes"]?["sha512"]?.ToString());

        using var receiver = new CliSandbox();
        var imported = (await receiver.RunAsync("pack", "import", output, "--offline")).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(imported["complete"]!.GetValue<bool>());
        Assert.HasCount(1, imported["missing"]!.AsArray());
        Assert.AreEqual(requiredPath, imported["missing"]![0]!["path"]?.ToString());
        var id = imported["instance"]!["id"]!.GetValue<string>();
        var recipient = new LauncherClient(new LauncherPaths(receiver.Root));
        var game = recipient.Instances.Get(id).GameDirectory;
        Assert.AreEqual("config/shared.cfg", await File.ReadAllTextAsync(Path.Combine(game, "config/shared.cfg"), TestContext.CancellationToken));
        var retainedExport = Path.Combine(receiver.Root, "incomplete.mrpack");
        (await receiver.RunAsync("pack", "export", id, retainedExport, "--format", format, "--for-multiplayer")).ReadEnvelope(0);
        using (var retained = ZipFile.OpenRead(retainedExport))
        using (var reader = new StreamReader(retained.GetEntry(indexName)!.Open()))
            Assert.IsTrue(JsonNode.DeepEquals(requirement, JsonNode.Parse(await reader.ReadToEndAsync(TestContext.CancellationToken))![filesKey]![0]), "Re-exporting an incomplete pack must not forget its missing requirements.");
        var selected = Path.Combine(receiver.Root, "selected.jar");
        await File.WriteAllTextAsync(selected, "wrong package", TestContext.CancellationToken);
        var staged = (await receiver.RunAsync("pack", "stage-file", id, requiredPath, selected)).ReadEnvelope(0)["data"]!;
        var manual = staged["manualDirectory"]!.GetValue<string>();
        (await receiver.RunAsync("pack", "complete", id, "--offline", "--manual-directory", manual)).ReadEnvelope(1);
        Assert.IsFalse(File.Exists(Path.Combine(game, requiredPath)));
        await File.WriteAllBytesAsync(selected, expected, TestContext.CancellationToken);
        (await receiver.RunAsync("pack", "stage-file", id, requiredPath, selected)).ReadEnvelope(0);
        var completed = (await receiver.RunAsync("pack", "complete", id, "--offline", "--manual-directory", manual)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(completed["complete"]!.GetValue<bool>());
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(Path.Combine(game, requiredPath), TestContext.CancellationToken));
        if (knownSource)
        {
            var record = recipient.Packs.GetState(id).Pack!.Files.Single(item => item.Path == requiredPath);
            Assert.AreEqual("curseforge", record.Platform);
            Assert.AreEqual("123", record.ProjectId);
            Assert.IsFalse(record.IdentityVerified);
            var cf = Path.Combine(receiver.Root, "unverified-source.zip");
            (await receiver.RunAsync("pack", "export", id, cf, "--format", "curseforge", "--for-multiplayer")).ReadEnvelope(0);
            using var cfArchive = ZipFile.OpenRead(cf);
            using var cfIndex = new StreamReader(cfArchive.GetEntry("manifest.json")!.Open());
            var cfManifest = JsonNode.Parse(await cfIndex.ReadToEndAsync(TestContext.CancellationToken))!;
            Assert.HasCount(0, cfManifest["files"]!.AsArray(), "An unverified sender claim cannot become a hashless publisher reference.");
            Assert.AreEqual(requirement["hashes"]?["sha512"]?.ToString(), cfManifest["rodelMissingFiles"]![0]!["hashes"]?["sha512"]?.ToString());
        }
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}