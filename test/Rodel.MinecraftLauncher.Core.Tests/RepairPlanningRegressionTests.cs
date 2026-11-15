using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Tools;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects repair admission against misleading schemas and local metadata/indexing failures; no model or public service is substituted for success.</summary>
[TestClass]
public sealed class RepairPlanningRegressionTests
{
    /// <summary>Review: generic same-name field conventions advertised content-directory kinds for a repair action. Existing authorization
    /// cases do not inspect this model-visible schema. Require the four business actions and matching rejection before incident lookup.</summary>
    [TestMethod]
    public void RepairSchema_AdvertisesAndAcceptsOnlyRepairActions()
    {
        var contract = new ToolInputContract<AiRepairPlan>(ToolInputJsonContext.Default.AiRepairPlan);
        var kinds = contract.Schema.GetProperty("properties").GetProperty("kind").GetProperty("enum");
        CollectionAssert.AreEquivalent(ExpectedRepairKinds, kinds.EnumerateArray().Select(value => value.GetString()).ToArray());
        foreach (var kind in new[] { "disable_mod", "java", "memory", "dependencies" })
            Assert.AreEqual(kind, contract.Deserialize(new() { ["repairId"] = "incident", ["kind"] = kind, ["reason"] = "evidence" }).Kind);
        foreach (var invalid in new[] { "mods", "dependency", "mod" })
            Assert.ThrowsExactly<ArgumentException>(() => contract.Deserialize(new() { ["repairId"] = "incident", ["kind"] = invalid, ["reason"] = "evidence" }));
    }

