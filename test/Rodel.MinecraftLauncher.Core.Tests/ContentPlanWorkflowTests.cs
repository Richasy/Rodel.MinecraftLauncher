using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects content plans and selection coordination across persistence and real local commits.</summary>
[TestClass]
public sealed class ContentPlanWorkflowTests
{
    /// <summary>
    /// Review: Modrinth's minecraft/iris pack-format tags are not instance mod-loader requirements.
    /// Existing frozen-plan coverage only uses a matching Fabric mod. These real planning calls substitute
    /// provider metadata, retain game-version warnings for every kind and require an actual Forge/Fabric
    /// mismatch to remain advisory. This detects both the false positive and simply removing all checks.
    /// </summary>
    [TestMethod]
    [DataRow("resourcepacks", "minecraft", false)]
    [DataRow("shaderpacks", "iris", false)]
    [DataRow("mods", "forge", true)]
    [DataRow("mods", "fabric", false)]
    public async Task ExactVersion_PackFormatDoesNotRequireInstanceLoader(string kind, string declaredLoader, bool mismatch) => await ReportStorage.RunAsync(async paths =>
    {
        var metadata = new JsonObject
        {
            ["id"] = "frozen",
            ["project_id"] = "root",
            ["game_versions"] = new JsonArray("another-game-version"),
            ["loaders"] = new JsonArray(declaredLoader),
            ["files"] = new JsonArray(new JsonObject
            {
                ["filename"] = kind == "mods" ? "package.jar" : "package.zip",
                ["url"] = "https://content.invalid/never-downloaded"
            })
        };
        using var handler = new ExactVersionHandler(metadata.ToJsonString());
        using var http = new HttpClient(handler);
        var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));

        var plan = await platform.PlanInstallAsync("modrinth", "root", "frozen", Path.Combine(paths.Root, "game"),
            kind: kind, minecraftVersion: "current-game-version", loader: "fabric", ct: TestContext.CancellationToken);

