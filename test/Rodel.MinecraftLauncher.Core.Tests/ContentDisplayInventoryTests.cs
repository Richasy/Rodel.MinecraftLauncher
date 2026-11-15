// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects fast display inventory without weakening actual content changes.</summary>
[TestClass]
public sealed class ContentDisplayInventoryTests
{
    /// <summary>
    /// Author review: existing scans always hash files. A display-only scan must retain real package
    /// metadata without presenting an uncomputed identity, while disabling a file changed after that
    /// snapshot must still inspect/hash the current bytes. This crosses actual ZIP, storage and mutation
    /// paths; it does not assert timing or mock the scanner to claim a performance improvement.
    /// </summary>
    [TestMethod]
    public async Task MetadataOnlySnapshot_OmitsHashWhileChangesInspectCurrentBytes() => await ReportStorage.RunAsync(async paths =>
    {
        var game = Path.Combine(paths.Root, "game");
        var mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        var source = Path.Combine(mods, "display.jar");
        WritePackage(source, "one");
        var service = new ContentService(paths);
        var full = (await service.ListAsync(game, "mods", ct: TestContext.CancellationToken)).Single();
        var display = (await service.ListAsync(game, "mods", ct: TestContext.CancellationToken, includeHashes: false)).Single();
        Assert.IsNotNull(full.Sha1);
        Assert.IsNull(display.Sha1, "Display data must not claim to have verified the package identity.");
        Assert.IsTrue(JsonNode.DeepEquals(full.Metadata, display.Metadata));
        Assert.AreEqual(full.Size, display.Size);
        Assert.AreEqual(full.FormatRecognized, display.FormatRecognized);

        File.Delete(source);
        WritePackage(source, "changed");
        var currentHash = Convert.ToHexStringLower(SHA1.HashData(await File.ReadAllBytesAsync(source, TestContext.CancellationToken)));
        Assert.AreNotEqual(full.Sha1, currentHash);
        var disabled = await service.SetEnabledAsync(game, "mods", "display.jar", false, ct: TestContext.CancellationToken);
        Assert.AreEqual(currentHash, disabled.Sha1);
        Assert.IsFalse(File.Exists(source));
        CollectionAssert.AreEqual(Convert.FromHexString(currentHash), SHA1.HashData(await File.ReadAllBytesAsync(Path.Combine(mods, "display.jar.disabled"), TestContext.CancellationToken)));
    });

    private static void WritePackage(string path, string version)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        writer.Write(new JsonObject { ["schemaVersion"] = 1, ["id"] = "display", ["name"] = "Display fixture", ["version"] = version }.ToJsonString());
    }

    /// <summary>
    /// Author review: a real Forge display name containing an apostrophe was cut to "[Let" by the
    /// mixed-quote regex in both inventory and analysis. Existing Fabric display coverage cannot catch
    /// this. Small Forge/NeoForge archives cross both readers, with independent expected names checking
    /// matching delimiters, literal backslashes and escaped quotes/Unicode rather than parser internals.
    /// </summary>
    [TestMethod]
    [DataRow("META-INF/mods.toml")]
    [DataRow("META-INF/neoforge.mods.toml")]
    public async Task TomlNames_PreserveApostrophesQuotesAndLiteralBackslashes(string metadataPath) => await ReportStorage.RunAsync(async paths =>
    {
        var game = Path.Combine(paths.Root, "game");
        var mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        using (var zip = ZipFile.Open(Path.Combine(mods, "quoted.jar"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry(metadataPath).Open()))
        {
            writer.Write("""
                modLoader="javafml"
                [[mods]]
                modId="corn"
                displayName="[Let's Do Addon] Corn \"Expansion\" \u4e16\U0000754c"
                version="1.0"
                [[mods]]
                modId='literal'
                displayName='He said "hello"\path' # literal TOML strings do not decode escapes
                version='2.0'
                """);
        }

        var service = new ContentService(paths);
        var inventory = (await service.ListAsync(game, "mods", includeHashes: false, ct: TestContext.CancellationToken)).Single();
        Assert.AreEqual("[Let's Do Addon] Corn \"Expansion\" 世界", inventory.Metadata?["displayName"]?.GetValue<string>());
        Assert.AreEqual("He said \"hello\"\\path", inventory.Metadata?["mods"]?[1]?["displayName"]?.GetValue<string>());
        var analysis = await service.AnalyzeAsync(game, TestContext.CancellationToken);
        Assert.AreEqual("[Let's Do Addon] Corn \"Expansion\" 世界", analysis.Mods.Single(mod => mod.Id == "corn").Name?.GetValue<string>());
        Assert.AreEqual("He said \"hello\"\\path", analysis.Mods.Single(mod => mod.Id == "literal").Name?.GetValue<string>());
    });

    /// <summary>Gets or sets the runner deadline for scanner and mutation operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}