using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Crosses actual CLI review, archive metadata and recoverable file transactions for renamed local mods.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class LocalModIdentityTests
{
    /// <summary>
    /// A renamed same-version package must require confirmation, replace its native-ID match, retain recoverable
    /// original bytes and player policy, and reuse an identical renamed copy. Filename-only checks permit two mods;
    /// the independent archive bytes, physical file count and old source claims distinguish the regression.
    /// The disabled Fabric variant protects saved enablement through both replacement and renamed reuse.
    /// Existing name-conflict tests use metadata-free ZIPs and cannot detect these failures.
    /// </summary>
    [TestMethod]
    [DataRow("fabric", true)]
    [DataRow("fabric", false)]
    [DataRow("quilt", true)]
    [DataRow("forge", true)]
    [DataRow("neoforge", true)]
    public async Task RenamedMod_RequiresReviewAndReplacesSameIdRecoverably(string format, bool enabled)
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var old = Path.Combine(fixture.Mods, "launcher-owned-0.0.1.jar" + (enabled ? string.Empty : ".disabled"));
        var installedName = "selected.jar" + (enabled ? string.Empty : ".disabled");
        WriteMod(old, format, "same_mod", "original bytes");
        WriteMod(fixture.Source, format, "same_mod", "fixed bytes, same display version");
        var oldBytes = await File.ReadAllBytesAsync(old, TestContext.CancellationToken);
        var sourceBytes = await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken);
        var lockPath = Path.Combine(Path.GetDirectoryName(fixture.Mods)!, ".rodel", "content-lock.json");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var tracking = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["files"] = new JsonArray(new JsonObject
            {
                ["path"] = "mods/" + Path.GetFileName(old),
                ["fileName"] = Path.GetFileName(old),
                ["kind"] = "mods",
                ["enabled"] = enabled,
                ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(oldBytes)),
                ["size"] = oldBytes.Length,
                ["platform"] = "builtin",
                ["projectId"] = "same_mod",
                ["versionId"] = "0.0.1",
                ["pinned"] = true,
                ["channel"] = "beta",
                ["futurePolicy"] = new JsonArray(17, null),
            }),
        };
        await File.WriteAllTextAsync(lockPath, tracking.ToJsonString(), TestContext.CancellationToken);

        var refused = (await cli.RunAsync("content", "add", fixture.InstanceId, fixture.Source)).ReadEnvelope(1);
        Assert.AreEqual("CONTENT_MOD_ID_CONFLICT", refused["error"]?["code"]?.GetValue<string>());
        var review = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(review["canApply"]?.GetValue<bool>());
        Assert.AreEqual("replace", review["items"]?[0]?["action"]?.GetValue<string>());
        Assert.AreEqual(Path.GetFileName(old), review["items"]?[0]?["replacements"]?[0]?["fileName"]?.GetValue<string>());
        CollectionAssert.AreEqual(oldBytes, await File.ReadAllBytesAsync(old, TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Mods, "selected.jar")), "Review must leave the old package installed until confirmation.");

        var result = (await cli.RunAsync("content", "apply-review", fixture.InstanceId, review["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(0)["data"]!;
        Assert.HasCount(1, Directory.GetFiles(fixture.Mods, "*.jar*"));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Mods, installedName), TestContext.CancellationToken));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken));
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(result["items"]![0]!["data"]!["journal"]!.GetValue<string>(), TestContext.CancellationToken))!;
        var backup = journal["changes"]!.AsArray().Single(change => change!["target"]!.GetValue<string>() == old)!["backup"]!.GetValue<string>();
        CollectionAssert.AreEqual(oldBytes, await File.ReadAllBytesAsync(backup, TestContext.CancellationToken));
        var retained = JsonNode.Parse(await File.ReadAllTextAsync(lockPath, TestContext.CancellationToken))!["files"]![0]!;
        Assert.IsNull(retained["platform"], "A local replacement must not claim to be the old bundled/provider release.");
        Assert.IsTrue(retained["pinned"]!.GetValue<bool>());
        Assert.AreEqual(enabled, retained["enabled"]!.GetValue<bool>());
        Assert.AreEqual("beta", retained["channel"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(tracking["files"]![0]!["futurePolicy"], retained["futurePolicy"]));

        var renamed = Path.Combine(Path.GetDirectoryName(fixture.Source)!, "renamed-again.jar");
        File.Copy(fixture.Source, renamed);
        var reused = (await cli.RunAsync("content", "add", fixture.InstanceId, renamed)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(reused["reused"]?.GetValue<bool>());
        Assert.AreEqual(installedName, reused["fileName"]?.GetValue<string>());
        Assert.AreEqual(enabled, reused["enabled"]?.GetValue<bool>());
        Assert.HasCount(1, Directory.GetFiles(fixture.Mods, "*.jar*"));
    }

    /// <summary>Two selected archives with one native identity must reject the whole review before either is copied.</summary>
    [TestMethod]
    public async Task ReviewFiles_DuplicateIdsInSelection_RejectsBeforeWriting()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var second = Path.Combine(Path.GetDirectoryName(fixture.Source)!, "other-name.jar");
        WriteMod(fixture.Source, "fabric", "same_mod", "first");
        WriteMod(second, "fabric", "same_mod", "second");
        var review = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source, second)).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(review["canApply"]?.GetValue<bool>());
        Assert.IsTrue(review["items"]!.AsArray().All(item => item!["issue"]?.GetValue<string>() == "duplicate-mod-id"));
        var failure = (await cli.RunAsync("content", "apply-review", fixture.InstanceId, review["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(1);
        Assert.AreEqual("CONTENT_REVIEW_UNRESOLVED", failure["error"]?["code"]?.GetValue<string>());
        Assert.IsEmpty(Directory.GetFiles(fixture.Mods, "*.jar"));
        Assert.AreEqual("unselected", await File.ReadAllTextAsync(Path.Combine(fixture.Mods, "notes.txt"), TestContext.CancellationToken));
    }

    /// <summary>A secondary Forge declaration also owns its ID; replacing it alone must not discard its companion mod.</summary>
    [TestMethod]
    public async Task ReviewFiles_SecondaryIdOfMultiModJar_RequiresCompleteReplacement()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var old = Path.Combine(fixture.Mods, "several-mods.jar");
        WriteMod(old, "forge", "companion", "original", "same_mod");
        WriteMod(fixture.Source, "forge", "same_mod", "partial replacement");
        var oldBytes = await File.ReadAllBytesAsync(old, TestContext.CancellationToken);
        var refused = (await cli.RunAsync("content", "add", fixture.InstanceId, fixture.Source)).ReadEnvelope(1);
        Assert.AreEqual("CONTENT_MOD_ID_CONFLICT", refused["error"]?["code"]?.GetValue<string>());
        var review = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source)).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(review["canApply"]?.GetValue<bool>());
        Assert.AreEqual("partial-mod-id-replacement", review["items"]?[0]?["issue"]?.GetValue<string>());
        CollectionAssert.AreEqual(oldBytes, await File.ReadAllBytesAsync(old, TestContext.CancellationToken));
    }

    /// <summary>Manual files can bypass import; enabling a second copy must preserve both files until the active conflict is disabled.</summary>
    [TestMethod]
    public async Task Enable_ModIdAlreadyEnabled_RejectsWithoutMovingEitherPackage()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var active = Path.Combine(fixture.Mods, "old-name.jar");
        var disabled = Path.Combine(fixture.Mods, "new-name.jar.disabled");
        WriteMod(active, "fabric", "same_mod", "active");
        WriteMod(disabled, "fabric", "same_mod", "disabled");
        var bytes = await File.ReadAllBytesAsync(disabled, TestContext.CancellationToken);
        var failure = (await cli.RunAsync("content", "enable", fixture.InstanceId, Path.GetFileName(disabled))).ReadEnvelope(1);
        Assert.AreEqual("CONTENT_MOD_ID_CONFLICT", failure["error"]?["code"]?.GetValue<string>());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(disabled, TestContext.CancellationToken));
        Assert.IsTrue(File.Exists(active));
        (await cli.RunAsync("content", "disable", fixture.InstanceId, Path.GetFileName(active))).ReadEnvelope(0);
        (await cli.RunAsync("content", "enable", fixture.InstanceId, Path.GetFileName(disabled))).ReadEnvelope(0);
        Assert.HasCount(1, Directory.GetFiles(fixture.Mods, "*.jar"));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(disabled[..^9], TestContext.CancellationToken));
    }

    private static void WriteMod(string path, string format, string id, string marker, string? companion = null)
    {
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        var metadataName = format switch { "fabric" => "fabric.mod.json", "quilt" => "quilt.mod.json", "forge" => "META-INF/mods.toml", _ => "META-INF/neoforge.mods.toml" };
        using (var writer = new StreamWriter(archive.CreateEntry(metadataName).Open()))
        {
            if (format == "fabric")
            {
                writer.Write(new JsonObject { ["schemaVersion"] = 1, ["id"] = id, ["version"] = "0.0.1" }.ToJsonString());
            }
            else if (format == "quilt")
            {
                writer.Write(new JsonObject { ["quilt_loader"] = new JsonObject { ["id"] = id, ["version"] = "0.0.1" } }.ToJsonString());
            }
            else
            {
                writer.Write($"modLoader=\"javafml\"\n[[mods]]\nmodId=\"{id}\"\nversion=\"0.0.1\"\n");
                if (companion is not null) writer.Write($"[[mods]]\nmodId=\"{companion}\"\nversion=\"0.0.1\"\n");
            }
        }

        using var contents = new StreamWriter(archive.CreateEntry("marker.txt").Open());
        contents.Write(marker);
    }

    /// <summary>Gets or sets the MSTest context supplying cancellation for real CLI and file operations.</summary>
    public TestContext TestContext { get; set; }
}