using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects local declaration analysis without equating it with loader compatibility or online repair.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ContentDependencyAnalysisTests
{
    /// <summary>
    /// A loader-declared provided ID (such as Fabric API's legacy alias) satisfies a consumer without a
    /// second JAR named after that alias. Disabled providers must stop satisfying it. This prevents a
    /// real local configuration from being falsely reported as missing a required mod.
    /// </summary>
    [TestMethod]
    public async Task Analyze_ProvidedAlias_RespectsProviderEnabledState()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        WriteMod(Path.Combine(fixture.Mods, "provider.jar"), new JsonObject
        {
            ["id"] = "provider",
            ["version"] = "1",
            ["provides"] = new JsonArray("legacy-api")
        });
        WriteMod(Path.Combine(fixture.Mods, "consumer.jar"), new JsonObject
        {
            ["id"] = "consumer",
            ["version"] = "1",
            ["depends"] = new JsonObject { ["legacy-api"] = "*" }
        });

        var present = (await cli.RunAsync("content", "analyze", fixture.InstanceId)).ReadEnvelope(0)["data"]!;
        Assert.DoesNotContain(finding => finding?["dependency"]?["id"]?.ToString() == "legacy-api", present["findings"]!.AsArray());
        Assert.IsTrue(present["advisoryOnly"]!.GetValue<bool>());
        (await cli.RunAsync("content", "disable", fixture.InstanceId, "provider.jar")).ReadEnvelope(0);
        var disabled = (await cli.RunAsync("content", "analyze", fixture.InstanceId)).ReadEnvelope(0)["data"]!;
        Assert.Contains(finding => finding?["kind"]?.ToString() == "missing-declared-dependency"
            && finding["dependency"]?["id"]?.ToString() == "legacy-api", disabled["findings"]!.AsArray());
    }

    /// <summary>Provides the runner's cancellation signal for isolated fixture creation.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static void WriteMod(string path, JsonObject descriptor)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open());
        writer.Write(descriptor.ToJsonString());
    }
}