        Assert.IsTrue(plan.CanApply, "Metadata advisories must not become an installation barrier.");
        Assert.AreEqual(kind, plan.Nodes.Single().Kind);
        Assert.AreEqual(mismatch, plan.Warnings.Any(value => value.Reason == "explicit-version-loader-metadata-mismatch"));
        Assert.Contains(value => value.Reason == "explicit-version-game-metadata-mismatch", plan.Warnings);
        Assert.AreEqual(1, handler.RequestCount, "Planning must not download the content.");
    });

    /// <summary>
    /// A persisted exact plan must reuse identical disabled bytes, retain policy/extensions and avoid a new provider lookup.
    /// Existing CLI content tests cover local import reviews, not downloaded-candidate selection or group application.
    /// The rejecting HTTP handler detects accidental download/latest lookup; actual files and the lock independently
    /// detect lost disabled state, metadata, or a substituted candidate after typed review deserialization.
    /// An explicit root override also retains its null consumer and exact-choice warning through the saved review.
    /// </summary>
    [TestMethod]
    public async Task ApplyPersistedPlan_IdenticalDisabledPackage_PreservesStateAndFrozenSource()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var game = Path.Combine(paths.Root, "game");
            var source = Path.Combine(paths.Root, "existing.jar.disabled");
            WritePackage(source);
            var bytes = await File.ReadAllBytesAsync(source, ct);
            var hash = Convert.ToHexStringLower(SHA1.HashData(bytes));
            var metadata = new JsonObject
            {
                ["id"] = "frozen",
                ["project_id"] = "root",
                ["version_number"] = "2.0",
                ["version_type"] = "release",
                ["game_versions"] = new JsonArray("synthetic"),
                ["loaders"] = new JsonArray("fabric"),
                ["dependencies"] = new JsonArray(),
                ["vendor"] = JsonNode.Parse("""{"nullable":null,"future":[1,"kept"]}"""),
                ["files"] = new JsonArray(new JsonObject
                {
                    ["filename"] = "remote.jar",
                    ["primary"] = true,
                    ["url"] = "https://content.invalid/should-not-download.jar",
                    ["size"] = bytes.Length,
                    ["hashes"] = new JsonObject { ["sha1"] = hash, ["sha512"] = Convert.ToHexStringLower(SHA512.HashData(bytes)) }
                })
            };
            var content = new ContentService(paths);
            await content.AddAsync(game, "mods", source, provenance: new JsonObject
            {
                ["platform"] = "modrinth",
                ["projectId"] = "root",
                ["versionId"] = "old",
                ["pinned"] = false,
                ["channel"] = "beta",
                ["futureTracking"] = JsonNode.Parse("""{"nullable":null,"keep":17}""")
            }, ct: ct);
            using var handler = new ExactVersionHandler(metadata.ToJsonString());
            using var http = new HttpClient(handler);
            var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));
            var plan = await platform.PlanInstallAsync("modrinth", "root", "original-choice", game,
                minecraftVersion: "synthetic", loader: "fabric",
                options: new() { VersionSelections = new Dictionary<string, string> { ["root"] = "frozen" } }, ct: ct);
            Assert.IsTrue(plan.CanApply);
            Assert.IsNotNull(plan.Nodes.Single().ReuseLocal);
            Assert.AreEqual("explicit-version-selection", plan.Warnings.Single().Reason);
            var reviewPath = Path.Combine(paths.Root, "frozen-plan.json");
            var frozen = JsonSerializer.SerializeToNode(plan, ContentJsonContext.InstallPlan)!.AsObject();
            Assert.IsTrue(frozen["warnings"]![0]!.AsObject().ContainsKey("requiredBy"));
            Assert.IsNull(frozen["warnings"]![0]!["requiredBy"], "An explicitly selected root retains its null consumer in review evidence.");
            frozen["futureReview"] = new JsonObject { ["nullable"] = null };
            await JsonFiles.WriteAsync(reviewPath, frozen, ct);
            var restored = JsonFiles.ReadSnapshot(reviewPath, stream => JsonSerializer.Deserialize(stream, ContentJsonContext.InstallPlan))!;
            Assert.IsTrue(JsonNode.DeepEquals(frozen, JsonSerializer.SerializeToNode(restored, ContentJsonContext.InstallPlan)),
                "Reading a frozen plan must retain unknown fields and explicit nulls without adding a discriminator.");

            var applied = await platform.ApplyPlanAsync(restored, null, ct);

            Assert.AreEqual(1, handler.RequestCount, "Applying the saved plan must neither resolve latest nor download the identical local file.");
            Assert.AreEqual("frozen", applied.Primary.VersionId);
            Assert.IsFalse(applied.Primary.Enabled);
            Assert.AreEqual("existing.jar.disabled", applied.Primary.FileName);
            Assert.AreEqual("beta", applied.Primary.Channel);
            Assert.IsFalse(applied.Primary.Pinned);
            Assert.IsTrue(JsonNode.DeepEquals(metadata["vendor"], applied.Primary.Upstream?["vendor"]));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(Path.Combine(game, "mods", "existing.jar.disabled"), ct));
            Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "remote.jar")));
            var stored = (await content.GetLockAsync(game, ct))["files"]!.AsArray().Single()!;
            Assert.IsTrue(stored["futureTracking"]!.AsObject().ContainsKey("nullable"));
            Assert.IsNull(stored["futureTracking"]!["nullable"]);
            Assert.AreEqual(17, stored["futureTracking"]!["keep"]!.GetValue<int>());
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source, ct));
        });
    }

    /// <summary>
    /// A malformed game selection must not undo an already committed disable, and recovery must replay only
    /// that operation's saved identity mapping after the user repairs options.txt. Unlike the provider-plan
    /// test, this reaches an actual settings failure and durable recovery boundary, not a serialization-only round trip.
    /// </summary>
    [TestMethod]
    public async Task DisableResourcePack_InvalidSelection_RecoversSavedMappingWithoutChangingOtherChoices()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var game = Path.Combine(paths.Root, "game");
            Directory.CreateDirectory(game);
            var source = Path.Combine(paths.Root, "selected.zip");
            using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
            using (var output = new StreamWriter(archive.CreateEntry("pack.mcmeta").Open()))
                output.Write("""{"pack":{"pack_format":1,"description":"synthetic"}}""");
            var content = new ContentService(paths);
            await content.AddAsync(game, "resourcepacks", source, ct: ct);
            var options = Path.Combine(game, "options.txt");
            const string invalid = "resourcePacks:broken\nincompatibleResourcePacks:[]\ncustomOption:retained\n";
            await File.WriteAllTextAsync(options, invalid, ct);

            var disabled = await content.SetEnabledAsync(game, "resourcepacks", "selected.zip", false, ct: ct);

            Assert.IsFalse(disabled.Enabled);
            Assert.AreEqual("pending-recovery", disabled.ResourcePackSelection?.State);
            Assert.IsNotNull(disabled.Warning);
            Assert.IsTrue(File.Exists(Path.Combine(game, "resourcepacks", "selected.zip.disabled")));
            Assert.AreEqual(invalid, await File.ReadAllTextAsync(options, ct), "Failed selection parsing must not rewrite the player's options.");
            await File.WriteAllTextAsync(options,
                "resourcePacks:[\"vanilla\",\"file/selected.zip\",\"custom:kept\"]\nincompatibleResourcePacks:[]\ncustomOption:retained\n", ct);

            await content.RecoverAsync(game, ct);

            var settings = await new GameSettingsService(paths).GetAsync(game, ct: ct);
            CollectionAssert.AreEqual(expected,
                JsonNode.Parse(settings.Values["resourcePacks"]!.GetValue<string>())!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
            Assert.AreEqual("retained", settings.Values["customOption"]!.GetValue<string>());
            Assert.IsTrue(File.Exists(Path.Combine(game, "resourcepacks", "selected.zip.disabled")));
            Assert.IsFalse(File.Exists(Path.Combine(game, "resourcepacks", "selected.zip")));
        });
    }

    private static void WritePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var output = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open());
        output.Write("""{"schemaVersion":1,"id":"fixture","version":"1","future":{"nullable":null}}""");
    }

    private sealed class ExactVersionHandler(string metadata) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (RequestCount != 1 || request.RequestUri?.AbsolutePath != "/version/frozen")
                throw new InvalidOperationException("The frozen workflow attempted an unexpected provider or download request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(metadata, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Gets or sets cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["vanilla", "custom:kept"];
}