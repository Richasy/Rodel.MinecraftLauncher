using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises owned Codex registration changes through the real installer and an isolated Codex home.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CodexIntegrationWorkflowTests
{
    /// <summary>
    /// Updates an owned binding after a neighboring MCP is added. The original text is the oracle for
    /// empty arguments, integer timeouts, and fields the older client may drop when rewriting its MCP table.
    /// </summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task OwnedCodexBinding_RebindAndRemove_PreserveOtherServerText()
    {
        var codex = Environment.GetEnvironmentVariable("RODEL_TEST_CODEX_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(codex))
            Assert.Inconclusive("Set RODEL_TEST_CODEX_EXECUTABLE to a Codex executable to run this isolated integration test.");

        using var original = new CliSandbox();
        using var destination = new CliSandbox();
        var configRoot = Path.Combine(original.Root, "Codex home");
        var requestFile = Path.Combine(Directory.CreateDirectory(original.Root).FullName, "integration.json");
        var request = new JsonObject
        {
            ["client"] = "codex",
            ["action"] = "install",
            ["clientExecutable"] = codex,
            ["clientConfigRoot"] = configRoot,
            ["skillsRoot"] = Path.Combine(original.Root, "skills"),
            ["installationRoot"] = Path.Combine(original.Root, "installation"),
            ["launcherExecutable"] = Environment.GetEnvironmentVariable("RODEL_TEST_CLI_EXECUTABLE")
                ?? Path.Combine(AppContext.BaseDirectory, "cli", "rodel-mc.exe")
        };
        await File.WriteAllTextAsync(requestFile, request.ToJsonString(), TestContext.CancellationToken);
        var created = await ApplyAsync(original, requestFile, "register");
        Assert.IsTrue(created["mcp"]!["registered"]!.GetValue<bool>());

        var neighbor = """
            [mcp_servers.node_repl]
            # Keep explicit defaults, comments and extension fields on unrelated servers.
            command = "must-not-launch.exe"
            args = []
            startup_timeout_sec = 120
            future_setting = "keep"
            """.ReplaceLineEndings("\n");
        var configFile = Path.Combine(configRoot, "config.toml");
        await File.AppendAllTextAsync(configFile, "\n" + neighbor + "\n", TestContext.CancellationToken);

        var updated = await ApplyAsync(destination, requestFile, "update_owned");
        Assert.IsTrue(updated["mcp"]!["registered"]!.GetValue<bool>());
        var status = (await destination.RunAsync("ai", "integration", "status", "--file", requestFile)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(status["mcpRegistered"]!.GetValue<bool>());
        Assert.IsFalse(status["bindingChanged"]!.GetValue<bool>());
        Assert.Contains(neighbor, await File.ReadAllTextAsync(configFile, TestContext.CancellationToken));

        request["action"] = "remove";
        await File.WriteAllTextAsync(requestFile, request.ToJsonString(), TestContext.CancellationToken);
        var removed = await ApplyAsync(destination, requestFile, "remove_owned");
        Assert.AreEqual("removed", removed["mcp"]!["status"]!.ToString());
        Assert.Contains(neighbor, await File.ReadAllTextAsync(configFile, TestContext.CancellationToken));
    }

    private static async Task<JsonNode> ApplyAsync(CliSandbox cli, string requestFile, string action)
    {
        var plan = (await cli.RunAsync("ai", "integration", "plan", "--file", requestFile)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(action, plan["mcpAction"]!.ToString());
        var receipt = (await cli.RunAsync("ai", "integration", "apply", "--file", requestFile,
            "--fingerprint", plan["fingerprint"]!.ToString())).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", receipt["status"]!.ToString(), receipt["mcp"]!.ToJsonString());
        return receipt;
    }

    /// <summary>Gets or sets the deadline for operations using the test-owned configuration.</summary>
    public TestContext TestContext { get; set; }
}