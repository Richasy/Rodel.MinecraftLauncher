using System.IO.Compression;
using System.Text;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Checks the real shader command tree and serialized results against actual engine configuration, without claiming game rendering.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class VisualPackWorkflowTests
{
    /// <summary>
    /// Core selection tests do not cover CLI symbols, optional disable semantics or NativeAOT metadata.
    /// This small process workflow checks plan/apply/read/clear using actual commands and an independent raw file.
    /// It can run unchanged against the published CLI through RODEL_TEST_CLI_EXECUTABLE.
    /// </summary>
    [TestMethod]
    public async Task ShaderCommands_PlanSelectReadAndClear_ActualConfiguration()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("instance", "create", "shader-command-fixture", "--version", "26.3")).ReadEnvelope(0);
        var id = created["data"]!["id"]!.ToString();
        var game = created["data"]!["gameDirectory"]!.ToString();
        Directory.CreateDirectory(Path.Combine(game, "shaderpacks"));
        using (var archive = ZipFile.Open(Path.Combine(game, "shaderpacks", "fixture.zip"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("shaders/fixture.fsh").Open())) writer.Write("synthetic");
        Directory.CreateDirectory(Path.Combine(game, "config"));
        var config = Path.Combine(game, "config", "iris.properties");
        await File.WriteAllTextAsync(config, "shaderPack=\nenableShaders=false\nfutureKey=retained\n", TestContext.CancellationToken);
        var before = await File.ReadAllBytesAsync(config, TestContext.CancellationToken);
        var listing = (await cli.RunAsync("shaderpack", "list", id)).ReadEnvelope(0);
        var fingerprint = listing["data"]!["fingerprint"]!.ToString();
        var plan = (await cli.RunAsync("shaderpack", "select", id, "--file", "fixture.zip", "--plan", "--expected-fingerprint", fingerprint)).ReadEnvelope(0);
        Assert.AreEqual("planned", plan["data"]!["state"]!.ToString());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(config, TestContext.CancellationToken));
        var applied = (await cli.RunAsync("shaderpack", "select", id, "--file", "fixture.zip", "--expected-fingerprint", fingerprint)).ReadEnvelope(0);
        Assert.IsTrue(applied["data"]!["selection"]!["enabled"]!.GetValue<bool>());
        Assert.Contains("shaderPack=fixture.zip", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(config, TestContext.CancellationToken)));
        var selected = (await cli.RunAsync("shaderpack", "list", id)).ReadEnvelope(0);
        Assert.AreEqual("fixture.zip", selected["data"]!["selected"]!.ToString());
        var cleared = (await cli.RunAsync("shaderpack", "select", id)).ReadEnvelope(0);
        Assert.IsFalse(cleared["data"]!["selection"]!["enabled"]!.GetValue<bool>());
        Assert.Contains("futureKey=retained", await File.ReadAllTextAsync(config, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the cancellation context used by this isolated file fixture.</summary>
    public TestContext TestContext { get; set; }
}