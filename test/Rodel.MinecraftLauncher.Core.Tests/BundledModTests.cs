using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Exercises shipped mod bytes, identity-based replacement and frozen review protection through real isolated Core storage.</summary>
[TestClass]
[DoNotParallelize]
public sealed class BundledModTests
{
    /// <summary>Review: a manually switched launch profile can leave a stale configured loader. Reading real inherited metadata must prevent choosing Fabric bytes for Forge while preserving the stored preference and explicit content filters.</summary>
    [TestMethod]
    public Task RuntimeMetadata_OverridesStaleLoaderPreference_WithoutRewritingSettings()
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("manual-runtime", "custom-profile", ct);
            await client.Settings.SetAsync("loader", JsonValue.Create("fabric"), instance, ct);
            var versions = Path.Combine(instance.MinecraftDirectory, "versions");
            Directory.CreateDirectory(Path.Combine(versions, "custom-profile"));
            Directory.CreateDirectory(Path.Combine(versions, "26.3"));
            await File.WriteAllTextAsync(Path.Combine(versions, "custom-profile", "custom-profile.json"), """
                {"id":"custom-profile","inheritsFrom":"26.3","libraries":[{"name":"net.minecraftforge:fmlloader:26.3-66.1.0"}]}
                """, ct);
            await File.WriteAllTextAsync(Path.Combine(versions, "26.3", "26.3.json"), """{"id":"26.3"}""", ct);

