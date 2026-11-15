using Microsoft.AspNetCore.Http;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects AI CLI input and observation contracts through real child processes/Core, with only HTTP model replies replaced.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AssistantWorkflowTests
{
    /// <summary>
    /// Typed read reports must preserve future fields, explicit nulls and noncanonical persisted values across
    /// the actual host RPC and CLI serializer. Existing generation tests use canonical records and cannot detect
    /// lossy projection or strict DTO deserialization here; no model/provider is needed for this read-only contract.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ReadReports_PreserveUnknownNullAndNoncanonicalRecords_AcrossHostAndCli()
    {
        await using var session = await AssistantCliSession.StartAsync();
        var conversationId = Guid.NewGuid().ToString("N");
        var turnId = Guid.NewGuid().ToString("N");
        var conversation = new JsonObject
        {
            ["schemaVersion"] = "future-schema",
            ["id"] = conversationId,
            ["revision"] = 1,
            ["title"] = null,
            ["createdUtc"] = "2026-10-20T00:00:00Z",
            ["updatedUtc"] = "2026-10-20T00:00:01Z",
            ["future"] = new JsonObject { ["nested"] = new JsonArray(13, null, "keep") }
        };
        var turn = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = turnId,
            ["conversationId"] = conversationId,
            ["status"] = "failed",
            ["modelId"] = null,
            ["title"] = new JsonArray("noncanonical"),
            ["ownership"] = "host_owned",
            ["prompt"] = "Read evidence without replay.",
            ["images"] = new JsonArray("future-image-reference"),
            ["createdUtc"] = "2026-10-20T00:00:00Z",
            ["intents"] = new JsonArray(new JsonObject { ["state"] = "unknown_effect", ["future"] = 71 }),
            ["grant"] = new JsonObject { ["state"] = "expired", ["unknown"] = true },
            ["wireHistory"] = new JsonArray("private-provider-state"),
            ["providerState"] = new JsonObject { ["opaque"] = true }
        };
        var directory = Path.Combine(session.Cli.Root, "ai", "conversations", conversationId);
        Directory.CreateDirectory(Path.Combine(directory, "turns"));
        await File.WriteAllTextAsync(Path.Combine(directory, "conversation.json"), conversation.ToJsonString(), TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "turns", turnId + ".json"), turn.ToJsonString(), TestContext.CancellationToken);

        var page = (await session.Cli.RunAsync("ai", "chat", "list", "--offset", "0", "--limit", "1")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(1, page["total"]!.GetValue<int>());
        Assert.IsFalse(page["hasMore"]!.GetValue<bool>());
        Assert.IsTrue(JsonNode.DeepEquals(conversation, page["items"]![0]));
        var shownTurn = (await session.Cli.RunAsync("ai", "chat", "turn", "show", conversationId, turnId)).ReadEnvelope(0)["data"]!;
        turn.Remove("wireHistory");
        turn.Remove("providerState");
        Assert.IsTrue(JsonNode.DeepEquals(turn, shownTurn), "Readback must retain grant/intent/ownership evidence without private provider state.");
        var shown = (await session.Cli.RunAsync("ai", "chat", "show", conversationId)).ReadEnvelope(0)["data"]!;
        conversation["turns"] = new JsonArray(turn.DeepClone());
        Assert.IsTrue(JsonNode.DeepEquals(conversation, shown));

        var extensions = JsonNode.Parse("""{"schemaVersion":"future","revision":null,"mcp":[],"fileRoots":null,"skillDirectories":[],"unknown":{"preserve":true}}""")!;
        await File.WriteAllTextAsync(Path.Combine(session.Cli.Root, "ai", "extensions.json"), extensions.ToJsonString(), TestContext.CancellationToken);
        var registrations = (await session.Cli.RunAsync("ai", "mcp", "list")).ReadEnvelope(0)["data"];
        // Effective grants always include the immutable data root; unknown/null source fields must still survive on disk.
        var effectiveExtensions = extensions.DeepClone();
        effectiveExtensions["fileRoots"] = new JsonArray(new JsonObject { ["id"] = "builtin:data", ["path"] = session.Cli.Root, ["mode"] = "read_write" });
        Assert.IsTrue(JsonNode.DeepEquals(effectiveExtensions, registrations));
        Assert.IsTrue(JsonNode.DeepEquals(extensions, JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(session.Cli.Root, "ai", "extensions.json"), TestContext.CancellationToken))));
    }

    /// <summary>Context-only updates must pass CLI admission without an unrelated title or permission flag.
    /// This exercises a separate command boundary from Core scope tests and detects rejection before typed dispatch.</summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ConversationContextOnlyChanges_ReachCore_WithoutUnrelatedFields()
    {
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(new Uri("http://127.0.0.1:1/"));
        var instance = (await session.Cli.RunAsync("instance", "create", "Context fixture", "--version", "synthetic")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var created = (await session.Cli.RunAsync("ai", "chat", "create", "--generate-title")).ReadEnvelope(0)["data"]!;
        var id = created["id"]!.ToString();
        Assert.AreEqual("pending", created["titleSource"]!.ToString());
        var bound = (await session.Cli.RunAsync("ai", "chat", "update", id, "--expected-revision", "1", "--instance", instance)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(instance, bound["instanceId"]!.ToString());
        var cleared = (await session.Cli.RunAsync("ai", "chat", "update", id, "--expected-revision", "2", "--clear-instance")).ReadEnvelope(0)["data"]!;
        Assert.IsNull(cleared["instanceId"]);
        Assert.AreEqual(3L, cleared["revision"]!.GetValue<long>());
    }

    /// <summary>
    /// Automation needs a bounded one-shot answer without permanently editing a player's model defaults.
    /// Default and explicitly selected service requests carry temporary parameters on the real wire and preserve configuration bytes.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task OneShotOverrides_ReachProvider_WithoutChangingSavedModelDefaults(bool explicitService)
    {
        JsonObject? wire = null;
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            calls++;
            wire = await ModelEndpoint.ReadRequestAsync(context);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"choices":[{"message":{"content":"Bounded answer."},"finish_reason":"stop"}]}""", context.RequestAborted);
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var state = (await session.Cli.RunAsync("ai", "service", "list")).ReadEnvelope(0)["data"]!;
        var service = state["services"]![0]!;
        var serviceId = service["id"]!.ToString();
        (await session.Cli.RunAsync("ai", "model", "save", "--service", serviceId, "--id", service["models"]![0]!["id"]!.ToString(),
            "--expected-revision", "1", "--temperature", "0.8", "--max-output-tokens", "128")).ReadEnvelope(0);
        var configuration = Path.Combine(session.Cli.Root, "ai", "services.json");
        var before = await File.ReadAllBytesAsync(configuration, TestContext.CancellationToken);
        string[] command = explicitService ? ["ai", "request", "--service", serviceId] : ["ai", "request"];
        var result = (await session.Cli.RunAsync([.. command, "--prompt", "Bound this answer.", "--temperature", "0", "--max-output-tokens", "32"])).ReadEnvelope(0)["data"]!;
        Assert.IsNotNull(wire);
        Assert.AreEqual(0d, wire["temperature"]!.GetValue<double>());
        Assert.AreEqual(32, wire["max_completion_tokens"]!.GetValue<int>());
        Assert.AreEqual(32, result["effectiveParameters"]!["maxOutputTokens"]!.GetValue<int>());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(configuration, TestContext.CancellationToken));
        var invalid = (await session.Cli.RunAsync([.. command, "--prompt", "Do not dispatch.", "--max-output-tokens", "0"])).ReadEnvelope(2);
        Assert.AreEqual("INVALID_ARGUMENT", invalid["error"]!["code"]!.ToString());
        Assert.AreEqual(1, calls);
    }

    /// <summary>
    /// Piped Unicode text is prompt data, not command tokens. Conflicting prompt sources and MCP stdin must reject before
    /// another model dispatch; otherwise automation may send unintended text or consume the protocol stream.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task PromptStdin_PreservesLiteralText_AndRejectsAmbiguousOrMcpInput(bool persistent)
    {
        JsonObject? request = null;
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            calls++;
            request = await ModelEndpoint.ReadRequestAsync(context);
            if (persistent)
            {
                await ModelEndpoint.SseAsync(context,
                [
                    """{"choices":[{"index":0,"delta":{"content":"Input accepted."},"finish_reason":"stop"}]}""", "[DONE]"
                ]);
            }
            else
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"choices":[{"message":{"content":"Input accepted."},"finish_reason":"stop"}]}""", context.RequestAborted);
            }
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        string[] command = ["ai", "request", "--prompt-stdin"];
        if (persistent)
        {
            var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
            command = ["ai", "chat", "send", conversation, "--expected-revision", "1", "--prompt-stdin", "--no-tools", "--wait"];
        }

        const string prompt = "读取日志，不修改。\n--background --data-dir \"不是参数\"";
        var sent = await CliSandbox.RunAsync(session.Cli.CreateStartInfo(command), prompt, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual("Input accepted.", sent.ReadEnvelope(0)["data"]![persistent ? "answer" : "text"]!.ToString());
        Assert.IsNotNull(request);
        Assert.AreEqual(prompt, request["messages"]!.AsArray().Last(item => item!["role"]!.ToString() == "user")!["content"]!.GetValue<string>());
        var rejected = (await session.Cli.RunAsync([.. command, "--prompt", "wrong"])).ReadEnvelope(2);
        Assert.AreEqual("INVALID_ARGUMENT", rejected["error"]!["code"]!.ToString());
        var oversized = (await CliSandbox.RunAsync(session.Cli.CreateStartInfo(command),
            new string('x', 64001), cancellationToken: TestContext.CancellationToken)).ReadEnvelope(2);
        Assert.AreEqual("INVALID_ARGUMENT", oversized["error"]!["code"]!.ToString());
        await using var mcp = await McpSession.ConnectAsync(session.Cli);
        var unsafeInput = await mcp.CallAsync("execute", new JsonObject
        {
            ["arguments"] = new JsonArray([.. command.Select(value => (JsonNode?)JsonValue.Create(value))])
        });
        Assert.AreEqual("INVALID_ARGUMENT", unsafeInput["error"]!["code"]!.ToString());
        Assert.AreEqual(1, calls);
    }

    /// <summary>
    /// An observer may time out and later resume after a durable cursor without canceling or resubmitting the actual turn.
    /// A real open HTTP response and persisted text event establish the active boundary; the one-second wait is the feature under test.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task WaitExistingTurn_TimeoutDoesNotCancel_AndCursorResumeDoesNotReplay()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            calls++;
            await ModelEndpoint.SseAsync(context,
            [
                new JsonObject { ["choices"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["index"] = 0, ["delta"] = new JsonObject { ["content"] = new string('看', 600) }
                }) }.ToJsonString()
            ]);
            await release.Task.WaitAsync(context.RequestAborted);
            await ModelEndpoint.SseAsync(context,
            [
                """{"choices":[{"index":0,"delta":{"content":" done"},"finish_reason":"stop"}]}""", "[DONE]"
            ]);
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var receipt = (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Observe only.", "--no-tools")).ReadEnvelope(0)["data"]!;
        var id = receipt["id"]!.ToString();
        var visible = await session.EventAsync(conversation, id, "text_delta", TestContext.CancellationToken);
        var cursor = visible["sequence"]!.GetValue<long>();
        var timedOut = (await session.Cli.RunAsync("ai", "chat", "turn", "wait", conversation, id, "--timeout-seconds", "1")).ReadEnvelope(1);
        Assert.AreEqual("AI_WAIT_TIMEOUT", timedOut["error"]!["code"]!.ToString());
        var live = (await session.Cli.RunAsync("ai", "chat", "turn", "show", conversation, id)).ReadEnvelope(0)["data"]!;
        Assert.AreNotEqual("canceled", live["status"]!.ToString());
        release.SetResult();
        var resumed = await session.Cli.RunAsync("ai", "chat", "turn", "wait", conversation, id, "--after", cursor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--events", "--timeout-seconds", "10");
        Assert.AreEqual(0, resumed.ExitCode, resumed.Output + resumed.Error);
        var completed = JsonNode.Parse(resumed.Output)!["data"]!;
        Assert.AreEqual("completed", completed["status"]!.ToString());
        Assert.IsTrue(completed["answer"]!.ToString().EndsWith(" done", StringComparison.Ordinal));
        var records = resumed.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
        Assert.IsTrue(records.All(item => item["kind"] is not null || item["event"]?.ToString() == "progress"));
        var events = records.Where(item => item["kind"] is not null).ToArray();
        Assert.IsNotEmpty(events);
        Assert.IsTrue(events.All(item => item["sequence"]!.GetValue<long>() > cursor));
        Assert.AreEqual(1, calls);
    }

    /// <summary>
    /// Reused provider call IDs in separate model responses must not suppress later reads or merge their timeline identities.
    /// Unlike the parser cases this crosses the actual model/Core/launcher-stdio/CLI boundary, without player instances or public services.
    /// </summary>
    [TestMethod]
    [Timeout(45000, CooperativeCancellation = true)]
    public async Task RepeatedProviderCallId_PerformsDistinctLauncherReads_AndCompletesToolLoop()
    {
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var body = await ModelEndpoint.ReadRequestAsync(context);
            calls++;
            var results = body["messages"]!.AsArray().Where(item => item!["role"]!.ToString() == "tool").ToArray();
            if (results.Length < 2)
            {
                await ModelEndpoint.SseAsync(context,
                [
                    """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"reused-provider-id","function":{"name":"instance_list","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""", "[DONE]"
                ]);
            }
            else
            {
                Assert.IsTrue(results.All(item => JsonNode.Parse(item!["content"]!.GetValue<string>())!["status"]!.ToString() == "succeeded"));
                await ModelEndpoint.SseAsync(context,
                [
                    """{"choices":[{"index":0,"delta":{"content":"Two actual launcher reads."},"finish_reason":"stop"}]}""", "[DONE]"
                ]);
            }
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var sent = (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Read instances twice.", "--wait")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("Two actual launcher reads.", sent["answer"]!.ToString());
        var events = (await session.Cli.RunAsync("ai", "chat", "turn", "events", conversation, sent["id"]!.ToString())).ReadEnvelope(0)["data"]!["items"]!.AsArray();
        var reads = events.Where(item => item!["kind"]!.ToString() == "tool_finished" && item["data"]!["name"]!.ToString() == "instance_list").ToArray();
        Assert.HasCount(2, reads);
        Assert.AreEqual(2, reads.Select(item => item!["stepId"]!.ToString()).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(reads.All(item => item!["data"]!["result"]!["operation"]!.ToString() == "instance_list"));
        Assert.AreEqual(3, calls);
    }

    /// <summary>
    /// One canonical update must change only selected conversation fields in one revision, without resetting other access.
    /// Stale and empty patches must preserve the committed record instead of silently performing a second edit.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ConversationUpdate_MergesFieldsInOneRevision_AndRejectsStaleOrEmptyEdits()
    {
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(new Uri("http://127.0.0.1:1/"));
        var created = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!;
        var id = created["id"]!.ToString();
        var changed = (await session.Cli.RunAsync("ai", "chat", "update", id, "--expected-revision", "1", "--title", "One coherent edit",
            "--mode", "allow_operations", "--files", "false", "--network", "true")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(2L, changed["revision"]!.GetValue<long>());
        Assert.AreEqual("One coherent edit", changed["title"]!.ToString());
        Assert.IsFalse(changed["fileAccessEnabled"]!.GetValue<bool>());
        Assert.IsTrue(changed["networkAccessEnabled"]!.GetValue<bool>());
        Assert.AreEqual("allow_operations", changed["mode"]!.ToString());
        var file = Path.Combine(session.Cli.Root, "ai", "conversations", id, "conversation.json");
        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var stale = (await session.Cli.RunAsync("ai", "chat", "update", id, "--expected-revision", "1", "--title", "Stale")).ReadEnvelope(1);
        Assert.AreEqual("AI_CONFIGURATION_CONFLICT", stale["error"]!["code"]!.ToString());
        (await session.Cli.RunAsync("ai", "chat", "update", id, "--expected-revision", "2")).ReadEnvelope(2);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>
    /// Provider requests and discovery must survive source-generated polymorphism through real CLI/Core host transport,
    /// including NativeAOT when selected. Direct parser tests cannot detect trimmed subtype fields or changed null/omission
    /// semantics at this boundary; fixed independent wire fixtures cover each protocol without accounts or tool execution.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat")]
    [DataRow("openai-responses")]
    [DataRow("anthropic-messages")]
    [DataRow("gemini")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ProviderProbeAndDiscovery_PreserveProtocolShapeAndNullableResults(string protocol)
    {
        JsonObject? wire = null;
        var discoveries = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            if (HttpMethods.IsGet(context.Request.Method))
            {
                Interlocked.Increment(ref discoveries);
                var directory = protocol == "gemini"
                    ? """{"models":[{"name":"fixture","displayName":"Fixture model"}]}"""
                    : """{"data":[{"id":"fixture","display_name":"Fixture model"}],"has_more":false}""";
                await context.Response.WriteAsync(directory, context.RequestAborted);
                return;
            }

            wire = await ModelEndpoint.ReadRequestAsync(context);
            context.Response.Headers["x-request-id"] = "fixture-request";
            await context.Response.WriteAsync(ProbeResponse(protocol), context.RequestAborted);
        }, TestContext.CancellationToken, [HttpMethods.Get, HttpMethods.Post]);
        await using var session = await AssistantCliSession.StartAsync();
        (await session.Cli.RunAsync("ai", "service", "save", "--protocol", protocol,
            "--base-url", endpoint.Endpoint.AbsoluteUri, "--model", "fixture")).ReadEnvelope(0);
        var services = (await session.Cli.RunAsync("ai", "service", "list")).ReadEnvelope(0)["data"]!;
        var serviceId = services["services"]![0]!["id"]!.ToString();
        var configuration = Path.Combine(session.Cli.Root, "ai", "services.json");
        var before = await File.ReadAllBytesAsync(configuration, TestContext.CancellationToken);
        var result = (await session.Cli.RunAsync("ai", "request", "--prompt", "保持原文😀",
            "--temperature", "0", "--max-output-tokens", "32")).ReadEnvelope(0)["data"]!.AsObject();
        Assert.AreEqual("completed", result["status"]!.ToString());
        Assert.AreEqual("保留😀", result["text"]!.ToString());
        Assert.AreEqual("fixture-request", result["requestId"]!.ToString());
        Assert.IsTrue(result.ContainsKey("refusal"));
        Assert.IsNull(result["refusal"]);
        Assert.IsTrue(result.ContainsKey("responseId"));
        Assert.IsNull(result["responseId"]);
        Assert.AreEqual(11L, result["usage"]!["inputTokens"]!.GetValue<long>());
        Assert.AreEqual(3L, result["usage"]!["outputTokens"]!.GetValue<long>());
        if (protocol == "anthropic-messages") Assert.IsNull(result["usage"]!["totalTokens"]);
        else Assert.AreEqual(14L, result["usage"]!["totalTokens"]!.GetValue<long>());
        Assert.AreEqual(0, result["toolsOffered"]!.GetValue<int>());
        Assert.IsFalse(result["persistentConversation"]!.GetValue<bool>());
        Assert.IsNotNull(wire);
        Assert.IsNull(wire["tools"], "An isolated text request must not offer tools.");
        var tokenField = protocol switch
        {
            "openai-chat" => wire["max_completion_tokens"],
            "openai-responses" => wire["max_output_tokens"],
            "anthropic-messages" => wire["max_tokens"],
            _ => wire["generationConfig"]!["maxOutputTokens"]
        };
        Assert.AreEqual(32, tokenField!.GetValue<int>());
        var catalog = (await session.Cli.RunAsync("ai", "service", "models", serviceId)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("fixture", catalog["models"]![0]!["id"]!.ToString());
        Assert.IsTrue(catalog["configuredModelsUnchanged"]!.GetValue<bool>());
        Assert.IsFalse(catalog["generationTested"]!.GetValue<bool>());
        Assert.AreEqual(1, discoveries);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(configuration, TestContext.CancellationToken));
    }

    private static string ProbeResponse(string protocol) => protocol switch
    {
        "openai-chat" => """{"choices":[{"message":{"content":"保留😀"},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":3,"total_tokens":14}}""",
        "openai-responses" => """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"保留😀"}]}],"usage":{"input_tokens":11,"output_tokens":3,"total_tokens":14}}""",
        "anthropic-messages" => """{"content":[{"type":"text","text":"保留😀"}],"stop_reason":"end_turn","usage":{"input_tokens":11,"output_tokens":3}}""",
        _ => """{"candidates":[{"content":{"parts":[{"text":"保留😀"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":3,"totalTokenCount":14}}"""
    };

    /// <summary>
    /// References are immutable turn inputs: changing a selected file after an incomplete response must not alter
    /// its retry or erase images from earlier context. The same client key must recover an ambiguous submission
    /// without generating new attachment IDs, but reject changed bytes. Real CLI/Core/HTTP/storage paths exercise
    /// these recovery boundaries; only model replies are scripted. Four files must reject before provider dispatch.
    /// </summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ImageReferences_FreezeSelectedBytes_AndRetryWithoutReopeningChangedSources()
    {
        var wires = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            wires.Add(await ModelEndpoint.ReadRequestAsync(context));
            await ModelEndpoint.SseAsync(context,
            [
                wires.Count == 1 ? """{"choices":[{"index":0,"delta":{"content":"Partial."},"finish_reason":"length"}]}"""
                    : """{"choices":[{"index":0,"delta":{"content":"Complete."},"finish_reason":"stop"}]}""",
                "[DONE]",
            ]);
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var source = Path.Combine(session.Cli.Root, "reference.png");
        const string encoded = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(encoded), TestContext.CancellationToken);
        string[] images = ["--image", source, "--image", source, "--image", source];
        var clientKey = Guid.NewGuid().ToString("N");
        (await session.Cli.RunAsync(["ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Use the pictures.", .. images, "--image", source, "--no-tools", "--wait"])).ReadEnvelope(2);
        Assert.IsEmpty(wires, "An overfull attachment draft must not dispatch.");
        var firstResult = (await session.Cli.RunAsync(["ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Use the pictures.", "--client-key", clientKey, .. images, "--no-tools", "--wait"])).ReadEnvelope(1);
        Assert.IsNotNull(firstResult["data"], firstResult.ToJsonString());
        var first = firstResult["data"]!;
        Assert.AreEqual("incomplete", first["status"]!.ToString());
        Assert.HasCount(3, first["images"]!.AsArray());
        var turnId = first["id"]!.ToString();
        var recovered = (await session.Cli.RunAsync(["ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Use the pictures.", "--client-key", clientKey, .. images, "--no-tools", "--wait"])).ReadEnvelope(1)["data"]!;
        Assert.AreEqual(turnId, recovered["id"]!.ToString());
        Assert.HasCount(1, wires, "Receipt recovery must not dispatch another model request.");
        var changed = Convert.FromBase64String(encoded);
        changed[^1] ^= 1;
        await File.WriteAllBytesAsync(source, changed, TestContext.CancellationToken);
        var conflict = (await session.Cli.RunAsync(["ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "Use the pictures.", "--client-key", clientKey, .. images, "--no-tools", "--wait"])).ReadEnvelope(1);
        Assert.AreEqual("AI_IDEMPOTENCY_CONFLICT", conflict["error"]!["code"]!.ToString());
        Assert.HasCount(1, wires, "Changed input with the same key must not dispatch or overwrite the original snapshots.");
        var revision = (await session.Cli.RunAsync("ai", "chat", "show", conversation)).ReadEnvelope(0)["data"]!["revision"]!.ToString();
        var retry = (await session.Cli.RunAsync("ai", "chat", "turn", "retry", conversation, turnId, "--expected-revision", revision,
            "--no-tools", "--wait")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", retry["status"]!.ToString());
        Assert.IsTrue(JsonNode.DeepEquals(first["images"], retry["images"]), "Retry must reuse the same managed reference identities.");
        Assert.HasCount(2, wires);
        foreach (var wire in wires)
        {
            var userMessages = wire["messages"]!.AsArray().Where(item => item!["role"]!.ToString() == "user").ToArray();
            Assert.IsNotEmpty(userMessages);
            foreach (var user in userMessages)
            {
                var content = user!["content"]!.AsArray();
                Assert.HasCount(4, content, "Both historical and retried messages retain all three original images.");
                Assert.AreEqual("Use the pictures.", content[0]!["text"]!.ToString());
                foreach (var image in content.Skip(1))
                    Assert.AreEqual("data:image/png;base64," + encoded, image!["image_url"]!["url"]!.ToString());
            }
        }
    }

    /// <summary>
    /// Unlike one-shot parameter tests, persistent CLI chat used to inject 4096 and reject explicit values above 65536.
    /// Observe real chat requests: omitted limits stay omitted for unknown deployments, saved values survive, and a
    /// temporary override is neither clamped by the saved value nor persisted to configuration.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat")]
    [DataRow("openai-responses")]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ChatOutputLimit_OmissionSavedSettingAndExplicitOverrideReachProvider(string protocol)
    {
        var wires = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            wires.Add(await ModelEndpoint.ReadRequestAsync(context));
            var events = protocol == "openai-chat"
                ? stringArray : ["""{"type":"response.completed","response":{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Done."}]}]}}"""];
            await ModelEndpoint.SseAsync(context, events);
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        var configuration = (await session.Cli.RunAsync("ai", "service", "save", "--protocol", protocol,
            "--base-url", endpoint.Endpoint.AbsoluteUri, "--model", "fixture")).ReadEnvelope(0)["data"]!;
        var service = configuration["services"]![0]!;
        var conversation = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var tokenField = protocol == "openai-chat" ? "max_completion_tokens" : "max_output_tokens";

        (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", "1",
            "--prompt", "No configured limit.", "--no-tools", "--wait")).ReadEnvelope(0);
        Assert.IsFalse(wires[0].ContainsKey(tokenField), "The CLI and Core must not invent an output limit.");
        (await session.Cli.RunAsync("ai", "model", "save", "--service", service["id"]!.ToString(), "--id", service["models"]![0]!["id"]!.ToString(),
            "--expected-revision", "1", "--max-output-tokens", "512")).ReadEnvelope(0);
        var file = Path.Combine(session.Cli.Root, "ai", "services.json");
        var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
        var revision = (await session.Cli.RunAsync("ai", "chat", "show", conversation)).ReadEnvelope(0)["data"]!["revision"]!.ToString();
        (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", revision,
            "--prompt", "Use my explicit limit.", "--max-output-tokens", "98304", "--no-tools", "--wait")).ReadEnvelope(0);
        Assert.AreEqual(98304, wires[1][tokenField]!.GetValue<int>());
        revision = (await session.Cli.RunAsync("ai", "chat", "show", conversation)).ReadEnvelope(0)["data"]!["revision"]!.ToString();
        (await session.Cli.RunAsync("ai", "chat", "send", conversation, "--expected-revision", revision,
            "--prompt", "Use my saved setting again.", "--no-tools", "--wait")).ReadEnvelope(0);
        Assert.AreEqual(512, wires[2][tokenField]!.GetValue<int>());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the deadline for owned process and HTTP-boundary scenarios.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] stringArray = ["""{"choices":[{"index":0,"delta":{"content":"Done."},"finish_reason":"stop"}]}""", "[DONE]"];
}