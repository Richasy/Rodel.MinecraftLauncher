using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Checks selection-report serialization through the real launch-plan command, including the NativeAOT target when configured.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JavaSelectionWorkflowTests
{
    /// <summary>A synthetic offline plan must preserve selected Java facts and protocol names without downloading files or starting Minecraft.</summary>
    [TestMethod]
    public async Task LaunchPlan_ExplicitJava_EmitsTypedSelectionContract()
    {
        using var sandbox = new CliSandbox();
        var fixture = new JavaProbeFixture(sandbox.Root);
        var created = (await sandbox.RunAsync("instance", "create", "selection-fixture", "--version", "synthetic")).ReadEnvelope(0)["data"]!;
        var minecraft = created["minecraftDirectory"]!.GetValue<string>();
        var directory = Path.Combine(minecraft, "versions", "synthetic");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "synthetic.json"), new JsonObject
        {
            ["id"] = "synthetic",
            ["mainClass"] = "Synthetic.Main",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 25, ["component"] = "java-runtime-epsilon" },
            ["libraries"] = new JsonArray(),
            ["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() }
        }.ToJsonString());
        var result = (await sandbox.RunAsync("launch", created["id"]!.GetValue<string>(), "--offline", "--plan", "--java", fixture.Executable)).ReadEnvelope(0);
        var selection = result["data"]!["javaSelection"]!;
        Assert.AreEqual("explicit", selection["source"]!.GetValue<string>());
        Assert.AreEqual(fixture.Executable, selection["selectedPath"]!.GetValue<string>());
        Assert.AreEqual(25, selection["selectedMajor"]!.GetValue<int>());
        Assert.AreEqual("25.0.1", selection["selectedVersion"]!.GetValue<string>());
        Assert.IsTrue(selection["advisory"]!.GetValue<bool>());
        Assert.IsTrue(selection.AsObject().ContainsKey("compatibilityNote"));
        Assert.IsNull(selection["compatibilityNote"]);
        Assert.AreEqual(25, selection["candidates"]![0]!["major"]!.GetValue<int>());
        Assert.IsTrue(selection["candidates"]![0]!["usable"]!.GetValue<bool>());
        Assert.IsEmpty(Directory.GetFiles(sandbox.Root, "*.stdout.log", SearchOption.AllDirectories));
        var sessions = Path.Combine(sandbox.Root, "sessions");
        Assert.IsFalse(Directory.Exists(sessions) && Directory.GetFiles(sessions, "*.json").Length > 0,
            "Planning must not create a game session.");
    }
}