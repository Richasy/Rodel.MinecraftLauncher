// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects existing-layout links and local settings ownership, independently of copy-import workflows.</summary>
[TestClass]
public sealed class LinkedInstanceTests
{
    /// <summary>
    /// Review: copy-import tests cannot prove zero-copy association. Use real shared/inherited metadata,
    /// HMCL/PCL isolation and a source inventory as an independent oracle. Reopen, edit RMCL settings,
    /// relink and remove registrations, requiring the original saves and source configuration to remain identical.
    /// </summary>
    [TestMethod]
    [DataRow("hmcl")]
    [DataRow("pcl")]
    public async Task Link_AllVersionsRetainsIsolationAndSourceFilesAcrossSettingsRetryAndRemoval(string launcher)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var source = Path.Combine(paths.Root, "external", ".minecraft");
            var child = Path.Combine(source, "versions", "modded");
            Directory.CreateDirectory(child);
            WriteVersion(source, "vanilla", """{"id":"vanilla","mainClass":"net.minecraft.client.main.Main","javaVersion":{"majorVersion":21},"libraries":[]}""");
            WriteVersion(source, "modded", """{"id":"modded","inheritsFrom":"vanilla","unknownExtension":{"retained":true}}""");
            File.WriteAllText(Path.Combine(source, "launcher_profiles.json"), """{"profiles":{"default":{"name":"Shared vanilla","lastVersionId":"vanilla","javaArgs":"-Dprofile=vanilla"}}}""");
            Directory.CreateDirectory(Path.Combine(source, "saves", "shared-world"));
            File.WriteAllText(Path.Combine(source, "saves", "shared-world", "level.dat"), "shared world bytes");
            Directory.CreateDirectory(Path.Combine(child, "saves", "isolated-world"));
            File.WriteAllText(Path.Combine(child, "saves", "isolated-world", "level.dat"), "isolated world bytes");
            if (launcher == "hmcl")
            {
                var config = Path.Combine(child, ".hmcl", "config");
                Directory.CreateDirectory(config);
                File.WriteAllText(Path.Combine(config, "instance-game-settings.json"), new JsonObject
                {
                    ["overrideProperties"] = new JsonArray("runningDirectory", "maxMemory", "autoMemory"),
                    ["runningDirectory"] = child,
                    ["maxMemory"] = 4096,
                    ["autoMemory"] = false,
                    ["futureOption"] = "preserve",
                }.ToJsonString());
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(child, "PCL"));
                File.WriteAllText(Path.Combine(child, "PCL", "Setup.ini"), "VersionArgumentIndieV2=True\nUnknown=preserve");
            }

            var baseline = Inventory(source);
            var store = new InstanceStore(paths);
            Assert.IsNull(store.PreviewLinks(source).Single(preview => preview.VersionId == "modded").ProfileId,
                "The one official default profile must not leak its settings or display name into every other version.");
            var linked = await store.LinkExistingAsync(source, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual(2, linked.Count);
            var records = new InstanceStore(paths).List();
            Assert.IsTrue(records.All(instance => !instance.Managed));
            var vanilla = records.Single(instance => instance.VersionId == "vanilla");
            Assert.AreEqual(source, vanilla.GameDirectory);
            Assert.AreEqual("-Dprofile=vanilla", new SettingsService(paths).GetEffective(vanilla).Values["jvmArguments"]![0]!.ToString(),
                "The matching single profile's custom JVM arguments must be retained, not just memory flags.");
            var modded = records.Single(instance => instance.VersionId == "modded");
            Assert.AreEqual(child, modded.GameDirectory);
            Assert.AreEqual(source, modded.MinecraftDirectory);
            var metadata = await new VersionResolver().ResolveAsync(modded.MinecraftDirectory, modded.VersionId!, TestContext.CancellationToken);
            Assert.AreEqual("net.minecraft.client.main.Main", metadata["mainClass"]!.ToString());
            Assert.IsTrue(metadata["unknownExtension"]!["retained"]!.GetValue<bool>());
            var settings = new SettingsService(paths);
            await settings.SetAsync("maxMemoryMb", JsonValue.Create(3072), modded, TestContext.CancellationToken);
            Assert.AreEqual(3072, settings.GetEffective(store.Get(modded.Id)).Values["maxMemoryMb"]!.GetValue<int>());
            var repeated = await store.LinkExistingAsync(source, cancellationToken: TestContext.CancellationToken);
            CollectionAssert.AreEquivalent(linked.Select(instance => instance.Id).ToArray(), repeated.Select(instance => instance.Id).ToArray());
            Assert.AreEqual(3072, settings.GetEffective(store.Get(modded.Id)).Values["maxMemoryMb"]!.GetValue<int>(), "Relinking cannot overwrite RMCL-local choices.");
            foreach (var instance in linked)
                await store.RemoveAsync(instance.Id, false, TestContext.CancellationToken);
            Assert.AreEqual(0, store.List().Count);
            CollectionAssert.AreEqual(baseline, Inventory(source), "Linking, settings edits and unlinking cannot write into the original game tree.");
        });
    }

    /// <summary>Review: a valid first selection followed by ambiguous isolation must not partially link the batch. Explicit resolution must recover without copying or guessing.</summary>
    [TestMethod]
    public async Task Link_AmbiguousIsolationRejectsWholeBatchUntilExplicitlySelected()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var source = Path.Combine(paths.Root, "external");
            WriteVersion(source, "a-shared", """{"id":"a-shared"}""");
            WriteVersion(source, "b-isolated", """{"id":"b-isolated"}""");
            var game = Path.Combine(source, "versions", "b-isolated");
            Directory.CreateDirectory(Path.Combine(game, "saves"));
            var baseline = Inventory(source);
            var store = new InstanceStore(paths);
            Assert.IsTrue(store.PreviewLinks(source).Single(preview => preview.VersionId == "b-isolated").NeedsGameDirectory);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.LinkExistingAsync(source, cancellationToken: TestContext.CancellationToken));
            Assert.AreEqual(0, store.List().Count, "Batch validation must precede all registrations.");
            var linked = await store.LinkExistingAsync(source, [new("a-shared"), new("b-isolated", game)], TestContext.CancellationToken);
            Assert.AreEqual(2, linked.Count);
            Assert.AreEqual(game, store.List().Single(instance => instance.VersionId == "b-isolated").GameDirectory);
            CollectionAssert.AreEqual(baseline, Inventory(source));
        });
    }

    /// <summary>Review: two official profiles can use one version with different saves. Discovery must preserve both game directories rather than collapsing them to the shared root.</summary>
    [TestMethod]
    public async Task Link_OfficialProfilesWithSameVersionRetainsBothGameDirectories()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var source = Path.Combine(paths.Root, "official");
            WriteVersion(source, "release", """{"id":"release"}""");
            var first = Path.Combine(source, "first");
            var second = Path.Combine(source, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            // An HMCL config left beside this version cannot override an explicitly selected official profile's save directory.
            var hmcl = Path.Combine(source, "versions", "release", ".hmcl", "config");
            Directory.CreateDirectory(hmcl);
            File.WriteAllText(Path.Combine(hmcl, "instance-game-settings.json"), new JsonObject
            {
                ["overrideProperties"] = new JsonArray("runningDirectory"),
                ["runningDirectory"] = source,
            }.ToJsonString());
            File.WriteAllText(Path.Combine(source, "launcher_profiles.json"), new JsonObject
            {
                ["profiles"] = new JsonObject
                {
                    ["first"] = new JsonObject { ["name"] = "First", ["lastVersionId"] = "release", ["gameDir"] = first },
                    ["second"] = new JsonObject { ["name"] = "Second", ["lastVersionId"] = "release", ["gameDir"] = second },
                    ["alternate"] = new JsonObject { ["name"] = "Alternate settings", ["lastVersionId"] = "release", ["gameDir"] = first, ["javaArgs"] = "-Dprofile=alternate" },
                },
            }.ToJsonString());
            var store = new InstanceStore(paths);
            var previews = store.PreviewLinks(source);
            Assert.AreEqual(3, previews.Count);
            Assert.IsTrue(previews.All(preview => preview.ProfileId is not null));
            var linked = await store.LinkExistingAsync(source, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual(3, linked.Count);
            Assert.AreEqual(3, linked.Select(instance => instance.Id).Distinct().Count(), "Different profiles with the same saves must retain their own launch settings.");
            CollectionAssert.AreEquivalent(new[] { first, first, second }, store.List().Select(instance => instance.GameDirectory).ToArray());
            var alternate = store.List().Single(instance => instance.Name == "Alternate settings");
            Assert.AreEqual("-Dprofile=alternate", new SettingsService(paths).GetEffective(alternate).Values["jvmArguments"]![0]!.ToString());
            var repeated = await store.LinkExistingAsync(source, cancellationToken: TestContext.CancellationToken);
            CollectionAssert.AreEquivalent(linked.Select(instance => instance.Id).ToArray(), repeated.Select(instance => instance.Id).ToArray());
        });
    }

    private static void WriteVersion(string root, string id, string json)
    {
        var directory = Path.Combine(root, "versions", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), json);
    }

    private static string[] Inventory(string source) => Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(source, file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
        .Order(StringComparer.Ordinal).ToArray();

    /// <summary>Gets the test cancellation deadline for file and registration operations.</summary>
    public TestContext TestContext { get; set; }
}