    /// <summary>Review: Fabric accepts literal description newlines; losing the declaration prevents the log's mod ID from authorizing
    /// the actual archive. Real ZIP indexing, incident planning, disable and receipt-based restore must preserve bytes and unknown fields.
    /// An unrelated archive remains ineligible. Existing memory repair tests do not cross this identity/evidence boundary.</summary>
    [TestMethod]
    public async Task LiteralDescriptionNewlines_RetainIdentity_AllowOnlyNamedModAndRestoreBytes() => await ReportStorage.RunAsync(async paths =>
    {
        var client = new LauncherClient(paths);
        var instance = await client.Instances.CreateAsync("Repair metadata", "fixture", TestContext.CancellationToken);
        var mods = Path.Combine(instance.GameDirectory, "mods");
        Directory.CreateDirectory(mods);
        var target = Path.Combine(mods, "different-file-name-1.0.jar");
        WriteMod(target, """
            {"schemaVersion":1,"id":"example_mod","version":"1.0","description":"First line
            Second line with \"quotes\" and \\path","future":{"value":42},"depends":{}}
            """);
        WriteMod(Path.Combine(mods, "unrelated.jar"), """{"id":"unrelated","version":"1.0"}""");
        WriteMod(Path.Combine(mods, "broken.jar"), """{"id":"broken","unfinished":}""");
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(target, TestContext.CancellationToken));
        var session = await FailedSessionAsync(client, instance.Id, "Incompatible mods found!\nImmediate reason: [HARD_DEP_NO_CANDIDATE example_mod 1.0 {depends library @ [>=1.0]}]");
        var repair = await client.Repairs.CreateAsync(new() { InstanceId = instance.Id, SessionId = session }, TestContext.CancellationToken);
        var analysis = await client.AnalyzeContentAsync(instance.Id, TestContext.CancellationToken);
        Assert.AreEqual("example_mod", analysis.Mods.Single(mod => mod.FileName == Path.GetFileName(target)).Id);
        Assert.AreEqual(42, analysis.Files.Single(file => file.FileName == Path.GetFileName(target)).Metadata!["future"]!["value"]!.GetValue<int>());
        Assert.IsNull(analysis.Files.Single(file => file.FileName == "broken.jar").Metadata, "Other malformed JSON must remain a visible metadata warning.");
        var unrelated = await Assert.ThrowsExactlyAsync<LauncherOperationException>(() => client.Repairs.PlanAsync(new()
        { RepairId = repair.Id!, Kind = "disable_mod", Target = "unrelated.jar", Reason = "not mentioned in evidence" }, TestContext.CancellationToken));
        Assert.AreEqual("REPAIR_MOD_EVIDENCE_REQUIRED", unrelated.Code);
        var plan = await client.Repairs.PlanAsync(new()
        { RepairId = repair.Id!, Kind = "disable_mod", Target = Path.GetFileName(target), Reason = "Loader identifies example_mod" }, TestContext.CancellationToken);
        var action = new AiRepairAction { RepairId = repair.Id!, PlanId = plan.Id! };
        await client.Repairs.ApplyAsync(action, TestContext.CancellationToken);
        Assert.IsFalse(File.Exists(target));
        Assert.IsTrue(File.Exists(target + ".disabled"));
        await client.Repairs.RestoreAsync(action, TestContext.CancellationToken);
        CollectionAssert.AreEqual(hash, SHA256.HashData(await File.ReadAllBytesAsync(target, TestContext.CancellationToken)));
        Assert.IsTrue(File.Exists(Path.Combine(mods, "unrelated.jar")));
    });

    /// <summary>Review: shared nested libraries are loader candidates, not duplicate mandatory mods. Existing online installation
    /// tests do not cross repair's independent local index. Real reviews must accept a satisfying existing candidate, reject an
    /// impossible range and still reject two mandatory top-level providers. No missing dependency means no HTTP is needed.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SharedNestedDependencies_RespectCandidatesAndMandatoryTopLevelProviders(bool topLevelProvider) => await ReportStorage.RunAsync(async paths =>
    {
        var client = new LauncherClient(paths);
        var instance = await client.Instances.CreateAsync("Nested repair", "26.3", TestContext.CancellationToken);
        var mods = Path.Combine(instance.GameDirectory, "mods");
        Directory.CreateDirectory(mods);
        WriteMod(Path.Combine(mods, "a-parent.jar"), """{"id":"parent_a","version":"1.0","jars":[{"file":"shared.jar"}]}""", "1.0");
        WriteMod(Path.Combine(mods, "b-parent.jar"), """{"id":"parent_b","version":"1.0","jars":[{"file":"shared.jar"}]}""", "2.0");
        if (topLevelProvider) WriteMod(Path.Combine(mods, "shared.jar"), """{"id":"shared","version":"2.0"}""");
        var consumer = Path.Combine(mods, "consumer.jar");
        WriteMod(consumer, """{"id":"consumer","version":"1.0","depends":{"shared":">=2.0"}}""");
        await client.Settings.UpdateAsync(new() { ["loader"] = "fabric", ["minecraftVersion"] = "26.3" }, instance: instance, cancellationToken: TestContext.CancellationToken);
        var session = await FailedSessionAsync(client, instance.Id, "Incompatible mods found!\nImmediate reason: [HARD_DEP_NO_CANDIDATE consumer 1.0 {depends shared @ [>=2.0]}]");
        var compatible = await client.PlanDependencyRepairAsync(instance.Id, session, cancellationToken: TestContext.CancellationToken);
        Assert.IsEmpty(compatible.Unresolved, compatible.ToString());
        Assert.IsEmpty(compatible.Plan.Nodes, "Already installed dependencies must not be downloaded or installed again.");
        Assert.DoesNotContain(finding => finding.Kind == "duplicate-id", (await client.AnalyzeContentAsync(instance.Id, TestContext.CancellationToken)).Findings);
        if (topLevelProvider)
        {
            var mandatory = Path.Combine(mods, "shared.jar");
            File.Delete(mandatory);
            WriteMod(mandatory, """{"id":"shared","version":"1.0"}""");
            var incompatibleMandatory = await client.PlanDependencyRepairAsync(instance.Id, session, cancellationToken: TestContext.CancellationToken);
            Assert.Contains(finding => finding.Reason == "installed-version-conflict-or-unsupported-range", incompatibleMandatory.Unresolved,
                "A satisfying optional nested candidate cannot replace a mandatory top-level version.");
            File.Delete(mandatory);
            WriteMod(mandatory, """{"id":"shared","version":"2.0"}""");
        }

        File.Delete(consumer);
        WriteMod(consumer, """{"id":"consumer","version":"1.0","depends":{"shared":">=3.0"}}""");
        var impossible = await client.PlanDependencyRepairAsync(instance.Id, session, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(finding => finding.Reason == "installed-version-conflict-or-unsupported-range", impossible.Unresolved);
        if (!topLevelProvider) WriteMod(Path.Combine(mods, "shared.jar"), """{"id":"shared","version":"2.0"}""");
        WriteMod(Path.Combine(mods, "duplicate.jar"), """{"id":"shared","version":"2.0"}""");
        var duplicate = await client.PlanDependencyRepairAsync(instance.Id, session, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(finding => finding.Reason == "duplicate-local-mod-id" && finding.DependencyId == "shared", duplicate.Unresolved);
    });

    /// <summary>Review: platform search cannot assume a loader ID is a slug. This real planner/download/index/commit flow substitutes
    /// HTTP responses only. A plausible but wrongly identified JAR must be rejected despite matching search/version metadata and hashes;
    /// a verified candidate must install without touching the consumer or upgrading a satisfying installed library through provider
    /// dependency metadata. Existing content-plan tests do not verify loader ID discovery or this add-only repair path.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DependencySearch_ReadableFallback_RequiresVerifiedArchiveIdentity(bool correctIdentity) => await ReportStorage.RunAsync(async paths =>
    {
        var client = new LauncherClient(paths);
        var instance = await client.Instances.CreateAsync("Search repair", "26.3", TestContext.CancellationToken);
        var mods = Path.Combine(instance.GameDirectory, "mods");
        Directory.CreateDirectory(mods);
        var consumer = Path.Combine(mods, "consumer.jar");
        WriteMod(consumer, """{"id":"consumer","version":"1.0","depends":{"library_api_v3":">=1.0"}}""");
        var shared = Path.Combine(mods, "shared.jar");
        WriteMod(shared, """{"id":"shared","version":"1.0"}""");
        var sharedHash = SHA256.HashData(await File.ReadAllBytesAsync(shared, TestContext.CancellationToken));
        var original = SHA256.HashData(await File.ReadAllBytesAsync(consumer, TestContext.CancellationToken));
        await client.Settings.UpdateAsync(new() { ["loader"] = "fabric", ["minecraftVersion"] = "26.3" }, instance: instance, cancellationToken: TestContext.CancellationToken);
        var session = await FailedSessionAsync(client, instance.Id, "Incompatible mods found!\nImmediate reason: [HARD_DEP_NO_CANDIDATE consumer 1.0 {depends library_api_v3 @ [>=1.0]}]");
        using var handler = new DependencyCandidateHandler(correctIdentity);
        using var http = new HttpClient(handler);
        var platform = new ContentPlatformService(paths, http, new Uri("https://content.invalid/"));
        var review = await client.PlanDependencyRepairAsync(instance.Id, session, null, platform, TestContext.CancellationToken);
        CollectionAssert.AreEqual(ExpectedSearchQueries, handler.SearchQueries.ToArray());
        Assert.AreEqual(1, handler.ArchiveRequests, "Both outcomes must reach actual candidate download and archive identity verification.");
        Assert.AreEqual(correctIdentity, review.Plan.CanApply, string.Join(", ", review.Unresolved.Select(finding => finding.Reason)));
        Assert.HasCount(2, Directory.GetFiles(mods), "Planning may populate cache, never installed content.");
        if (correctIdentity)
        {
            Assert.IsEmpty(review.Unresolved);
            Assert.AreEqual("frozen", review.Plan.Nodes.Single().VersionId);
            await client.ApplyDependencyRepairAsync(instance.Id, review.Id, TestContext.CancellationToken);
            var installed = await client.AnalyzeContentAsync(instance.Id, TestContext.CancellationToken);
            Assert.Contains(mod => mod.Id == "library_api_v3" && mod.Version?.ToString() == "1.0", installed.Mods);
        }
        else
        {
            Assert.IsEmpty(review.Plan.Nodes);
            Assert.Contains(finding => finding.Reason == "no-verified-candidate-or-unsupported-range", review.Unresolved);
        }

        CollectionAssert.AreEqual(original, SHA256.HashData(await File.ReadAllBytesAsync(consumer, TestContext.CancellationToken)));
        CollectionAssert.AreEqual(sharedHash, SHA256.HashData(await File.ReadAllBytesAsync(shared, TestContext.CancellationToken)));
    });

    private async Task<string> FailedSessionAsync(LauncherClient client, string instanceId, string evidence)
    {
        var session = Guid.NewGuid().ToString("N");
        var log = Path.Combine(client.Paths.LogsDirectory, session + ".log");
        await File.WriteAllTextAsync(log, evidence, TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(client.Paths.SessionsDirectory, session + ".json"), new JsonObject
        { ["id"] = session, ["instanceId"] = instanceId, ["gameDirectory"] = client.Instances.Get(instanceId).GameDirectory, ["status"] = "exited", ["exitCode"] = 1, ["stdoutLog"] = log }, TestContext.CancellationToken);
        return session;
    }

    private static void WriteMod(string path, string metadata, string? nestedVersion = null)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open())) writer.Write(metadata);
        if (nestedVersion is null) return;
        using var nestedStream = archive.CreateEntry("shared.jar").Open();
        using var nested = new ZipArchive(nestedStream, ZipArchiveMode.Create);
        using var descriptor = new StreamWriter(nested.CreateEntry("fabric.mod.json").Open());
        descriptor.Write(new JsonObject { ["id"] = "shared", ["version"] = nestedVersion }.ToJsonString());
    }

    private sealed class DependencyCandidateHandler : HttpMessageHandler
    {
        private readonly byte[] _archive;
        private readonly JsonObject _version;
        internal List<string> SearchQueries { get; } = [];
        internal int ArchiveRequests { get; private set; }

        internal DependencyCandidateHandler(bool correctIdentity)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
                writer.Write(new JsonObject { ["id"] = correctIdentity ? "library_api_v3" : "unrelated_api", ["version"] = "1.0", ["depends"] = new JsonObject { ["shared"] = ">=1.0" } }.ToJsonString());
            }

            _archive = buffer.ToArray();
            _version = new()
            {
                ["id"] = "frozen",
                ["project_id"] = "library",
                ["version_number"] = "1.0",
                ["version_type"] = "release",
                ["game_versions"] = new JsonArray("26.3"),
                ["loaders"] = new JsonArray("fabric"),
                ["dependencies"] = new JsonArray(new JsonObject { ["project_id"] = "shared-project", ["dependency_type"] = "required" }),
                ["files"] = new JsonArray(new JsonObject
                {
                    ["filename"] = "library.jar",
                    ["url"] = "https://content.invalid/library.jar",
                    ["primary"] = true,
                    ["size"] = _archive.Length,
                    ["hashes"] = new JsonObject { ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(_archive)), ["sha512"] = Convert.ToHexStringLower(SHA512.HashData(_archive)) }
                })
            };
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/library.jar")
            {
                ArchiveRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_archive) });
            }

            JsonNode result;
            if (uri.AbsolutePath == "/search")
            {
                var query = Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&').Single(value => value.StartsWith("query=", StringComparison.Ordinal))[6..]);
                SearchQueries.Add(query);
                result = new JsonObject
                {
                    ["hits"] = query == "library api"
                    ? new JsonArray(new JsonObject { ["project_id"] = "library", ["slug"] = "different-platform-slug", ["project_type"] = "mod" }) : []
                };
            }
            else if (uri.AbsolutePath == "/project/library/version")
            {
                result = new JsonArray(_version.DeepClone());
            }
            else if (uri.AbsolutePath == "/version/frozen")
            {
                result = _version.DeepClone();
            }
            else if (uri.AbsolutePath == "/project/library")
            {
                result = new JsonObject { ["id"] = "library", ["title"] = "Library API", ["project_type"] = "mod" };
            }
            else
            {
                throw new InvalidOperationException("Unexpected repair provider request: " + uri);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>Gets cancellation for the isolated Core storage and ZIP operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] ExpectedRepairKinds = ["disable_mod", "java", "memory", "dependencies"];
    private static readonly string[] ExpectedSearchQueries = ["library_api_v3", "library api"];
}