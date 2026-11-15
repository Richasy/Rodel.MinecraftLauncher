using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Protects inventory read recovery and advisory declaration indexing for manually installed packages.
/// Mutation/review authorization is covered by the neighboring content workflow suites.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ContentReadTests
{
    private static readonly string[] ExpectedDuplicateFiles = ["first.jar", "second.jar"];

    /// <summary>
    /// A held exclusive handle must not hide a package or abort the inventory. Releasing it must restore
    /// its digest on the next read; advisory analysis must keep unknown packages manageable throughout.
    /// Existing mutation tests do not cross this read-failure/recovery boundary.
    /// </summary>
    [TestMethod]
    public async Task ListAndAnalyze_ExclusivePackageHandle_ReportWarningAndRecoverAfterRelease()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var installed = Path.Combine(fixture.Mods, "selected.jar");
        File.Copy(fixture.Source, installed);
        using (var held = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var inventory = (await cli.RunAsync("content", "list", fixture.InstanceId)).ReadEnvelope(0)["data"]!.AsArray();
            var unreadable = inventory.Single(item => item?["fileName"]?.ToString() == "selected.jar")!.AsObject();
            Assert.IsTrue(unreadable["isContent"]?.GetValue<bool>());
            Assert.IsTrue(unreadable["enabled"]?.GetValue<bool>());
            Assert.IsNotNull(unreadable["readWarning"]);
            Assert.IsTrue(unreadable.ContainsKey("sha1"), "A failed read reports an explicit unknown digest, rather than an unrequested hash.");
            Assert.IsNull(unreadable["sha1"]);
            Assert.Contains(item => item?["fileName"]?.ToString() == "notes.txt", inventory, "One inaccessible archive must not suppress unrelated entries.");

            var analysis = (await cli.RunAsync("content", "analyze", fixture.InstanceId)).ReadEnvelope(0)["data"]!;
            Assert.IsTrue(analysis["advisoryOnly"]?.GetValue<bool>());
            Assert.Contains(finding => finding?["kind"]?.ToString() == "unknown-metadata"
                && finding["fileName"]?.ToString() == "selected.jar", analysis["findings"]!.AsArray());
        }

        var recovered = (await cli.RunAsync("content", "list", fixture.InstanceId)).ReadEnvelope(0)["data"]!.AsArray()
            .Single(item => item?["fileName"]?.ToString() == "selected.jar")!.AsObject();
        Assert.IsNotNull(recovered["sha1"]);
        Assert.IsFalse(recovered.ContainsKey("readWarning"), "A successful rescan must not retain a transient access warning.");
        Assert.IsTrue(recovered["archiveReadable"]?.GetValue<bool>());
        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken),
            await File.ReadAllBytesAsync(installed, TestContext.CancellationToken));
    }

    /// <summary>
    /// Indexing real archive metadata must retain declared ranges, report duplicate IDs and missing required
    /// dependencies, and ignore disabled providers. The existing unknown-package/recovery cases do not exercise
    /// declaration indexing; checking the findings crosses both the metadata parser and CLI serialization boundary.
    /// </summary>
    [TestMethod]
    public async Task Analyze_DeclaredDependenciesAndDisabledProvider_RetainsRangesAndReportsAdvisoryFindings()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var first = Path.Combine(fixture.Mods, "first.jar");
        WriteFabricMetadata(first, """
            {"id":"duplicate","version":"1","name":"First","depends":{"provider":[">=2","<4"],"minecraft":"*"},"future":{"nullable":null}}
            """);
        WriteFabricMetadata(Path.Combine(fixture.Mods, "second.jar"), """{"id":"duplicate","version":"2"}""");
        WriteFabricMetadata(Path.Combine(fixture.Mods, "provider.jar.disabled"), """{"id":"provider","version":"3"}""");
        var before = await File.ReadAllBytesAsync(first, TestContext.CancellationToken);

        var report = (await cli.RunAsync("content", "analyze", fixture.InstanceId)).ReadEnvelope(0)["data"]!;
        var mods = report["mods"]!.AsArray();
        Assert.AreEqual(2, mods.Count, "A disabled provider must not enter the enabled mod index.");
        var findings = report["findings"]!.AsArray();
        var duplicate = findings.Single(finding => finding?["kind"]?.ToString() == "duplicate-id")!;
        CollectionAssert.AreEquivalent(ExpectedDuplicateFiles, duplicate["files"]!.AsArray().Select(file => file!.ToString()).ToArray());
        var missing = findings.Single(finding => finding?["kind"]?.ToString() == "missing-declared-dependency")!;
        Assert.AreEqual("provider", missing["dependency"]?["id"]?.ToString());
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""[">=2","<4"]"""), missing["dependency"]?["range"]));
        Assert.IsTrue(report["advisoryOnly"]?.GetValue<bool>(), "Declared findings must not become launch prerequisites.");
        var inventory = report["files"]!.AsArray().Single(file => file?["fileName"]?.ToString() == "first.jar")!;
        Assert.IsTrue(inventory["metadata"]!["future"]!.AsObject().ContainsKey("nullable"));
        Assert.IsNull(inventory["metadata"]!["future"]!["nullable"]);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(first, TestContext.CancellationToken));
    }

    private static void WriteFabricMetadata(string file, string metadata)
    {
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open());
        writer.Write(metadata);
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}