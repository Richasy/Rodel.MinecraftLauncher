using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Crosses the official MEAI loop, real Core skill guards, durable events and self-authored HTTP protocols.</summary>
[TestClass]
public sealed class AgentLoopProtocolTests
{
    /// <summary>
    /// Two model-requested tools must be executed by MEAI, persisted separately, and sent back with exact correlation
    /// and native signatures. Single-call parser cases cannot detect batched-result assumptions, InformationalOnly
    /// invalidating continuation, or returning intermediate assistant text as the final answer. Rejection after real
    /// tools also proves compaction updates MEAI's actual working history without replay or aggregate-history revival.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat", false)]
    [DataRow("openai-responses", false)]
    [DataRow("anthropic-messages", false)]
    [DataRow("gemini", false)]
    [DataRow("openai-responses", true)]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task TwoSkills_ContinueThroughOfficialLoop_WithNativeStateAndDurableResults(string protocol, bool recover)
    {
        using var sandbox = new AiSandbox();
        var requests = new List<JsonObject>();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var body = await ModelEndpoint.ReadRequestAsync(context);
            if (recover)
            {
                var excerpts = body["input"]!.AsArray().Where(item =>
                    item?["content"]?.ToString().StartsWith("EXCERPT ", StringComparison.Ordinal) == true).ToArray();
                if (excerpts.Length > 0)
                {
                    var omitted = excerpts.Select(item => JsonNode.Parse(
                        item!["content"]!.ToString().Split('\n')[0]["EXCERPT ".Length..])!["id"]!.DeepClone()).ToArray();
                    var selection = new JsonObject { ["keep"] = new JsonArray(), ["omit"] = new JsonArray(omitted) }.ToJsonString();
                    var frame = JsonNode.Parse(Answer(protocol)[0])!;
                    frame["response"]!["output"]![0]!["content"]![0]!["text"] = selection;
                    await ModelEndpoint.SseAsync(context, [frame.ToJsonString()]);
                    return;
                }
            }

            requests.Add(body);
            if (recover && requests.Count == 2)
            {
                await ModelEndpoint.SseAsync(context,
                    ["""{"type":"response.failed","response":{"error":{"code":"context_length_exceeded","message":"Controlled post-tool context rejection."}}}"""]);
                return;
            }

            await ModelEndpoint.SseAsync(context, requests.Count == 1 ? Calls(protocol) : Answer(protocol));
        }, TestContext.CancellationToken);
        var conversation = await ConfigureAsync(sandbox, protocol, endpoint.Endpoint);
        var background = string.Concat(Enumerable.Repeat("Previous nonessential explanation. ", 400));
        if (recover)
        {
            var earlier = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
            earlier["status"] = "completed";
            earlier["wireHistory"] = new JsonArray(
                new JsonObject { ["role"] = "user", ["text"] = "Inspect only; do not change settings." },
                new JsonObject { ["role"] = "assistant", ["text"] = background });
            await sandbox.Store.SaveTurnAsync(earlier, TestContext.CancellationToken);
        }

        await using var session = new ChatSession(sandbox);
        var receipt = await session.SendAsync(Request(conversation) with { MaximumModelCalls = recover ? 8 : 4 }, TestContext.CancellationToken);
        var completed = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("completed", completed["status"]!.ToString(), completed.ToJsonString());
        Assert.AreEqual("FINAL-ANSWER", completed["answer"]!.ToString());
        Assert.HasCount(recover ? 3 : 2, requests);
        var results = Results(protocol, requests[^1]);
        Assert.HasCount(2, results);
        foreach (var result in Results(protocol, requests[1])) Assert.Contains("launcher-basics", result.ToJsonString());
        if (protocol != "openai-chat") Assert.Contains("opaque-loop-signature", requests[^1].ToJsonString());
        if (protocol == "gemini")
        {
            CollectionAssert.AreEqual(expected, results.Select(result => result["id"]!.ToString()).ToArray());
            Assert.IsTrue(results.All(result => result["name"]!.ToString() == "skill_read"));
        }

        var events = await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken);
        Assert.HasCount(2, events["items"]!.AsArray().Where(item => item!["kind"]!.ToString() == "tool_finished").ToArray());
        var saved = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), receipt["id"]!.ToString(), true);
        var history = saved["wireHistory"]!.AsArray();
        Assert.HasCount(2, history.Where(item => item!["role"]!.ToString() == "tool").ToArray());
        if (recover)
        {
            Assert.DoesNotContain(background, requests[^1].ToJsonString(), "The retry must use the compacted MEAI working list.");
            Assert.DoesNotContain(background, history.ToJsonString(), "MEAI's aggregate response must not revive archived history.");
            CollectionAssert.AreEqual(expected, results.Select(result => result["call_id"]!.ToString()).ToArray());
            var archive = new AiContextArchives(sandbox.Paths).Load(conversation["id"]!.ToString(), saved["contextCheckpoint"]!["archiveId"]!.ToString());
            Assert.Contains(item => item?["text"]?.ToString() == background, archive.Messages);
            Assert.HasCount(2, archive.Messages.Where(item => item?["role"]?.ToString() == "tool").ToArray());
        }

        var previous = AiChatHistory.Read(history).First(message => message.Contents.OfType<FunctionCallContent>().Any());
        var state = AiChatMessages.Native(previous);
        Assert.IsNotNull(state);
        foreach (var call in previous.Contents.OfType<FunctionCallContent>()) call.InformationalOnly = true;
        Assert.IsNotNull(AiChatMessages.Native(previous), "MEAI's handled flag is not a provider-content edit.");
        previous.Contents.Add(new TextContent("Actual content edit."));
        Assert.IsNull(AiChatMessages.Native(previous), "Real content edits must still invalidate signed state.");
    }

    /// <summary>
    /// MEAI normally permits a final tool-free request after its iteration limit. The real Core shared gate must stop
    /// before that extra HTTP request; completed tool results stay durable, not replayed to manufacture a final answer.
    /// </summary>
    [TestMethod]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task SharedBudget_PreventsExtraFinalModelRequest_WithoutLosingCompletedTools()
    {
        using var sandbox = new AiSandbox();
        var requests = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            await ModelEndpoint.SseAsync(context, Calls("openai-chat"));
        }, TestContext.CancellationToken);
        var conversation = await ConfigureAsync(sandbox, "openai-chat", endpoint.Endpoint);
        await using var session = new ChatSession(sandbox);
        var receipt = await session.SendAsync(Request(conversation) with { MaximumModelCalls = 2 }, TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual("incomplete", result["status"]!.ToString(), result.ToJsonString());
        Assert.AreEqual("AI_MODEL_CALL_BUDGET_EXCEEDED", result["error"]!["code"]!.ToString());
        Assert.AreEqual(2, requests);
        var saved = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), receipt["id"]!.ToString(), true);
        Assert.HasCount(4, saved["wireHistory"]!.AsArray().Where(item => item!["role"]!.ToString() == "tool").ToArray());
    }

    /// <summary>Incomplete/refused provider calls remain observations, never executable tools in the official driver.</summary>
    [TestMethod]
    [DataRow("openai-chat", "length", "incomplete")]
    [DataRow("openai-responses", "max_output_tokens", "incomplete")]
    [DataRow("openai-chat", "stop", "refused")]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task NonterminalModelReply_DoesNotDispatchItsCompleteToolArguments(string protocol, string finish, string status)
    {
        using var sandbox = new AiSandbox();
        var requests = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            var frames = Calls(protocol);
            var frame = JsonNode.Parse(frames[0])!;
            if (protocol == "openai-responses")
            {
                frame["type"] = "response.incomplete";
                frame["response"]!["status"] = "incomplete";
                frame["response"]!["incomplete_details"] = new JsonObject { ["reason"] = finish };
            }
            else
            {
                frame["choices"]![0]!["finish_reason"] = finish;
                if (status == "refused") frame["choices"]![0]!["delta"]!["refusal"] = "Request refused.";
            }

            frames[0] = frame.ToJsonString();
            await ModelEndpoint.SseAsync(context, frames);
        }, TestContext.CancellationToken);
        var conversation = await ConfigureAsync(sandbox, protocol, endpoint.Endpoint);
        await using var session = new ChatSession(sandbox);
        var receipt = await session.SendAsync(Request(conversation), TestContext.CancellationToken);
        var result = await session.FinishAsync(receipt, TestContext.CancellationToken);
        Assert.AreEqual(status, result["status"]!.ToString(), result.ToJsonString());
        if (status == "incomplete")
        {
            Assert.AreEqual("AI_OUTPUT_TOKEN_LIMIT", result["error"]!["code"]!.ToString());
            Assert.AreEqual(finish, result["finishReason"]!.ToString());
        }

        Assert.AreEqual(1, requests);
        var events = await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken);
        Assert.DoesNotContain(item => item!["kind"]!.ToString() == "tool_started", events["items"]!.AsArray());
    }

    /// <summary>Unknown names and repeated invocation IDs receive audited failures without bypassing the same Core guard.</summary>
    [TestMethod]
    [DataRow(false, "AI_TOOL_NOT_OFFERED", 0)]
    [DataRow(true, "AI_DUPLICATE_TOOL_CALL", 1)]
    [Timeout(20000, CooperativeCancellation = true)]
    public async Task InvalidCalls_ReturnAuditedFailures_WithoutDuplicateSkillDispatch(bool duplicate, string code, int skillReads)
    {
        using var sandbox = new AiSandbox();
        var requests = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            if (Interlocked.Increment(ref requests) > 1)
            {
                await ModelEndpoint.SseAsync(context, Answer("openai-chat"));
                return;
            }

            var frames = Calls("openai-chat");
            var frame = JsonNode.Parse(frames[0])!;
            var calls = frame["choices"]![0]!["delta"]!["tool_calls"]!.AsArray();
            if (duplicate) calls[1]!["id"] = "call-1";
            else foreach (var call in calls) call!["function"]!["name"] = "unoffered";
            frames[0] = frame.ToJsonString();
            await ModelEndpoint.SseAsync(context, frames);
        }, TestContext.CancellationToken);
        var conversation = await ConfigureAsync(sandbox, "openai-chat", endpoint.Endpoint);
        await using var session = new ChatSession(sandbox);
        var receipt = await session.SendAsync(Request(conversation), TestContext.CancellationToken);
        Assert.AreEqual("completed", (await session.FinishAsync(receipt, TestContext.CancellationToken))["status"]!.ToString());
        var saved = sandbox.Store.GetTurnDocument(conversation["id"]!.ToString(), receipt["id"]!.ToString(), true);
        Assert.Contains(code, saved["wireHistory"]!.ToJsonString());
        var events = (await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken))["items"]!.AsArray();
        Assert.HasCount(skillReads, events.Where(item => item!["kind"]!.ToString() == "skill_started" && item["stepId"]!.ToString() != "core-skill").ToArray());
        Assert.AreEqual(2, requests);
    }

    private async Task<JsonObject> ConfigureAsync(AiSandbox sandbox, string protocol, Uri endpoint)
    {
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new()
        {
            Protocol = protocol,
            BaseUrl = endpoint.AbsoluteUri,
            Models = [new() { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        await using var setup = new ChatSession(sandbox);
        var created = await setup.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var conversation = sandbox.Store.GetDocument(created["id"]!.ToString());
        conversation["fileAccessEnabled"] = false;
        conversation["networkAccessEnabled"] = false;
        await sandbox.Store.SaveConversationAsync(conversation, TestContext.CancellationToken);
        return conversation;
    }

    private static AiChatSend Request(JsonObject conversation) => new()
    {
        ConversationId = conversation["id"]!.ToString(),
        ExpectedRevision = conversation["revision"]!.GetValue<long>(),
        ClientKey = Guid.NewGuid().ToString("N"),
        Prompt = "Read the enabled built-in skill twice.",
        MaximumModelCalls = 4
    };

    private static string[] Calls(string protocol)
    {
        const string args = """{"id":"builtin:launcher-basics"}""";
        if (protocol == "openai-chat")
        {
            var calls = new JsonArray(Enumerable.Range(1, 2).Select(index => (JsonNode)new JsonObject
            {
                ["index"] = index - 1,
                ["id"] = "call-" + index,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "skill_read", ["arguments"] = args }
            }).ToArray());
            return [new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["tool_calls"] = calls }, ["finish_reason"] = "tool_calls" })
            }.ToJsonString(), "[DONE]"];
        }

        if (protocol == "openai-responses")
            return ["""{"type":"response.completed","response":{"status":"completed","output":[{"type":"reasoning","encrypted_content":"opaque-loop-signature"},{"type":"function_call","call_id":"call-1","name":"skill_read","arguments":"{\"id\":\"builtin:launcher-basics\"}"},{"type":"function_call","call_id":"call-2","name":"skill_read","arguments":"{\"id\":\"builtin:launcher-basics\"}"}]}}"""];
        if (protocol == "anthropic-messages")
        {
            return
            [
                """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"Read context.","signature":"opaque-loop-signature"}}""",
                """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"call-1","name":"skill_read","input":{"id":"builtin:launcher-basics"}}}""",
                """{"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"call-2","name":"skill_read","input":{"id":"builtin:launcher-basics"}}}""",
                """{"type":"message_delta","delta":{"stop_reason":"tool_use"}}""", """{"type":"message_stop"}"""
            ];
        }

        return ["""{"candidates":[{"content":{"parts":[{"thought":true,"text":"Read context.","thoughtSignature":"opaque-loop-signature"},{"functionCall":{"id":"call-1","name":"skill_read","args":{"id":"builtin:launcher-basics"}}},{"functionCall":{"id":"call-2","name":"skill_read","args":{"id":"builtin:launcher-basics"}}}]},"finishReason":"STOP"}]}"""];
    }

    private static string[] Answer(string protocol) => protocol switch
    {
        "openai-chat" => ["""{"choices":[{"index":0,"delta":{"content":"FINAL-ANSWER"},"finish_reason":"stop"}]}""", "[DONE]"],
        "openai-responses" => ["""{"type":"response.completed","response":{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"FINAL-ANSWER"}]}]}}"""],
        "anthropic-messages" => ["""{"type":"content_block_start","index":0,"content_block":{"type":"text","text":"FINAL-ANSWER"}}""", """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""", """{"type":"message_stop"}"""],
        _ => ["""{"candidates":[{"content":{"parts":[{"text":"FINAL-ANSWER"}]},"finishReason":"STOP"}]}"""]
    };

    private static JsonNode[] Results(string protocol, JsonObject wire) => protocol switch
    {
        "openai-chat" => [.. wire["messages"]!.AsArray().Where(item => item!["role"]!.ToString() == "tool").Select(item => item!)],
        "openai-responses" => [.. wire["input"]!.AsArray().Where(item => item?["type"]?.ToString() == "function_call_output").Select(item => item!)],
        "anthropic-messages" => [.. wire["messages"]!.AsArray().SelectMany(item => item!["content"]!.AsArray()).Where(item => item?["type"]?.ToString() == "tool_result").Select(item => item!)],
        _ => [.. wire["contents"]!.AsArray().SelectMany(item => item!["parts"]!.AsArray()).Where(item => item?["functionResponse"] is not null).Select(item => item!["functionResponse"]!)]
    };

    /// <summary>Gets or sets the bounded model/turn test context.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["call-1", "call-2"];
}