            var context = await client.GetContentContextAsync(instance.Id, ct: ct);
            Assert.AreEqual("26.3", context.MinecraftVersion);
            Assert.AreEqual("forge", context.Loader);
            Assert.AreEqual("version-metadata", context.LoaderSource);
            Assert.AreEqual("fabric", client.Settings.GetEffective(instance).Values["loader"]!.GetValue<string>());
            var explicitFilter = await client.GetContentContextAsync(instance.Id, loader: "quilt", ct: ct);
            Assert.AreEqual("quilt", explicitFilter.Loader, "An explicit search filter is a separate caller choice.");
        });

    /// <summary>
    /// Review: ordinary file import tests cannot prove shipped assets or dependency closure. Selecting only AI or BetterTools must install
    /// exactly its loader-specific dependency closure offline, retain a player's existing UI, persist builtin provenance and avoid duplicates on retry.
    /// Explicit expected identities distinguish Fabric/Quilt's API and standalone native Forge/NeoForge AI. AI and BetterTools must never pull in the withdrawn UI.
    /// This uses actual packaged JARs, without substituting an HTTP provider or claiming Minecraft gameplay validation.
    /// Warm browsing must not reopen immutable launcher JARs, but installation state must follow real disable/enable receipts.
    /// An exclusive fixture handle distinguishes cached browsing from review, which must still read the actual source.
    /// </summary>
    [TestMethod]
    [DataRow("26.3", "fabric", "rodel_ai,fabric-api", "rodel_ai")]
    [DataRow("26.2", "fabric", "rodel_ai,fabric-api", "rodel_ai")]
    [DataRow("26.1.2", "fabric", "rodel_ai,fabric-api", "rodel_ai")]
    [DataRow("26.3", "quilt", "rodel_ai,fabric-api", "rodel_ai")]
    [DataRow("26.3", "forge", "rodel_ai", "rodel_ai")]
    [DataRow("26.3", "neoforge", "rodel_ai", "rodel_ai")]
    [DataRow("1.20.1", "forge", "rodel_ai", "rodel_ai")]
    [DataRow("26.3", "fabric", "bettertools,fabric-api", "bettertools")]
    [DataRow("26.2", "fabric", "bettertools,fabric-api", "bettertools")]
    [DataRow("26.1.2", "fabric", "bettertools,fabric-api", "bettertools")]
    [DataRow("1.21.11", "fabric", "bettertools,fabric-api", "bettertools")]
    [DataRow("26.3", "quilt", "bettertools,fabric-api", "bettertools")]
    [DataRow("26.3", "forge", "bettertools", "bettertools")]
    [DataRow("26.3", "neoforge", "bettertools", "bettertools")]
    public Task Selection_InstallsBundledDependencyClosure_AndReusesItsIdentity(string minecraftVersion, string loader, string expectedIds, string selectedId)
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("bundled-ai", minecraftVersion, ct);
            await client.Settings.SetAsync("loader", JsonValue.Create(loader), instance, ct);
            var expected = expectedIds.Split(',');
            var mods = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(mods);
            var unrelated = Path.Combine(mods, "manually-installed-ui.jar");
            WriteMod(unrelated, "rodel_game_ui", "0.0.1");
            var original = await File.ReadAllBytesAsync(unrelated, ct);
            var review = await client.ReviewBundledModsAsync(instance.Id, [selectedId], ct);
            CollectionAssert.AreEquivalent(expected, review.Plan!.Nodes.Select(node => node.ProjectId).ToArray());
            Assert.HasCount(1, Directory.GetFiles(mods), "Planning must not install files.");
            var result = (ContentInstallReceipt)await client.ApplyContentReviewAsync(instance.Id, review.Id, ct);
            Assert.HasCount(expected.Length, result.Installed);
            Assert.IsTrue(result.Installed.All(entry => entry.Platform == "builtin" && entry.Enabled));
            Assert.IsTrue(review.Plan.Nodes.All(node => node.GameVersion == minecraftVersion));
            var catalog = await client.GetBundledModsAsync(instance.Id, ct);
            await Assert.ThrowsAsync<ArgumentException>(() => client.ReviewBundledModsAsync(instance.Id, ["rodel_game_ui"], ct));
            Assert.IsTrue(catalog.Mods.Single(mod => mod.Id == selectedId).Installed);
            Assert.DoesNotContain(mod => mod.Id == "rodel_game_ui", catalog.Mods, "The withdrawn UI must not be offered or pulled in by another mod.");
            Assert.IsTrue(catalog.Mods.All(mod => mod.MinecraftVersion == minecraftVersion && mod.Loader == loader && mod.Version == "0.0.1"));
            Assert.DoesNotContain(mod => mod.Id == "worldreroll" && mod.Installed, catalog.Mods);
            var selected = result.Installed.Single(entry => (entry.Metadata?["id"] ?? entry.Metadata?["modId"])?.ToString() == selectedId);
            var disabled = (ContentEntry)await client.SetContentEnabledAsync(instance.Id, [selected.FileName], false, cancellationToken: ct);
            using (var sourceLock = new FileStream(Path.Combine(AppContext.BaseDirectory, "Assets", "BuiltInMods", minecraftVersion, loader, selectedId == "rodel_ai" ? "rodel-ai.jar" : "bettertools.jar"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var warm = await new LauncherClient(paths).GetBundledModsAsync(instance.Id, ct);
                Assert.IsFalse(warm.Mods.Single(mod => mod.Id == selectedId).Installed, "A cached catalog must not retain stale enabled state.");
                Assert.AreEqual("0.0.1", warm.Mods.Single(mod => mod.Id == selectedId).InstalledVersion);
                await Assert.ThrowsAsync<IOException>(() => client.ReviewBundledModsAsync(instance.Id, [selectedId], ct));
            }

            await client.SetContentEnabledAsync(instance.Id, [disabled.FileName], true, cancellationToken: ct);
            Assert.IsTrue((await client.GetBundledModsAsync(instance.Id, ct)).Mods.Single(mod => mod.Id == selectedId).Installed);
            var repeated = await client.ReviewBundledModsAsync(instance.Id, [selectedId], ct);
            await client.ApplyContentReviewAsync(instance.Id, repeated.Id, ct);
            Assert.HasCount(expected.Length + 1, Directory.GetFiles(mods), "A repeated install must not create suffixed duplicates.");
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(unrelated, ct));
        });

    /// <summary>
    /// Review: this differs from tracked online updates because a private mod can be untracked, renamed and disabled.
    /// Native Forge/NeoForge use modId and can retain the same release version with different bytes; the old file must still be replaced.
    /// The actual metadata ID must select that file for replacement; a different mod occupying the desired filename survives.
    /// </summary>
    [TestMethod]
    [DataRow("fabric", "0.0.0")]
    [DataRow("forge", "0.0.1")]
    [DataRow("neoforge", "0.0.1")]
    public Task Install_ReplacesRenamedDisabledMod_AndPreservesFilenameCollision(string loader, string oldVersion)
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("bundled-upgrade", "26.3", ct);
            await client.Settings.SetAsync("loader", JsonValue.Create(loader), instance, ct);
            var review = await client.ReviewBundledModsAsync(instance.Id, ["worldreroll"], ct);
            var mods = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(mods);
            var old = Path.Combine(mods, "renamed-old.jar.disabled");
            WriteMod(old, "worldreroll", oldVersion, loader: loader);
            var collision = Path.Combine(mods, review.Plan!.Nodes.Single(node => node.ProjectId == "worldreroll").FileName);
            WriteMod(collision, "unrelated", "1.0", loader: loader);
            var original = await File.ReadAllBytesAsync(collision, ct);
            review = await client.ReviewBundledModsAsync(instance.Id, ["worldreroll"], ct);
            await client.ApplyContentReviewAsync(instance.Id, review.Id, ct);
            Assert.IsFalse(File.Exists(old));
            var files = await client.ListContentAsync(instance.Id, cancellationToken: ct);
            var installed = files.Where(entry => entry.Metadata is JsonObject metadata && (metadata["id"] ?? metadata["modId"])?.ToString() == "worldreroll").ToArray();
            Assert.HasCount(1, installed);
            Assert.IsTrue(installed[0].Enabled);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(collision, ct));
            Assert.IsTrue((await client.GetBundledModsAsync(instance.Id, ct)).Mods.Single(mod => mod.Id == "worldreroll").Installed);
        });

    /// <summary>
    /// Review: the picker is not the trust boundary. Runtime mismatch and a real file added after review must both prevent
    /// application through Core. Separate isolated instances distinguish compatibility rejection from inventory drift.
    /// </summary>
    [TestMethod]
    public Task Review_RejectsIncompatibleRuntime_AndApplyRejectsChangedInventory()
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var incompatible = await client.Instances.CreateAsync("wrong-version", "1.21", ct);
            var mismatch = await Assert.ThrowsAsync<LauncherOperationException>(() => client.ReviewBundledModsAsync(incompatible.Id, ["worldreroll"], ct));
            Assert.AreEqual("BUNDLED_MOD_INCOMPATIBLE", mismatch.Code);
            var instance = await client.Instances.CreateAsync("drift", "26.3", ct);
            await client.Settings.SetAsync("loader", JsonValue.Create("fabric"), instance, ct);
            var review = await client.ReviewBundledModsAsync(instance.Id, ["worldreroll"], ct);
            var mods = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(mods);
            var external = Path.Combine(mods, "external.jar");
            WriteMod(external, "external", "1.0");
            var original = await File.ReadAllBytesAsync(external, ct);
            var failure = await Assert.ThrowsAsync<LauncherOperationException>(() => client.ApplyContentReviewAsync(instance.Id, review.Id, ct));
            Assert.AreEqual("CONTENT_REVIEW_STALE", failure.Code);
            Assert.HasCount(1, Directory.GetFiles(mods));
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(external, ct));

            // A loader switch can keep every content file unchanged. Inventory drift alone cannot protect that review.
            var switched = await client.Instances.CreateAsync("loader-switch", "26.3", ct);
            await client.Settings.SetAsync("loader", JsonValue.Create("fabric"), switched, ct);
            var frozen = await client.ReviewBundledModsAsync(switched.Id, ["worldreroll"], ct);
            await client.Settings.SetAsync("loader", JsonValue.Create("forge"), switched, ct);
            var changed = await Assert.ThrowsAsync<LauncherOperationException>(() => client.ApplyContentReviewAsync(switched.Id, frozen.Id, ct));
            Assert.AreEqual("CONTENT_REVIEW_STALE", changed.Code);
            Assert.IsFalse(Directory.Exists(Path.Combine(switched.GameDirectory, "mods")) && Directory.EnumerateFiles(Path.Combine(switched.GameDirectory, "mods")).Any());
        });

    private static void WriteMod(string path, string id, string version, string? minecraft = null, string loader = "fabric")
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (loader is "forge" or "neoforge")
        {
            using var metadata = new StreamWriter(archive.CreateEntry(loader == "forge" ? "META-INF/mods.toml" : "META-INF/neoforge.mods.toml").Open());
            metadata.Write($"""
                modLoader="javafml"
                loaderVersion="[1,)"
                license="MIT"
                [[mods]]
                modId="{id}"
                displayName="{id}"
                version="{version}"
                """);
            return;
        }

        using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open());
        writer.Write(new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["name"] = id,
            ["version"] = version,
            ["depends"] = minecraft is null ? null : new JsonObject { ["minecraft"] = minecraft }
        }.ToJsonString());
    }

    /// <summary>
    /// Review: dependency closure must retain a player's compatible Fabric API. Extend this workflow instead of adding
    /// a parser-only duplicate: the real API's ~26.3- predicate was downgraded despite the comparator fixture passing.
    /// Exact planned identities and unchanged bytes distinguish retention from replacement; other minor versions and
    /// an unmet patch lower bound must still replace the API, rejecting a prefix-only interpretation of '~'.
    /// </summary>
    [TestMethod]
    [DataRow(">=26.3- <26.4-", true)]
    [DataRow(">=26.2- <26.3-", false)]
    [DataRow("~26.3-", true)]
    [DataRow("~26.3", true)]
    [DataRow("~26.2-", false)]
    [DataRow("~26.4-", false)]
    [DataRow("~26.3.1-", false)]
    public Task Install_RetainsSharedDependencyOnlyForItsDeclaredGame(string minecraftRange, bool retain)
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("shared-dependency", "26.3", ct);
            await client.Settings.SetAsync("loader", JsonValue.Create("fabric"), instance, ct);
            var mods = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(mods);
            var api = Path.Combine(mods, "fabric-api-newer.jar");
            WriteMod(api, "fabric-api", "0.165.0+26.3", minecraftRange);
            var original = await File.ReadAllBytesAsync(api, ct);
            var review = await client.ReviewBundledModsAsync(instance.Id, ["worldreroll"], ct);
            string[] expected = retain ? ["worldreroll"] : ["fabric-api", "worldreroll"];
            CollectionAssert.AreEquivalent(expected, review.Plan!.Nodes.Select(node => node.ProjectId).ToArray());
            await client.ApplyContentReviewAsync(instance.Id, review.Id, ct);
            Assert.HasCount(2, Directory.GetFiles(mods));
            if (retain) CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(api, ct));
            else Assert.IsFalse(File.Exists(api), "A newer API for another game must not survive as a duplicate.");
        });

    /// <summary>Gets or sets cooperative cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; }

}