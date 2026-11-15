// Copyright (c) Richasy. All rights reserved.

using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Tests;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises the real CLI/Core/DPAPI path against a loopback Yggdrasil peer; no public account or game server is contacted.</summary>
[TestClass]
public sealed class ExternalAccountWorkflowTests
{
    /// <summary>
    /// The Core tests substitute HTTP. This distinct process-level case proves stdin-only password input, ALI discovery, cross-command protected
    /// role selection, default preservation and generic refresh dispatch. It would reject a CLI wired to Microsoft refresh or a session handle
    /// that exists only in the first process. Fixed UUIDs and independently inspected HTTP bodies are the protocol oracle.
    /// </summary>
    [TestMethod]
    public async Task PasswordAcrossCommands_SelectsOfferedRoleWithoutExposingCredentials()
    {
        const string first = "0123456789abcdef0123456789abcdef";
        const string second = "fedcba9876543210fedcba9876543210";
        const string password = "fixture-password-from-stdin";
        const string token = "fixture-access-token-private";
        var authenticated = false;
        var selected = false;
        var refreshes = 0;
        var skinReady = false;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var path = context.Request.Path.Value;
            if (path == "/site")
            {
                context.Response.Headers["X-Authlib-Injector-API-Location"] = "/api/";
                await context.Response.WriteAsync("{}", context.RequestAborted); return;
            }

            if (path == "/api/")
            { await context.Response.WriteAsync("""{"meta":{"serverName":"Loopback skin service"},"future":null}""", context.RequestAborted); return; }

            if (path?.Contains("/sessionserver/", StringComparison.Ordinal) == true)
            {
                if (!skinReady) { context.Response.StatusCode = 204; return; }

                Assert.AreEqual("GET", context.Request.Method);
                Assert.IsFalse(context.Request.Headers.ContainsKey("Authorization"));
                await context.Response.WriteAsJsonAsync(new JsonObject
                {
                    ["id"] = second,
                    ["name"] = "SecondRole",
                    ["properties"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "textures",
                        ["value"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"textures":{"SKIN":{"url":"https://skin.test/changed.png"}}}"""))
                    })
                }, context.RequestAborted);
                return;
            }

            var request = await ModelEndpoint.ReadRequestAsync(context);
            if (path == "/api/authserver/authenticate")
            {
                Assert.AreEqual("fixture-user", request["username"]!.GetValue<string>());
                Assert.AreEqual(password, request["password"]!.GetValue<string>());
                authenticated = true;
            }
            else if (path == "/api/authserver/refresh")
            {
                Assert.AreEqual(token, request["accessToken"]!.GetValue<string>());
                if (request["selectedProfile"] is { } role) { Assert.AreEqual(second, role["id"]!.GetValue<string>()); selected = true; }

                refreshes++;
            }
            else { context.Response.StatusCode = 404; return; }

            await context.Response.WriteAsJsonAsync(new JsonObject
            {
                ["accessToken"] = token,
                ["clientToken"] = "fixture-client-token-private",
                ["user"] = new JsonObject { ["id"] = "fixture-owner" },
                ["availableProfiles"] = new JsonArray(new JsonObject { ["id"] = first, ["name"] = "FirstRole" }, new JsonObject { ["id"] = second, ["name"] = "SecondRole" }),
                ["selectedProfile"] = selected ? new JsonObject { ["id"] = second, ["name"] = "SecondRole" } : null
            }, context.RequestAborted);
        }, TestContext.CancellationToken, ["GET", "POST"]);
        using var sandbox = new CliSandbox();
        var offline = (await sandbox.RunAsync("account", "add-offline", "RetainedDefault")).ReadEnvelope(0)["data"]!["id"]!.GetValue<string>();
        _ = (await sandbox.RunAsync("account", "select", offline)).ReadEnvelope(0);
        var login = await CliSandbox.RunAsync(sandbox.CreateStartInfo("account", "external", "login", new Uri(endpoint.Endpoint, "site").AbsoluteUri,
            "--username", "fixture-user", "--password-stdin"), password + "\n", cancellationToken: TestContext.CancellationToken);
        var result = login.ReadEnvelope(0)["data"]!;
        Assert.IsTrue(authenticated);
        Assert.AreEqual("roles", result["stage"]!.GetValue<string>());
        var attempt = result["attemptId"]!.GetValue<string>();
        var complete = (await sandbox.RunAsync("account", "external", "select", attempt, second)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(selected);
        var id = complete["account"]!["id"]!.GetValue<string>();
        Assert.AreEqual(second, complete["account"]!["uuid"]!.GetValue<string>());
        Assert.AreEqual(offline, (await sandbox.RunAsync("account", "current")).ReadEnvelope(0)["data"]!["accountId"]!.GetValue<string>());
        _ = (await sandbox.RunAsync("account", "refresh", id)).ReadEnvelope(0);
        Assert.AreEqual(2, refreshes);
        // Review: extend the existing process journey at its authenticated checkpoint. The skin-only option
        // must use the same Core query without rotating DPAPI credentials or changing the selected offline account.
        var credentialFiles = Directory.GetFiles(Path.Combine(sandbox.Root, "account-secrets"), "*.json");
        var credentialBytes = credentialFiles.ToDictionary(file => file, File.ReadAllBytes);
        skinReady = true;
        var skin = (await sandbox.RunAsync("account", "refresh", id, "--skin-only")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("https://skin.test/changed.png", skin["skinUrl"]!.GetValue<string>());
        Assert.AreEqual(2, refreshes);
        foreach (var file in credentialFiles)
            CollectionAssert.AreEqual(credentialBytes[file], await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        Assert.AreEqual(offline, (await sandbox.RunAsync("account", "current")).ReadEnvelope(0)["data"]!["accountId"]!.GetValue<string>());
        var listed = (await sandbox.RunAsync("account", "list")).ReadEnvelope(0)["data"]!.ToJsonString();
        foreach (var secret in new[] { password, token, "fixture-client-token-private" })
        {
            Assert.IsFalse(login.Output.Contains(secret, StringComparison.Ordinal));
            Assert.IsFalse(complete.ToJsonString().Contains(secret, StringComparison.Ordinal));
            Assert.IsFalse(listed.Contains(secret, StringComparison.Ordinal));
            foreach (var file in Directory.EnumerateFiles(sandbox.Root, "*.json", SearchOption.AllDirectories))
                Assert.IsFalse((await File.ReadAllTextAsync(file, TestContext.CancellationToken)).Contains(secret, StringComparison.Ordinal), file);
        }
    }

    /// <summary>
    /// Review: password stdin belongs to a direct CLI, never the MCP protocol stream. The existing password case cannot detect a server
    /// consuming its next JSON-RPC message as a password. This real stdio request must fail before input or HTTP, and a subsequent request
    /// must still succeed. No credentials are supplied and the bounded transport deadline exposes an accidental read rather than hiding it.
    /// </summary>
    [TestMethod]
    public async Task PasswordStdinThroughMcp_RejectsInputAndKeepsTransportUsable()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli);
        var failure = await mcp.CallAsync("execute", new JsonObject
        {
            ["arguments"] = new JsonArray("account", "external", "login", "http://unused.test/", "--username", "synthetic-user", "--password-stdin")
        });
        Assert.AreEqual("AUTH_INTERACTION_REQUIRED", failure["error"]?["code"]?.ToString());
        var list = await mcp.CallAsync("execute", new JsonObject { ["arguments"] = new JsonArray("account", "list") });
        Assert.AreEqual("succeeded", list["status"]?.ToString());
    }

    /// <summary>Gets the runner's cancellation token for the loopback server and CLI processes.</summary>
    public TestContext TestContext { get; set; } = null!;
}