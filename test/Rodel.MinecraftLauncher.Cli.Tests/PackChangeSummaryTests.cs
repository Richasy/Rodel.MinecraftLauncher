using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects frozen pack change association by declared mod identity, without changing file-level application semantics.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PackChangeSummaryTests
{
    /// <summary>
    /// Renamed artifacts of one declared mod are one update, not unrelated additions/removals.
    /// Unrelated additions, removals and config changes must retain their distinct categories.
    /// Existing file-transaction tests cannot detect filename-based or display-name-based false association.
    /// The real CLI imports and reviews small valid JARs; no public provider or Minecraft process is used.
    /// </summary>
    [TestMethod]
    public async Task Review_RenamedModIdentity_SeparatesUpdatesAdditionsRemovalsAndFiles()
    {
        using var cli = new CliSandbox();
        var original = Archive(cli.Root, "one", [("unrelated-old-name.jar", "alpha", "1"), ("removed.jar", "beta", "1")]);
        var next = Archive(cli.Root, "two", [("renamed-new-artifact.jar", "alpha", "2"), ("added.jar", "gamma", "1")]);
        var imported = (await cli.RunAsync("pack", "import", original, "--offline")).ReadEnvelope(0)["data"]!;
        var id = imported["instance"]!["id"]!.ToString();

        var reviewed = (await cli.RunAsync("pack", "review", id, next, "--offline")).ReadEnvelope(0)["data"]!;
        var summary = reviewed["changeSummary"]!;
        var update = summary["mods"]!.AsArray().Single(mod => mod?["action"]?.ToString() == "update")!;

        Assert.AreEqual("alpha", update["id"]?.ToString());
        Assert.AreEqual("1", update["beforeVersion"]?.ToString());
        Assert.AreEqual("2", update["afterVersion"]?.ToString());
        Assert.AreEqual(1, summary["updatedMods"]?.GetValue<int>());
        Assert.AreEqual(1, summary["addedMods"]?.GetValue<int>());
        Assert.AreEqual(1, summary["removedMods"]?.GetValue<int>());
        Assert.AreEqual(1, summary["otherFiles"]?.GetValue<int>());
        Assert.AreEqual("mods/unrelated-old-name.jar", update["beforePath"]?.ToString());
        Assert.AreEqual("mods/renamed-new-artifact.jar", update["afterPath"]?.ToString());
        Assert.HasCount(2, update["files"]!.AsArray());
        Assert.Contains(action => action?["path"]?.ToString() == "mods/unrelated-old-name.jar" && action["action"]?.ToString() == "delete", reviewed["actions"]!.AsArray());
        Assert.Contains(action => action?["path"]?.ToString() == "mods/renamed-new-artifact.jar" && action["action"]?.ToString() == "add", reviewed["actions"]!.AsArray());
    }

    /// <summary>
    /// Duplicate mod IDs make pairing ambiguous; malformed descriptors must not turn inspection into an
    /// install/launch gate. Keep every raw file action and expose the read warning instead of guessing.
    /// This differs from an ordinary successful association and catches false "one mod update" summaries.
    /// </summary>
    [TestMethod]
    public async Task Review_AmbiguousIdsAndUnreadableMetadata_RetainsRawChangesAndWarnings()
    {
        using var cli = new CliSandbox();
        var original = Archive(cli.Root, "one", [("old-one.jar", "same", "1"), ("old-two.jar", "same", "1")], invalidDescriptor: true);
        var next = Archive(cli.Root, "two", [("next-one.jar", "same", "2"), ("next-two.jar", "same", "2")], invalidDescriptor: true);
        var imported = (await cli.RunAsync("pack", "import", original, "--offline")).ReadEnvelope(0)["data"]!;

        var reviewed = (await cli.RunAsync("pack", "review", imported["instance"]!["id"]!.ToString(), next, "--offline")).ReadEnvelope(0)["data"]!;
        var summary = reviewed["changeSummary"]!;

        Assert.HasCount(0, summary["mods"]!.AsArray());
        Assert.AreEqual(reviewed["actions"]!.AsArray().Count(action => action?["action"]?.ToString() != "unchanged"), summary["files"]!.AsArray().Count);
        Assert.Contains(warning => warning?["path"]?.ToString()?.Contains("invalid-", StringComparison.Ordinal) == true, summary["warnings"]!.AsArray());
        Assert.AreEqual("reviewed", reviewed["phase"]?.ToString());
        Assert.IsTrue(reviewed["incoming"]!["complete"]!.GetValue<bool>());
    }

    /// <summary>Duplicate JSON keys fail during lazy node access; that optional inspection must become a warning, not fail review.</summary>
    [TestMethod]
    public async Task Review_DuplicateMetadataProperties_PreservesUsableFileReview()
    {
        using var cli = new CliSandbox();
        const string duplicate = """{"id":"alpha","id":"beta","version":"1"}""";
        var original = Archive(cli.Root, "one", [("old.jar", string.Empty, string.Empty)], descriptorOverride: duplicate);
        var next = Archive(cli.Root, "two", [("next.jar", string.Empty, string.Empty)], descriptorOverride: duplicate);
        var imported = (await cli.RunAsync("pack", "import", original, "--offline")).ReadEnvelope(0)["data"]!;

        var review = (await cli.RunAsync("pack", "review", imported["instance"]!["id"]!.ToString(), next, "--offline")).ReadEnvelope(0)["data"]!;

        Assert.AreEqual("reviewed", review["phase"]?.ToString());
        Assert.HasCount(0, review["changeSummary"]!["mods"]!.AsArray());
        Assert.HasCount(2, review["changeSummary"]!["warnings"]!.AsArray());
    }

    /// <summary>Legal TOML table comments must not hide a second bundled mod and create a false single-ID update.</summary>
    [TestMethod]
    public async Task Review_CommentedBundledModTable_RetainsUnassociatedFileChanges()
    {
        using var cli = new CliSandbox();
        const string bundled = "[[mods]]\nmodId=\"alpha\"\nversion=\"1\"\n[[mods]] # bundled second mod\nmodId=\"beta\"\nversion=\"1\"\n";
        var original = Archive(cli.Root, "one", [("old.jar", string.Empty, string.Empty)], descriptorOverride: bundled, metadataFormat: "META-INF/mods.toml");
        var next = Archive(cli.Root, "two", [("next.jar", string.Empty, string.Empty)], descriptorOverride: bundled, metadataFormat: "META-INF/mods.toml");
        var imported = (await cli.RunAsync("pack", "import", original, "--offline")).ReadEnvelope(0)["data"]!;

        var review = (await cli.RunAsync("pack", "review", imported["instance"]!["id"]!.ToString(), next, "--offline")).ReadEnvelope(0)["data"]!;

        Assert.HasCount(0, review["changeSummary"]!["mods"]!.AsArray());
        Assert.Contains(file => file?["path"]?.ToString() == "mods/old.jar", review["changeSummary"]!["files"]!.AsArray());
        Assert.Contains(file => file?["path"]?.ToString() == "mods/next.jar", review["changeSummary"]!["files"]!.AsArray());
    }

    private static string Archive(string root, string version, (string File, string Id, string Version)[] mods, bool invalidDescriptor = false,
        string? descriptorOverride = null, string metadataFormat = "fabric.mod.json")
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, version + ".mrpack");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "modrinth.index.json", new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["name"] = "Mod association fixture",
            ["versionId"] = version,
            ["dependencies"] = new JsonObject { ["minecraft"] = "1.21.1" },
            ["files"] = new JsonArray()
        }.ToJsonString());
        Write(archive, "overrides/config/owned.txt", version);
        foreach (var mod in mods)
            Jar(archive, "overrides/mods/" + mod.File, descriptorOverride ?? new JsonObject { ["id"] = mod.Id, ["name"] = mod.Id, ["version"] = mod.Version }.ToJsonString(), metadataFormat);
        if (invalidDescriptor) Jar(archive, "overrides/mods/invalid-" + version + ".jar", "{ invalid");
        return path;
    }

    private static void Jar(ZipArchive archive, string path, string descriptor, string metadataFormat = "fabric.mod.json")
    {
        using var output = archive.CreateEntry(path).Open();
        using var jar = new ZipArchive(output, ZipArchiveMode.Create);
        Write(jar, metadataFormat, descriptor);
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}