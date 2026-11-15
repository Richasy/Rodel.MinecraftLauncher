// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises failed and successful Claude rebinds through the real launcher CLI and MCP bridge.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeIntegrationWorkflowTests
{
    /// <summary>
    /// Rejects the candidate add after an owned registration exists, proving the old config remains
    /// byte-identical and usable via its recorded command. A successful retry must change only its value.
    /// </summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task OwnedClaudeBinding_WhenCandidateAddFails_PreservesWorkingRegistration()
    {
        using var original = new CliSandbox();
        using var destination = new CliSandbox();
        var configRoot = Path.Combine(Directory.CreateDirectory(original.Root).FullName, "Claude home");
        var configFile = Path.Combine(configRoot, ".claude.json");
        var requestFile = Path.Combine(original.Root, "integration.json");
        var request = new JsonObject
        {
            ["client"] = "claude",
            ["clientExecutable"] = Path.Combine(AppContext.BaseDirectory, "claude-mcp-fixture", "claude-mcp-fixture.exe"),
            ["clientConfigRoot"] = configRoot,
            ["skillsRoot"] = Path.Combine(original.Root, "skills"),
            ["installationRoot"] = Path.Combine(original.Root, "installation"),
            ["launcherExecutable"] = Environment.GetEnvironmentVariable("RODEL_TEST_CLI_EXECUTABLE")
                ?? Path.Combine(AppContext.BaseDirectory, "cli", "rodel-mc.exe")
        };
        var ct = TestContext.CancellationToken;
        await File.WriteAllTextAsync(requestFile, request.ToJsonString(), ct);
        var created = await ApplyAsync(original, requestFile, "register");
        Assert.IsTrue(created["mcp"]!["registered"]!.GetValue<bool>());

        var previous = UserEntry(await File.ReadAllTextAsync(configFile, ct)).ToJsonString();
        // A project override shares the server name. Only the user-level value may be replaced.
        var fixture = $$"""
            {
              "projects": { "other": { "mcpServers": { "rodel-minecraft": { "command": "project.exe" } } } },
              "mcpServers": {
                "rodel\u002dminecraft" : {{previous}},
                "neighbor": { "type": "stdio", "command": "neighbor.exe", "args": [], "timeout": 120, "extra": {"keep": true} }
              }
            }
            """.ReplaceLineEndings("\r\n");
        await File.WriteAllTextAsync(configFile, fixture, ct);
        var reject = Path.Combine(Directory.CreateDirectory(destination.Root).FullName, "reject-claude-add");
        await File.WriteAllTextAsync(reject, "simulate Claude refusing the new entry", ct);

        var failed = await ApplyAsync(destination, requestFile, "update_owned", expectedExitCode: 1);
        Assert.AreEqual("partial", failed["status"]!.GetValue<string>());
        Assert.AreEqual("failed", failed["mcp"]!["status"]!.GetValue<string>());
        Assert.AreEqual(55, failed["mcp"]!["details"]!["exitCode"]!.GetValue<int>());
        var afterFailure = await File.ReadAllTextAsync(configFile, ct);
        Assert.AreEqual(fixture, afterFailure);
        var status = (await original.RunAsync("ai", "integration", "status", "--file", requestFile)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(status["mcpRegistered"]!.GetValue<bool>());
        await VerifyBindingAsync(UserEntry(afterFailure), original.Root);

        File.Delete(reject);
        var updated = await ApplyAsync(destination, requestFile, "update_owned");
        Assert.IsTrue(updated["mcp"]!["registered"]!.GetValue<bool>());
        var afterRetry = await File.ReadAllTextAsync(configFile, ct);
        var newEntry = UserEntry(afterRetry);
        Assert.AreEqual(destination.Root, newEntry["args"]![1]!.GetValue<string>());
        Assert.AreEqual(fixture.Replace(previous, newEntry.ToJsonString(), StringComparison.Ordinal), afterRetry);
    }

    private static JsonObject UserEntry(string config)
        => JsonNode.Parse(config)!["mcpServers"]!["rodel-minecraft"]!.AsObject();

    private static async Task VerifyBindingAsync(JsonObject entry, string expectedRoot)
    {
        var command = entry["command"]!.GetValue<string>();
        var start = new ProcessStartInfo(command) { WorkingDirectory = Path.GetDirectoryName(command)! };
        foreach (var argument in entry["args"]!.AsArray())
            start.ArgumentList.Add(argument!.GetValue<string>());
        Assert.AreEqual(expectedRoot, start.ArgumentList[1]);

        await using var session = await McpSession.ConnectAsync(start);
        var result = await session.CallAsync("tool_call", new JsonObject
        {
            ["name"] = "config_get",
            ["arguments"] = new JsonObject { ["key"] = "language" }
        });
        Assert.AreEqual("succeeded", result["status"]!.GetValue<string>());
    }

    private static async Task<JsonNode> ApplyAsync(CliSandbox cli, string request, string action, int expectedExitCode = 0)
    {
        var plan = (await cli.RunAsync("ai", "integration", "plan", "--file", request)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(action, plan["mcpAction"]!.GetValue<string>());
        var apply = await cli.RunAsync("ai", "integration", "apply", "--file", request,
            "--fingerprint", plan["fingerprint"]!.GetValue<string>());
        return apply.ReadEnvelope(expectedExitCode)["data"]!;
    }

    /// <summary>Gets or sets the deadline for isolated integration operations.</summary>
    public TestContext TestContext { get; set; }
}