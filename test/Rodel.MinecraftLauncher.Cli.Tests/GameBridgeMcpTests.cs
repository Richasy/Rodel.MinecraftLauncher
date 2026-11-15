// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Crosses actual stdio MCP, command symbols and a separately owned Core monitor. Only the Java game peer is substituted.</summary>
[TestClass]
public sealed class GameBridgeMcpTests
{
    /// <summary>Review: exercises actual MEAI, world observation and stdio MCP with a nonempty native-key array. Separate deserialization must preserve exact request value identity, and an applied input receipt must not claim a gameplay objective was observed.</summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task LocalInput_RealMeaiAndMcp_PreservesNestedValueIdentityAndEffect()
    {
        await using var session = await AssistantCliSession.StartAsync();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, session.Cli.Root);
        var edit = fixture.Edit() with { Kind = "player.control", Health = null, Control = new() { Keys = ["forward", "sneak"], Ticks = 2 } };
        var writes = 0;
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method != "game.edit") return fixture.Response(request);
            var native = request.Params!.Value.Deserialize(GameBridgeJsonContext.Default.GameEditRequest)!;
            CollectionAssert.AreEqual(_expectedKeys, native.Control!.Keys);
            Interlocked.Increment(ref writes);
            return new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(new GameOperation { OperationId = edit.OperationId, State = "applied" }, GameBridgeJsonContext.Default.GameOperation) };
        }, TestContext.CancellationToken);
        var calls = 0;
        JsonNode? editResult = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var wire = await ModelEndpoint.ReadRequestAsync(context);
            var step = Interlocked.Increment(ref calls);
            if (step > 2)
            {
                editResult = JsonNode.Parse(wire["messages"]!.AsArray().Last(item => item?["role"]?.ToString() == "tool")!["content"]!.ToString());
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"The local edit has an applied receipt."},"finish_reason":"stop"}]}""", "[DONE]"]);
                return;
            }

            var arguments = new JsonObject { ["sessionId"] = fixture.Session };
            if (step == 2) arguments["request"] = JsonSerializer.SerializeToNode(edit, GameBridgeJsonContext.Default.GameEditRequest);
            var delta = new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = new JsonObject
                    {
                        ["tool_calls"] = new JsonArray(new JsonObject
                        {
                            ["index"] = 0,
                            ["id"] = "bridge-" + step,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = step == 1 ? "game_snapshot" : "game_edit", ["arguments"] = arguments.ToJsonString() }
                        })
                    },
                    ["finish_reason"] = "tool_calls"
                })
            };
            await ModelEndpoint.SseAsync(context, [delta.ToJsonString(), "[DONE]"]);
        }, TestContext.CancellationToken);
        await session.ConfigureAsync(endpoint.Endpoint);
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var turn = (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", "1", "--allow-operations", "--max-turns", "3", "--prompt", "Observe the selected game, then hold forward and sneak for two ticks in its owned local world.", "--wait")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", turn["status"]?.ToString(), turn.ToJsonString());
        Assert.AreEqual(3, calls);
        Assert.AreEqual(1, writes);
        Assert.AreEqual("applied", editResult?["data"]?["state"]?.ToString(), editResult?.ToJsonString());
        var observed = (await session.Cli.RunAsync("process", "game", "operation", "show", fixture.Session, edit.OperationId)).ReadEnvelope(0);
        Assert.AreEqual("applied", observed["data"]!["state"]!.ToString());
        var verification = turn["intents"]!.AsArray().Single(item => item?["name"]?.ToString() == "game_edit")!["verification"]!;
        Assert.IsTrue(verification["effectResolved"]!.GetValue<bool>());
        Assert.IsFalse(verification["desiredStateObserved"]!.GetValue<bool>(), "Native dispatch completion is not an observed movement objective.");
    }

    /// <summary>Review: a named MCP edit must traverse the canonical CLI and preserve the server read-only guard. Comparing its durable receipt through a separate CLI process catches a facade-only refusal or incorrect target mapping.</summary>
    [TestMethod]
    public async Task RemoteEdit_RealMcpAndSeparateCli_ObserveOneRejectedReceipt()
    {
        using var cli = new CliSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, cli.Root);
        var writes = 0;
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.edit") Interlocked.Increment(ref writes);
            return fixture.Response(request, "remote_read_only");
        }, TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);
        var snapshot = await mcp.CallAsync("game_snapshot", new() { ["sessionId"] = fixture.Session });
        Assert.AreEqual("remote_read_only", snapshot["data"]?["mode"]?.ToString());
        var edit = fixture.Edit();
        var result = await mcp.CallAsync("game_edit", new()
        {
            ["sessionId"] = fixture.Session,
            ["request"] = JsonSerializer.SerializeToNode(edit, GameBridgeJsonContext.Default.GameEditRequest)
        });
        Assert.AreEqual("succeeded", result["status"]?.ToString(), result.ToJsonString());
        Assert.AreEqual("rejected", result["data"]?["state"]?.ToString());
        Assert.AreEqual("SERVER_READ_ONLY", result["data"]?["errorCode"]?.ToString());
        var observed = (await cli.RunAsync("process", "game", "operation", "show", fixture.Session, edit.OperationId)).ReadEnvelope(0);
        Assert.IsTrue(JsonNode.DeepEquals(result["data"], observed["data"]));
        Assert.AreEqual(0, writes, "No remote mutation may reach the peer through either entry point.");
    }

    /// <summary>Review: omitting a required world binding must fail before transport and intent persistence, even when a live bridge exists. This checks the generated nested schema and actual deserialization rather than inspecting schema text alone.</summary>
    [TestMethod]
    [DataRow("missing_world")]
    [DataRow("unknown_kind")]
    [DataRow("health_range")]
    [DataRow("control_timing")]
    [DataRow("mixed_payload")]
    [DataRow("operation_case")]
    public async Task InvalidTypedEdit_McpRejectsBeforeDispatchOrIntent(string invalid)
    {
        using var cli = new CliSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, cli.Root);
        var edits = 0;
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.edit") Interlocked.Increment(ref edits);
            return fixture.Response(request);
        }, TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);
        var request = JsonSerializer.SerializeToNode(fixture.Edit(), GameBridgeJsonContext.Default.GameEditRequest)!.AsObject();
        if (invalid == "missing_world")
        {
            request.Remove("worldEpoch");
        }
        else if (invalid == "unknown_kind")
        {
            request["kind"] = "game.chat.send";
        }
        else if (invalid == "control_timing") { request.Remove("health"); request["kind"] = "player.control"; request["control"] = new JsonObject { ["ticks"] = 3, ["interactionDelayTicks"] = 5 }; }
        else if (invalid == "operation_case")
        {
            request["operationId"] = "ABCDEF0123456789ABCDEF0123456789";
        }
        else if (invalid == "mixed_payload") { request["kind"] = "command.execute"; request["command"] = "time query daytime"; }
        else
        {
            request["health"] = 21;
        }

        var failure = await mcp.CallAsync("game_edit", new() { ["sessionId"] = fixture.Session, ["request"] = request });
        Assert.AreEqual("failed", failure["status"]?.ToString());
        Assert.AreEqual(0, edits);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Paths.SessionsDirectory, fixture.Session + ".bridge-operations")));
    }

    /// <summary>Review: omitted defaults and a Unicode knowledge search must survive source-generated MCP conversion and the real CLI route. The peer checks the effective input lease; unavailable book data must retain its reason rather than becoming an invented empty catalog.</summary>
    [TestMethod]
    public async Task SemanticQueryAndInput_OmittedDefaults_CrossActualMcpAndCli()
    {
        using var cli = new CliSandbox();
        await using var fixture = await BridgeFixture.CreateAsync(TestContext.CancellationToken, cli.Root);
        var queries = 0; var edits = 0;
        using var data = JsonDocument.Parse("""{"yaw":12}""");
        using var unavailable = JsonDocument.Parse("""{"available":false,"provider":"patchouli","reason":"api_not_available"}""");
        await using var peer = await fixture.ConnectAsync(request =>
        {
            if (request.Method == "game.query")
            {
                var query = request.Params!.Value.Deserialize(GameBridgeJsonContext.Default.GameQueryRequest)!;
                Assert.AreEqual(50, query.Limit);
                if (query.Kind == "books")
                {
                    Assert.AreEqual("混合饲料", query.Search);
                    Assert.AreEqual("fixture:guide", query.Resource);
                }

                Interlocked.Increment(ref queries);
                return new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(new GameQueryResult { SessionId = fixture.Session, GameIncarnation = query.GameIncarnation, WorldEpoch = query.WorldEpoch, Dimension = query.Dimension, SampledAt = DateTimeOffset.UtcNow.ToString("O"), Kind = query.Kind, Source = "client_synchronized", Data = query.Kind == "books" ? unavailable.RootElement : data.RootElement }, GameBridgeJsonContext.Default.GameQueryResult) };
            }

            if (request.Method == "game.edit")
            {
                var edit = request.Params!.Value.Deserialize(GameBridgeJsonContext.Default.GameEditRequest)!;
                Assert.AreEqual(1, edit.Control!.Ticks);
                Assert.IsEmpty(edit.Control.Keys);
                Interlocked.Increment(ref edits);
                return new BridgeRpc { Id = request.Id, Result = JsonSerializer.SerializeToElement(new GameOperation { OperationId = edit.OperationId, State = "applied" }, GameBridgeJsonContext.Default.GameOperation) };
            }

            return fixture.Response(request);
        }, TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);
        var request = JsonSerializer.SerializeToNode(fixture.Edit(), GameBridgeJsonContext.Default.GameEditRequest)!.AsObject();
        request.Remove("health"); request.Remove("operationId"); request["kind"] = "player";
        var observed = await mcp.CallAsync("game_query", new() { ["sessionId"] = fixture.Session, ["request"] = request.DeepClone() });
        Assert.AreEqual("succeeded", observed["status"]?.ToString(), observed.ToJsonString());
        Assert.AreEqual(12, observed["data"]!["data"]!["yaw"]!.GetValue<int>());
        var knowledge = request.DeepClone().AsObject();
        knowledge["kind"] = "books"; knowledge["search"] = "混合饲料"; knowledge["resource"] = "fixture:guide";
        var book = await mcp.CallAsync("game_query", new() { ["sessionId"] = fixture.Session, ["request"] = knowledge });
        Assert.AreEqual("succeeded", book["status"]?.ToString(), book.ToJsonString());
        Assert.IsFalse(book["data"]!["data"]!["available"]!.GetValue<bool>());
        Assert.AreEqual("api_not_available", book["data"]!["data"]!["reason"]!.GetValue<string>());
        request["kind"] = "player.control"; request["operationId"] = Guid.NewGuid().ToString("N"); request["control"] = new JsonObject { ["yaw"] = 12 };
        var result = await mcp.CallAsync("game_edit", new() { ["sessionId"] = fixture.Session, ["request"] = request });
        Assert.AreEqual("applied", result["data"]?["state"]?.ToString(), result.ToJsonString());
        Assert.AreEqual(2, queries); Assert.AreEqual(1, edits);
    }

    /// <summary>Gets the test runner's cancellation context for owned transports and subprocesses.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] _expectedKeys = ["forward", "sneak"];
}