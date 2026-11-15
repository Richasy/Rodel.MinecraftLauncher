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

/// <summary>Protects local-file source discovery, explicit candidate selection and confirmed replacement across real storage; only the provider HTTP peer is substituted.</summary>
[TestClass]
public sealed class LocalContentUpdateTests
{
    /// <summary>
    /// Review: ordinary plan tests start with tracked sources. A renamed untracked file must be discovered,
    /// with search candidates remaining unselected when its hash is unknown. The frozen plan must replace
    /// exactly that file, use its beta channel policy, retain its disabled state and recoverable bytes, and persist the chosen source.
    /// Read-only checks and an HTTP peer refusing fresh metadata during application detect early writes and re-resolution.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LocalFile_CheckSelectAndApply_PreservesStateAndOriginal(bool hashMatches) => await ReportStorage.RunAsync(async paths =>
    {
        var ct = TestContext.CancellationToken;
        using var provider = new LocalUpdateProvider { HashMatches = hashMatches };
        using var http = new HttpClient(provider);
        var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));
        var content = new ContentService(paths);
        var game = await PrepareAsync(paths, content, provider, ct);
        var before = (await content.GetLockAsync(game, ct)).ToJsonString();
        var check = await platform.PlanUpdatesAsync(game, "mods", [FileName], options: Options(), ct: ct);
        Assert.AreEqual(before, (await content.GetLockAsync(game, ct)).ToJsonString(), "Discovery cannot attach a source or mutate the update baseline.");
        if (!hashMatches)
        {
            Assert.AreEqual("source-selection-required", check.Items.Single().Reason);
            Assert.IsNull(check.Plan);
            Assert.HasCount(2, check.Items.Single().SourceCandidates, "Search suggestions must not auto-select a similarly named project.");
            Assert.Contains("native_id", provider.Queries);
            Assert.Contains("Native Display Name", provider.Queries);
            check = await platform.PlanUpdatesAsync(game, "mods", [FileName], options: Options("site-project"), ct: ct);
        }

        Assert.AreEqual("available", check.Items.Single().Status);
        Assert.AreEqual("site-project", check.Items.Single().ProjectId, "The native ID is not a platform project ID.");
        Assert.IsTrue(check.CanApply);
        CollectionAssert.AreEqual(provider.OldBytes, await File.ReadAllBytesAsync(Path.Combine(game, "mods", FileName), ct));
        Assert.AreEqual(before, (await content.GetLockAsync(game, ct)).ToJsonString());
        var frozen = JsonSerializer.Deserialize(JsonSerializer.Serialize(check.Plan!, ContentJsonContext.InstallPlan), ContentJsonContext.InstallPlan)!;
        Assert.AreEqual(FileName, frozen.Nodes.Single().LocalReplacement?.FileName);
        provider.RejectMetadata = true;

        var result = await platform.ApplyPlanAsync(frozen, null, ct);

        var installed = (await content.ListAsync(game, "mods", ct: ct)).Single();
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", FileName)));
        Assert.IsFalse(installed.Enabled);
        Assert.AreEqual("beta", installed.Tracking?["channel"]?.ToString());
        Assert.AreEqual("site-project", installed.Tracking?["projectId"]?.ToString());
        Assert.AreEqual("v2", installed.Tracking?["versionId"]?.ToString());
        CollectionAssert.AreEqual(provider.NewBytes, await File.ReadAllBytesAsync(Path.Combine(game, installed.Path), ct));
        CollectionAssert.AreEqual(provider.OldBytes, await File.ReadAllBytesAsync(Path.Combine(paths.Root, "source.jar"), ct));
        var journal = JsonFiles.ReadObject(result.Journal);
        var old = journal["changes"]!.AsArray().Single(change => change!["target"]!.ToString() == Path.Combine(game, "mods", FileName))!;
        CollectionAssert.AreEqual(provider.OldBytes, await File.ReadAllBytesAsync(old["backup"]!.ToString(), ct));
    });

    /// <summary>
    /// Review: a search match is not identity evidence. An explicitly selected same-title project with different
    /// archive IDs must fail after the actual download/ZIP inspection while leaving local bytes and provenance intact.
    /// This boundary differs from hash matching and prevents trusting platform titles/slugs as mod declarations.
    /// </summary>
    [TestMethod]
    public async Task SimilarTitle_WrongArchiveIds_RejectsReplacement() => await ReportStorage.RunAsync(async paths =>
    {
        var ct = TestContext.CancellationToken;
        using var provider = new LocalUpdateProvider();
        using var http = new HttpClient(provider);
        var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));
        var content = new ContentService(paths);
        var game = await PrepareAsync(paths, content, provider, ct);
        var before = (await content.GetLockAsync(game, ct)).ToJsonString();

        var check = await platform.PlanUpdatesAsync(game, "mods", [FileName], options: Options("counterfeit"), ct: ct);

        Assert.AreEqual("failed", check.Items.Single().Status);
        Assert.IsFalse(check.CanApply);
        Assert.IsTrue(provider.DownloadedCounterfeit, "The case must reach actual candidate bytes, not reject before inspection.");
        Assert.AreEqual(before, (await content.GetLockAsync(game, ct)).ToJsonString());
        CollectionAssert.AreEqual(provider.OldBytes, await File.ReadAllBytesAsync(Path.Combine(game, "mods", FileName), ct));
    });

    /// <summary>Review: an untracked file can change without changing the tracking lock. The real commit must reject those changed bytes even after a valid frozen source lookup.</summary>
    [TestMethod]
    public async Task FrozenLocalReplacement_FileChanged_PreservesInterveningBytes() => await ReportStorage.RunAsync(async paths =>
    {
        var ct = TestContext.CancellationToken;
        using var provider = new LocalUpdateProvider { HashMatches = true };
        using var http = new HttpClient(provider);
        var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));
        var content = new ContentService(paths);
        var game = await PrepareAsync(paths, content, provider, ct);
        var check = await platform.PlanUpdatesAsync(game, "mods", [FileName], options: Options(), ct: ct);
        var changed = LocalUpdateProvider.Package("native_id", "edited");
        var file = Path.Combine(game, "mods", FileName);
        await File.WriteAllBytesAsync(file, changed, ct);

        await Assert.ThrowsAsync<IOException>(() => platform.ApplyPlanAsync(check.Plan!, null, ct));

        CollectionAssert.AreEqual(changed, await File.ReadAllBytesAsync(file, ct));
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "v2.jar.disabled")));
        Assert.IsNull((await content.ListAsync(game, "mods", ct: ct)).Single().Tracking?["platform"]);
    });

    /// <summary>Review: an unavailable optional source lookup must report a retryable per-file failure without poisoning local storage; a later successful check must use the same unchanged file.</summary>
    [TestMethod]
    public async Task SourceLookupFailure_RetryRecoversWithoutLocalMutation() => await ReportStorage.RunAsync(async paths =>
    {
        var ct = TestContext.CancellationToken;
        using var provider = new LocalUpdateProvider { HashMatches = true, FailHash = true };
        using var http = new HttpClient(provider);
        var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));
        var content = new ContentService(paths);
        var game = await PrepareAsync(paths, content, provider, ct);
        var before = (await content.GetLockAsync(game, ct)).ToJsonString();

        var failed = await platform.PlanUpdatesAsync(game, "mods", [FileName], options: Options(), ct: ct);

        Assert.AreEqual("failed", failed.Items.Single().Status);
        Assert.IsNotNull(failed.Items.Single().Failure);
        Assert.IsFalse(failed.CanApply);
        Assert.AreEqual(before, (await content.GetLockAsync(game, ct)).ToJsonString());
        CollectionAssert.AreEqual(provider.OldBytes, await File.ReadAllBytesAsync(Path.Combine(game, "mods", FileName), ct));
        provider.FailHash = false;
        var retry = await platform.PlanUpdatesAsync(game, "mods", [FileName], options: Options(), ct: ct);
        Assert.AreEqual("available", retry.Items.Single().Status);
        Assert.IsTrue(retry.CanApply);
        Assert.AreEqual(before, (await content.GetLockAsync(game, ct)).ToJsonString());
    });

    private static ContentInstallOptions Options(string? source = null) => new()
    {
        MinecraftVersion = "26.3",
        Loader = "fabric",
        UseCurrentContext = true,
        SourceSelections = source is null ? null : new Dictionary<string, string> { [FileName] = source }
    };

    private static async Task<string> PrepareAsync(LauncherPaths paths, ContentService content, LocalUpdateProvider provider, CancellationToken ct)
    {
        var game = Path.Combine(paths.Root, "game");
        var source = Path.Combine(paths.Root, "source.jar");
        await File.WriteAllBytesAsync(source, provider.OldBytes, ct);
        await content.AddAsync(game, "mods", source, ct: ct);
        File.Move(Path.Combine(game, "mods", "source.jar"), Path.Combine(game, "mods", FileName));
        await content.SetPolicyAsync(game, "mods", FileName, pinned: false, channel: "beta", ct: ct);
        return game;
    }

    private const string FileName = "renamed.jar.disabled";

    /// <summary>Gets or sets cancellation supplied by MSTest.</summary>
    public TestContext TestContext { get; set; }

    private sealed class LocalUpdateProvider : HttpMessageHandler
    {
        internal byte[] OldBytes { get; } = Package("native_id", "1");
        internal byte[] NewBytes { get; } = Package("native_id", "2");
        private readonly byte[] _counterfeit = Package("unrelated_id", "2");
        internal bool HashMatches { get; init; }
        internal bool FailHash { get; set; }
        internal bool RejectMetadata { get; set; }
        internal bool DownloadedCounterfeit { get; private set; }
        internal List<string> Queries { get; } = [];

        internal static byte[] Package(string id, string version)
        {
            using var bytes = new MemoryStream();
            using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open()))
                writer.Write(new JsonObject { ["schemaVersion"] = 1, ["id"] = id, ["name"] = "Native Display Name", ["version"] = version }.ToJsonString());
            return bytes.ToArray();
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/files/", StringComparison.Ordinal))
            {
                DownloadedCounterfeit |= path == "/files/wrong.jar";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(path == "/files/wrong.jar" ? _counterfeit : NewBytes) });
            }

            if (RejectMetadata) throw new InvalidOperationException("Applying a frozen plan requested fresh metadata.");
            JsonNode result;
            if (path.StartsWith("/version_file/", StringComparison.Ordinal))
            {
                if (FailHash) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                Assert.AreEqual("/version_file/" + Convert.ToHexStringLower(SHA1.HashData(OldBytes)), path);
                if (!HashMatches) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                result = Version("v1", "site-project", OldBytes);
            }
            else if (path == "/search")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Single(value => value.StartsWith("query=", StringComparison.Ordinal))[6..];
                Queries.Add(Uri.UnescapeDataString(query));
                result = new JsonObject { ["hits"] = new JsonArray(Hit("site-project"), Hit("counterfeit")) };
            }
            else if (path == "/project/site-project/version")
            {
                result = new JsonArray(Version("v2", "site-project", NewBytes));
            }
            else if (path == "/project/counterfeit/version")
            {
                result = new JsonArray(Version("wrong", "counterfeit", _counterfeit));
            }
            else if (path == "/version/v2")
            {
                result = Version("v2", "site-project", NewBytes);
            }
            else if (path == "/version/wrong")
            {
                result = Version("wrong", "counterfeit", _counterfeit);
            }
            else if (path.StartsWith("/project/", StringComparison.Ordinal))
            {
                result = new JsonObject { ["title"] = "Native Display Name" };
            }
            else
            {
                throw new InvalidOperationException("Unexpected fixture route: " + path);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result.ToJsonString(), Encoding.UTF8, "application/json") });
        }

        private static JsonObject Hit(string project) => new()
        {
            ["project_id"] = project,
            ["slug"] = project,
            ["title"] = "Native Display Name",
            ["author"] = project,
            ["project_type"] = "mod"
        };

        private static JsonObject Version(string id, string project, byte[] bytes) => new()
        {
            ["id"] = id,
            ["project_id"] = project,
            ["version_number"] = id,
            ["version_type"] = id == "v2" ? "beta" : "release",
            ["date_published"] = "2026-11-04T00:00:00Z",
            ["game_versions"] = new JsonArray("26.3"),
            ["loaders"] = new JsonArray("fabric"),
            ["dependencies"] = new JsonArray(),
            ["files"] = new JsonArray(new JsonObject
            {
                ["filename"] = id + ".jar",
                ["url"] = "https://content.invalid/files/" + id + ".jar",
                ["primary"] = true,
                ["size"] = bytes.Length,
                ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(bytes)) }
            })
        };
    }
}