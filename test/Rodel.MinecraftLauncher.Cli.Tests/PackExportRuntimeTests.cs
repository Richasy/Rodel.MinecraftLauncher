using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects exported runtime declarations against confusing profile IDs with game and loader versions.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PackExportRuntimeTests
{
    /// <summary>
    /// Export must describe the selected local runtime, including instance overrides, rather than an old
    /// registration or imported manifest. Existing export coverage has no installed loader profile. Reopening
    /// both archive formats through real inspection distinguishes a usable dependency declaration from a ZIP
    /// that merely exists; source metadata and unknown pack dependencies must remain intact.
    /// </summary>
    [TestMethod]
    [DataRow("mrpack", "fabric", "0.19.0", "net.fabricmc:fabric-loader:0.19.0", "fabric-loader", true)]
    [DataRow("curseforge", "fabric", "0.19.0", "net.fabricmc:fabric-loader:0.19.0", "fabric-loader", true)]
    [DataRow("mrpack", "quilt", "0.29.2", "org.quiltmc:quilt-loader:0.29.2", "quilt-loader", true)]
    [DataRow("curseforge", "quilt", "0.29.2", "org.quiltmc:quilt-loader:0.29.2", "quilt-loader", true)]
    [DataRow("mrpack", "forge", "47.4.0", "net.minecraftforge:forge:1.20.1-47.4.0", "forge", true)]
    [DataRow("curseforge", "forge", "47.4.0", "net.minecraftforge:forge:1.20.1-47.4.0", "forge", true)]
    [DataRow("mrpack", "forge", "47.4.28", "net.minecraftforge:fmlloader:1.20.1-47.4.28", "forge", true)]
    [DataRow("curseforge", "forge", "47.4.28", "net.minecraftforge:fmlloader:1.20.1-47.4.28", "forge", true)]
    [DataRow("mrpack", "neoforge", "21.1.1", "net.neoforged:neoforge:21.1.1", "neoforge", true)]
    [DataRow("curseforge", "neoforge", "21.1.1", "net.neoforged:neoforge:21.1.1", "neoforge", true)]
    [DataRow("mrpack", "neoforge", "47.1.106", "net.neoforged.fancymodloader:loader:47.2.2", "neoforge", true)]
    [DataRow("curseforge", "neoforge", "47.1.106", "net.neoforged.fancymodloader:loader:47.2.2", "neoforge", true)]
    [DataRow("mrpack", "vanilla", null, null, null, true)]
    [DataRow("curseforge", "vanilla", null, null, null, true)]
    [DataRow("mrpack", "fabric", "0.19.0", "net.fabricmc:fabric-loader:0.19.0", "fabric-loader", false)]
    [DataRow("curseforge", "fabric", "0.19.0", "net.fabricmc:fabric-loader:0.19.0", "fabric-loader", false)]
    public async Task Export_SelectedLoaderProfile_DeclaresBaseGameAndCurrentLoader(
        string format, string loader, string? loaderVersion, string? coordinate, string? dependency, bool imported)
    {
        using var cli = new CliSandbox();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = await client.Instances.CreateAsync("Runtime export", "old-registration", TestContext.CancellationToken);
        var selected = "custom-local-profile";
        await JsonFiles.WriteAsync(Path.Combine(instance.MinecraftDirectory, "versions", "1.20.1", "1.20.1.json"),
            new JsonObject { ["id"] = "1.20.1", ["mainClass"] = "net.minecraft.client.main.Main", ["libraries"] = new JsonArray() }, TestContext.CancellationToken);
        var profile = new JsonObject { ["id"] = selected, ["inheritsFrom"] = "1.20.1", ["libraries"] = coordinate is null ? [] : new JsonArray(new JsonObject { ["name"] = coordinate }) };
        // NeoForge 1.20.1 declares an independent FML version, which must not become the pack's NeoForge version.
        if (coordinate == "net.neoforged.fancymodloader:loader:47.2.2")
            profile["arguments"] = new JsonObject { ["game"] = new JsonArray("--fml.forgeVersion", "47.1.106", "--fml.fmlVersion", "47.2.2") };
        await JsonFiles.WriteAsync(Path.Combine(instance.MinecraftDirectory, "versions", selected, selected + ".json"), profile, TestContext.CancellationToken);
        var registration = JsonFiles.ReadObject(instance.MetadataPath);
        registration["settings"] = new JsonObject { ["versionId"] = selected };
        await JsonFiles.WriteAsync(instance.MetadataPath, registration, TestContext.CancellationToken);
        var trackingPath = Path.Combine(instance.GameDirectory, ".rodel", "pack-lock.json");
        if (imported)
        {
            await JsonFiles.WriteAsync(trackingPath, new JsonObject
            {
                ["dependencies"] = new JsonObject { ["minecraft"] = "1.19.4", ["forge"] = "old-loader", ["future"] = "retained" },
                ["files"] = new JsonArray()
            }, TestContext.CancellationToken);
        }

        var trackingBefore = imported ? await File.ReadAllBytesAsync(trackingPath, TestContext.CancellationToken) : null;
        var registrationBefore = await File.ReadAllBytesAsync(instance.MetadataPath, TestContext.CancellationToken);
        var output = Path.Combine(cli.Root, "runtime-export.zip");

        (await cli.RunAsync("pack", "export", instance.Id, output, "--format", format)).ReadEnvelope(0);
        var inspected = (await cli.RunAsync("pack", "inspect", output)).ReadEnvelope(0)["data"]!;

        Assert.AreEqual("1.20.1", inspected["minecraftVersion"]?.ToString(), $"The exported {loader} runtime must declare the Minecraft release, not its installation profile ID.");
        if (dependency is not null)
            Assert.AreEqual(loaderVersion, inspected["dependencies"]?[dependency]?.ToString());
        foreach (var key in sourceArray.Where(key => key != dependency))
            Assert.IsNull(inspected["dependencies"]?[key], "A loader outside the selected runtime must not survive export.");
        if (trackingBefore is not null)
        {
            Assert.AreEqual("retained", inspected["dependencies"]?["future"]?.ToString());
            CollectionAssert.AreEqual(trackingBefore, await File.ReadAllBytesAsync(trackingPath, TestContext.CancellationToken));
        }
        else
        {
            Assert.IsFalse(File.Exists(trackingPath), "Export must not convert an ordinary instance into a managed pack.");
        }

        CollectionAssert.AreEqual(registrationBefore, await File.ReadAllBytesAsync(instance.MetadataPath, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] sourceArray = ["fabric-loader", "quilt-loader", "forge", "neoforge"];
}