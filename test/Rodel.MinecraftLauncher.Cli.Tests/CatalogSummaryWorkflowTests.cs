// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects catalog provenance and open metadata across the actual CLI serialization boundary.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CatalogSummaryWorkflowTests
{
    /// <summary>Offline release discovery must retain provider fields and explicit nulls without rewriting the source manifest.</summary>
    [TestMethod]
    public async Task VersionCatalog_OfflineRead_PreservesProviderMetadata()
    {
        using var cli = new CliSandbox();
        var cache = Path.Combine(cli.Root, "cache", "version_manifest_v2.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        const string manifest = """{"latest":{"release":"future"},"versions":[{"id":"future","type":"release","providerField":{"enabled":true}}],"providerExtension":null}""";
        await File.WriteAllTextAsync(cache, manifest, TestContext.CancellationToken);

        var result = (await cli.RunAsync("versions", "list", "--offline")).ReadEnvelope(0)["data"]!.AsObject();

        Assert.IsTrue(result["cached"]!.GetValue<bool>());
        Assert.IsTrue(result["offline"]!.GetValue<bool>());
        Assert.AreEqual("future", result["latest"]!["release"]!.GetValue<string>());
        Assert.IsTrue(result["versions"]![0]!["providerField"]!["enabled"]!.GetValue<bool>());
        Assert.IsTrue(result.ContainsKey("providerExtension"));
        Assert.IsNull(result["providerExtension"]);
        Assert.IsEmpty(result["localVersions"]!.AsArray());
        Assert.AreEqual(manifest, await File.ReadAllTextAsync(cache, TestContext.CancellationToken));
    }

    /// <summary>Typed loader summaries must preserve provider-specific candidates, optional-field absence, and explicit null metadata.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoaderCatalog_OfflineRead_PreservesOpenMetadata(bool timestampPresent)
    {
        using var cli = new CliSandbox();
        var cache = Path.Combine(cli.Root, "cache", "loader-catalogs", "fabric", "1.21.1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var catalog = JsonNode.Parse("""{"loader":"fabric","minecraftVersion":"1.21.1","versions":[{"version":"0.16.0","minecraftVersion":"1.21.1","providerField":{"enabled":true}}],"providerExtension":null}""")!.AsObject();
        if (timestampPresent)
            catalog["cachedAt"] = null;
        var original = catalog.ToJsonString();
        await File.WriteAllTextAsync(cache, original, TestContext.CancellationToken);

        var result = (await cli.RunAsync("loader", "list", "fabric", "--minecraft", "1.21.1", "--offline")).ReadEnvelope(0)["data"]!.AsObject();

        Assert.AreEqual("cache", result["source"]!.GetValue<string>());
        Assert.IsTrue(result["offline"]!.GetValue<bool>());
        Assert.IsTrue(result.ContainsKey("warning"));
        Assert.IsNull(result["warning"]);
        Assert.IsTrue(result["versions"]![0]!["providerField"]!["enabled"]!.GetValue<bool>());
        Assert.IsTrue(result.ContainsKey("providerExtension"));
        Assert.IsNull(result["providerExtension"]);
        Assert.AreEqual(timestampPresent, result.ContainsKey("cachedAt"));
        Assert.AreEqual(original, await File.ReadAllTextAsync(cache, TestContext.CancellationToken));
    }

    /// <summary>Offline Java catalogs must carry open platform data and distinguish a missing cache from a usable cached release list.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task JavaCatalog_OfflineRead_PreservesProvenanceAndPlatformData(bool cached)
    {
        using var cli = new CliSandbox();
        if (cached)
        {
            var path = Path.Combine(cli.Root, "cache", "java-runtime-all.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                """{"windows-x64":{"future-runtime":[{"version":{"name":"99.0.1"},"providerField":true}]},"providerExtension":null}""",
                TestContext.CancellationToken);
        }

        var result = (await cli.RunAsync("java", "available", "--offline")).ReadEnvelope(0)["data"]!.AsObject();

        Assert.AreEqual("cache", result["source"]!.GetValue<string>());
        Assert.IsTrue(result["offline"]!.GetValue<bool>());
        Assert.AreEqual(!cached, result.ContainsKey("warning"));
        if (cached)
        {
            Assert.IsTrue(result["windows-x64"]!["future-runtime"]![0]!["providerField"]!.GetValue<bool>());
            Assert.IsTrue(result.ContainsKey("providerExtension"));
            Assert.IsNull(result["providerExtension"]);
        }
        else
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(result["warning"]!.GetValue<string>()));
        }
    }

    /// <summary>Gets or sets the test cancellation context.</summary>
    public TestContext TestContext { get; set; }
}