namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Checks dynamic rule extraction through the actual CLI launch planner without installing or running Minecraft.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class MetadataRuleWorkflowTests
{
    /// <summary>
    /// A real plan must use the same rule view for architecture preflight, libraries and mixed argument entries.
    /// Synthetic Java reporting replaces only JDK discovery; checking selected paths/arguments and unchanged input
    /// catches a disconnected reader, eager schema rejection or a preflight crash on an ignored rule entry.
    /// </summary>
    [TestMethod]
    public async Task LaunchPlan_DynamicRules_SelectsLibrariesAndArgumentsWithoutRewritingMetadata()
    {
        using var cli = new CliSandbox();
        var java = new JavaProbeFixture(cli.Root);
        var instance = (await cli.RunAsync("instance", "create", "metadata-rules", "--version", "synthetic")).ReadEnvelope(0)["data"]!;
        var directory = Path.Combine(instance["minecraftDirectory"]!.GetValue<string>(), "versions", "synthetic");
        Directory.CreateDirectory(directory);
        var metadata = """
            {
              "id":"synthetic","mainClass":"fixture.Main","javaVersion":{"majorVersion":25},
              "libraries":[
                {"name":"fixture:allowed:1.0","rules":[17,null,{"action":"allow","os":{"arch":"amd64"},"vendor":{"future":true}}]},
                {"name":"fixture:blocked:1.0","rules":[{"action":"allow","os":{"name":"linux"}}]}
              ],
              "arguments":{
                "jvm":["-Dplain=kept",{"rules":[null,17,{"action":"allow","features":{"is_demo_user":false}}],
                  "value":["-Dconditional=kept"]}],
                "game":["--plain",{"rules":[{"action":"allow","features":{"future_absent_flag":false}}],"value":"--conditional"},
                  {"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo-only"}]
              },
              "vendor":{"nested":[1,null,{"retained":true}]}
            }
            """;
        var file = Path.Combine(directory, "synthetic.json");
        await File.WriteAllTextAsync(file, metadata, TestContext.CancellationToken);
        var plan = (await cli.RunAsync("launch", instance["id"]!.GetValue<string>(), "--plan", "--offline", "--java", java.Executable))
            .ReadEnvelope(0)["data"]!;
        var arguments = plan["arguments"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        Assert.Contains("-Dplain=kept", arguments);
        Assert.Contains("-Dconditional=kept", arguments);
        Assert.Contains("--conditional", arguments);
        Assert.DoesNotContain("--demo-only", arguments);
        var libraries = plan["requiredFiles"]!.AsArray()
            .Where(value => value!["kind"]!.ToString() == "library")
            .Select(value => Path.GetFileName(value!["path"]!.GetValue<string>())).ToArray();
        Assert.HasCount(1, libraries);
        Assert.AreEqual("allowed-1.0.jar", libraries[0]);
        Assert.AreEqual(metadata, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
    }

    /// <summary>Gets the runner deadline for the isolated metadata fixture.</summary>
    public TestContext TestContext { get; set; } = null!;

}