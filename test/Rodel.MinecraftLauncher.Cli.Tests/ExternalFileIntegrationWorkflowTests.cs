// Copyright (c) Richasy. All rights reserved.

using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises file-based client registration through the actual integration commands.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ExternalFileIntegrationWorkflowTests
{
    /// <summary>
    /// Exercises creation, rebind, and owned removal with noncanonical neighboring configuration.
    /// Exact bytes after removal distinguish a scoped edit from serializing the other entries.
    /// </summary>
    [TestMethod]
    [DataRow("copilot", "mcp-config.json", """{ "mcpServers" : { "neighbor":{"type":"local","args":[],"timeout":120,"future":{ "keep":true }} }, "unknown": [1,2] }""")]
    [DataRow("deepseek-harness", "cordis.patch.yml", "\uFEFF# 手工条目\r\n- id: keep-original\r\n  config: { value: !!js '1 + 2' }\r\n")]
    [DataRow("deepseek-harness", "cordis.patch.yml", "[{id: keep-original, config: {future: true}}, # keep trailing comma\r\n]\r\n")]
    [DataRow("deepseek-harness", "cordis.patch.yml", null)]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task FileBasedClient_OwnedRebindAndRemove_PreserveOtherConfiguration(string client, string filename, string? fixture)
    {
        using var source = new CliSandbox();
        using var destination = new CliSandbox();
        var home = Directory.CreateDirectory(Path.Combine(source.Root, "External client home")).FullName;
        var config = Path.Combine(home, filename);
        var requestPath = Path.Combine(source.Root, "request.json");
        var request = new JsonObject
        {
            ["client"] = client,
            ["clientConfigRoot"] = home,
            ["installationRoot"] = Path.Combine(source.Root, "installation"),
            ["launcherExecutable"] = Environment.GetEnvironmentVariable("RODEL_TEST_CLI_EXECUTABLE")
                ?? Path.Combine(AppContext.BaseDirectory, "cli", "rodel-mc.exe")
        };
        var ct = TestContext.CancellationToken;
        if (fixture is not null) await File.WriteAllTextAsync(config, fixture, ct);
        await File.WriteAllTextAsync(requestPath, request.ToJsonString(), ct);

        var created = await ApplyAsync(source, requestPath, "register");
        Assert.IsTrue(created["mcp"]!["registered"]!.GetValue<bool>());
        Assert.IsTrue(File.Exists(Path.Combine(home, "skills", "rodel-minecraft", "SKILL.md")));
        var installedText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(config, ct));
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(source.Root), installedText);

        var updated = await ApplyAsync(destination, requestPath, "update_owned");
        Assert.IsTrue(updated["mcp"]!["registered"]!.GetValue<bool>());
        Assert.AreEqual(installedText.Replace(System.Text.Json.JsonSerializer.Serialize(source.Root),
            System.Text.Json.JsonSerializer.Serialize(destination.Root), StringComparison.Ordinal), Encoding.UTF8.GetString(await File.ReadAllBytesAsync(config, ct)));
        var status = (await destination.RunAsync("ai", "integration", "status", "--file", requestPath)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(status["mcpRegistered"]!.GetValue<bool>());

        request["action"] = "remove";
        await File.WriteAllTextAsync(requestPath, request.ToJsonString(), ct);
        var removed = await ApplyAsync(destination, requestPath, "remove_owned");
        Assert.AreEqual("removed", removed["mcp"]!["status"]!.GetValue<string>());
        Assert.AreEqual(fixture ?? "[]\n", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(config, ct)));
    }

    private static async Task<JsonNode> ApplyAsync(CliSandbox cli, string requestPath, string expectedAction)
    {
        var plan = (await cli.RunAsync("ai", "integration", "plan", "--file", requestPath)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(expectedAction, plan["mcpAction"]!.GetValue<string>(), plan.ToJsonString());
        var receipt = (await cli.RunAsync("ai", "integration", "apply", "--file", requestPath,
            "--fingerprint", plan["fingerprint"]!.GetValue<string>())).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", receipt["status"]!.GetValue<string>());
        return receipt;
    }

    /// <summary>Gets or sets the deadline for isolated configuration operations.</summary>
    public TestContext TestContext { get; set; }
}