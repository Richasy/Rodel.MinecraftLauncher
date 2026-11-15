using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>
/// Protects provider-to-canonical streaming and secret boundaries using actual HTTP/framing and fixed synthetic wire data.
/// These tests do not establish compatibility with a real provider account or that a model selects a useful tool.
/// </summary>
[TestClass]
public sealed class ProviderStreamTests
{
    /// <summary>
    /// A provider redirect must not send another request or expose its credential to the redirected endpoint.
    /// Two controlled HTTP servers exercise our redirect policy; a failure before reaching the first server cannot satisfy the test.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ProviderRedirect_IsNotFollowed_AndNeverForwardsCredential()
    {
        var redirected = 0;
        var reached = false;
        await using var target = await ModelEndpoint.StartAsync(context =>
        {
            Interlocked.Increment(ref redirected);
            return context.Response.WriteAsync("Unexpected request.", context.RequestAborted);
        }, TestContext.CancellationToken);
        await using var source = await ModelEndpoint.StartAsync(context =>
        {
            reached = true;
            context.Response.Redirect(target.Endpoint.AbsoluteUri);
            return Task.CompletedTask;
        }, TestContext.CancellationToken);
        await Assert.ThrowsAsync<AiProtocolException>(async () => await AiProviderRequests.ExecuteAsync(
            Context("openai-chat") with { Credential = "synthetic-redirect-credential" }, source.Endpoint, "Fixture.", 16, TestContext.CancellationToken));
        Assert.IsTrue(reached);
        Assert.AreEqual(0, redirected);
    }

    /// <summary>
    /// Provider-specific envelopes must yield the same complete Unicode answer and executable tool arguments.
    /// Byte fragmentation and opaque signatures distinguish correct assembly from accepting partial chunks or leaking continuation state.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat")]
    [DataRow("openai-responses")]
    [DataRow("anthropic-messages")]
    [DataRow("gemini")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task FragmentedProviderStream_PreservesUnicodeAndCompleteToolCall(string protocol)
    {
        JsonObject? request = null;
        await using var server = await ModelEndpoint.StartAsync(async context =>
        {
            request = await ModelEndpoint.ReadRequestAsync(context);
            await ModelEndpoint.SseAsync(context, Frames(protocol));
        }, TestContext.CancellationToken);
        var deltas = new StringBuilder();
        var reply = await AiModelStream.RunAsync(Context(protocol), server.Endpoint,
            [new ChatMessage(ChatRole.User, "Read the note.")],
            [AiToolDeclarations.Create("read_note", "Read the selected note.", new JsonObject
            {
                ["type"] = "object", ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("path")
            })], 256,
            (kind, text) => { if (kind == "text_delta") deltas.Append(text); return Task.CompletedTask; },
            _ => Task.CompletedTask, TestContext.CancellationToken);

        Assert.IsNotNull(request, "The fixture must be reached; a substituted parser result would not test the HTTP contract.");
        Assert.AreEqual("completed", AiChatMessages.Status(reply));
        Assert.AreEqual("已收到😀", reply.Text);
        Assert.AreEqual("已收到😀", deltas.ToString());
        var calls = reply.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToArray();
        Assert.HasCount(1, calls);
        Assert.AreEqual("read_note", calls[0].Name);
        Assert.AreEqual("内容.txt", AiChatMessages.Arguments(calls[0])["path"]!.GetValue<string>());
        Assert.DoesNotContain("opaque-signature", deltas.ToString());
        if (protocol == "gemini")
            Assert.Contains("opaque-signature", AiChatMessages.Native(reply.Messages.Single())!.Payload.ToJsonString());
    }

    /// <summary>
    /// A stream ending in half a tool argument must fail as protocol data, not promote an incomplete call that an executor could act on.
    /// The fixed malformed fragment reaches the real HTTP reader; no tool or persistence collaborator is replaced by a success result.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task TruncatedToolArguments_AreNotReturnedAsExecutableInput()
    {
        var reached = false;
        await using var server = await ModelEndpoint.StartAsync(async context =>
        {
            reached = true;
            await ModelEndpoint.SseAsync(context,
            [
                """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"half","function":{"name":"read_note","arguments":"{\"path\":\"unfinished"}}]}}]}"""
            ]);
        }, TestContext.CancellationToken);

        var error = await Assert.ThrowsAsync<AiProtocolException>(async () => await AiModelStream.RunAsync(Context("openai-chat"),
            server.Endpoint, [new ChatMessage(ChatRole.User, "Read.")], [], 256,
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask, TestContext.CancellationToken));
        Assert.IsTrue(reached);
        Assert.AreEqual("AI_RESPONSE_INTERRUPTED", error.Code);
    }

    /// <summary>
    /// A provider echo must redact the credential without corrupting similarly named business fields.
    /// This guards the former authorization-field scrub defect, not arbitrary redactor implementation details.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ProviderEcho_RedactsCredential_WithoutChangingAuthorizationBusinessData()
    {
        const string credential = "synthetic-provider-key-78542";
        var text = new JsonObject
        {
            ["unchangedReviewRestoresAuthorization"] = false,
            ["api-key-policy"] = "read_only",
            ["diagnostic"] = "Authorization: Bearer " + credential
        }.ToJsonString();
        await using var server = await ModelEndpoint.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new JsonObject
            {
                ["choices"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["message"] = new JsonObject { ["content"] = text },
                    ["finish_reason"] = "stop"
                })
            }.ToJsonString(), context.RequestAborted);
        }, TestContext.CancellationToken);

        var result = await AiProviderRequests.ExecuteAsync(Context("openai-chat") with { Credential = credential },
            server.Endpoint, "Fixture.", 10, TestContext.CancellationToken);
        Assert.DoesNotContain(credential, JsonSerializer.Serialize(result, AssistantJsonContext.Default.AiProviderResult));
        var observed = JsonNode.Parse(((AiTextResult)result).Text)!;
        Assert.IsFalse(observed["unchangedReviewRestoresAuthorization"]!.GetValue<bool>());
        Assert.AreEqual("read_only", observed["api-key-policy"]!.GetValue<string>());
    }

    private static AiProviderContext Context(string protocol) => new(protocol, 1, "fixture-binding", "fixture", null, 0, 256);

    /// <summary>
    /// Complete tool batches must keep their result IDs and actual images through persistence and HTTP projection.
    /// A filename-only result, dropped binary content, or a user image inserted before all OpenAI tool replies must
    /// fail. A Gemini provider switch separately guards against losing the tool name with discarded opaque state.
    /// The tiny byte sequences are opaque transport fixtures, not decoded pictures.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat", false)]
    [DataRow("openai-responses", false)]
    [DataRow("anthropic-messages", false)]
    [DataRow("gemini", false)]
    [DataRow("gemini", true)]
    public void ToolImages_PreserveCompletedBatchOrderAndCorrelation_AcrossStoredHistory(string protocol, bool switched)
    {
        var context = Context(protocol);
        var source = switched ? context with { ModelId = "previous-binding" } : context;
        var assistant = new ChatMessage(ChatRole.Assistant,
            [AiChatMessages.Call("call-1", "render_skin", new JsonObject()), AiChatMessages.Call("call-2", "render_skin", new JsonObject())]);
        var stored = new JsonArray(AiChatHistory.Write(assistant));
        for (var index = 1; index <= 2; index++)
        {
            AiMcpContent[] images = [new("image") { Data = [(byte)index, 2, 3], MediaType = "image/png", ByteLength = 3 }];
            var envelope = new JsonObject
            {
                ["status"] = "succeeded",
                ["data"] = new JsonObject { ["path"] = "not-opened.png" },
                ["imageContent"] = JsonSerializer.SerializeToNode(images, AssistantJsonContext.Default.AiMcpContentArray)
            };
            var effects = new JsonObject
            {
                ["data"] = new JsonObject { ["intents"] = new JsonArray(new JsonObject { ["result"] = envelope.DeepClone() }) }
            };
            Assert.IsNull(AiChatMessages.VisibleToolResult(effects)["data"]!["intents"]![0]!["result"]!["imageContent"]![0]!["data"],
                "Effects observations must not leak nested image bodies into visible events.");
            var result = AiChatMessages.ToolResult("call-" + index, envelope);
            AiChatMessages.SetToolName(result, "render_skin");
            AiChatMessages.SetProviderCallId(result, "provider-" + index);
            var message = AiChatMessages.ToolMessage(result);
            AiChatMessages.Bind(message, new(source.Protocol, source.ModelId, source.ServiceRevision, new JsonObject()));
            stored.Add((JsonNode)AiChatHistory.Write(message));
        }

        var history = AiChatMessages.ForProvider(context, AiChatHistory.Read(JsonNode.Parse(stored.ToJsonString())!.AsArray())).ToArray();
        var wire = JsonSerializer.SerializeToNode(AiChatWire.Build(context, history, [], null),
            AssistantJsonContext.Default.AiProviderRequest)!;
        Assert.DoesNotContain("pendingToolImages", wire.ToJsonString(), "An invocation carrier must not become provider text or duplicate image data.");
        if (protocol is "openai-chat" or "openai-responses")
        {
            var input = wire[protocol == "openai-chat" ? "messages" : "input"]!.AsArray();
            var outputs = input.Select((item, index) => (Item: item!, Index: index))
                .Where(item => item.Item["role"]?.ToString() == "tool" || item.Item["type"]?.ToString() == "function_call_output").ToArray();
            Assert.HasCount(2, outputs);
            foreach (var output in outputs)
                Assert.AreEqual("not-opened.png", JsonNode.Parse(output.Item[protocol == "openai-chat" ? "content" : "output"]!.ToString())!["data"]!["path"]!.ToString());
            CollectionAssert.AreEqual(expected, outputs.Select(item => item.Item[protocol == "openai-chat" ? "tool_call_id" : "call_id"]!.ToString()).ToArray());
            var observation = input.Last()!;
            Assert.AreEqual("user", observation["role"]!.ToString());
            Assert.IsTrue(outputs.All(item => item.Index < input.Count - 1));
            var blocks = observation["content"]!.AsArray().Where(block => block?["type"]?.ToString() is "input_image" or "image_url").ToArray();
            Assert.HasCount(2, blocks);
            for (var index = 0; index < blocks.Length; index++)
            {
                var uri = protocol == "openai-chat" ? blocks[index]!["image_url"]!["url"] : blocks[index]!["image_url"];
                Assert.AreEqual("data:image/png;base64," + Convert.ToBase64String([(byte)(index + 1), 2, 3]), uri!.ToString());
            }
        }
        else if (protocol == "anthropic-messages")
        {
            var results = wire["messages"]!.AsArray().Last()!["content"]!.AsArray();
            Assert.HasCount(2, results);
            for (var index = 0; index < results.Count; index++)
            {
                Assert.AreEqual("call-" + (index + 1), results[index]!["tool_use_id"]!.ToString());
                Assert.AreEqual("not-opened.png", JsonNode.Parse(results[index]!["content"]![0]!["text"]!.ToString())!["data"]!["path"]!.ToString());
                Assert.AreEqual(Convert.ToBase64String([(byte)(index + 1), 2, 3]), results[index]!["content"]![1]!["source"]!["data"]!.ToString());
            }
        }
        else
        {
            var parts = wire["contents"]!.AsArray().Last()!["parts"]!.AsArray();
            Assert.HasCount(4, parts);
            for (var index = 0; index < 2; index++)
            {
                var response = parts[index * 2]!["functionResponse"]!;
                Assert.AreEqual("render_skin", response["name"]!.ToString());
                Assert.AreEqual("not-opened.png", response["response"]!["result"]!["data"]!["path"]!.ToString());
                if (switched) Assert.IsNull(response["id"]); else Assert.AreEqual("provider-" + (index + 1), response["id"]!.ToString());
                Assert.AreEqual(Convert.ToBase64String([(byte)(index + 1), 2, 3]), parts[(index * 2) + 1]!["inlineData"]!["data"]!.ToString());
            }
        }
    }

    /// <summary>
    /// Reference bytes must survive actual MEAI JSON persistence and a provider switch, not become mere file names.
    /// Existing text/signature cases cannot detect dropped image contents. The four wire shapes are checked against
    /// the protocol's data-URI/base64 contract; the small distinct byte payloads are opaque transport fixtures, not decoded pictures.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat")]
    [DataRow("openai-responses")]
    [DataRow("anthropic-messages")]
    [DataRow("gemini")]
    public void StoredImages_SurviveProviderSwitch_WithOriginalBytesAndOrder(string protocol)
    {
        string[] types = ["image/png", "image/jpeg", "image/webp"];
        byte[][] bytes = [[1, 2, 3], [4, 5, 6], [7, 8, 9]];
        var message = new ChatMessage(ChatRole.User, "Compare these references.");
        for (var index = 0; index < types.Length; index++)
            message.Contents.Add(new DataContent(bytes[index], types[index]) { Name = "reference-" + index });
        AiChatMessages.Bind(message, new("openai-chat", "previous-binding", 1,
            new JsonObject { ["role"] = "user", ["content"] = "stale provider-only state" }));
        var saved = JsonNode.Parse(new JsonArray(AiChatHistory.Write(message)).ToJsonString())!.AsArray();
        var context = Context(protocol);
        var history = AiChatMessages.ForProvider(context, AiChatHistory.Read(saved)).ToArray();
        var wire = JsonSerializer.SerializeToNode(AiChatWire.Build(context, history, [], null),
            AssistantJsonContext.Default.AiProviderRequest)!;
        Assert.DoesNotContain("stale provider-only state", wire.ToJsonString());
        var content = protocol switch
        {
            "openai-responses" => wire["input"]![0]!["content"]!.AsArray(),
            "gemini" => wire["contents"]![0]!["parts"]!.AsArray(),
            _ => wire["messages"]![0]!["content"]!.AsArray(),
        };
        Assert.HasCount(4, content);
        Assert.AreEqual("Compare these references.", content[0]!["text"]!.ToString());
        for (var index = 0; index < types.Length; index++)
        {
            var part = content[index + 1]!;
            var expectedData = Convert.ToBase64String(bytes[index]);
            if (protocol is "openai-chat" or "openai-responses")
            {
                var uri = protocol == "openai-chat" ? part["image_url"]!["url"] : part["image_url"];
                Assert.AreEqual("data:" + types[index] + ";base64," + expectedData, uri!.ToString());
            }
            else
            {
                var payload = protocol == "gemini" ? part["inlineData"]! : part["source"]!;
                Assert.AreEqual(types[index], payload[protocol == "gemini" ? "mimeType" : "media_type"]!.ToString());
                Assert.AreEqual(expectedData, payload["data"]!.ToString());
            }
        }
    }

    /// <summary>
    /// Complete MEAI contents must survive real JSON persistence without exposing protected state in the archive projection.
    /// Exact-binding reuse preserves unknown provider fields; revision changes or content edits must drop opaque state.
    /// Direct streaming cases cannot detect loss or stale-signature reuse across this durable boundary.
    /// </summary>
    [TestMethod]
    public void StoredHistory_PreservesOpaqueContinuation_WithoutRequiringAssistantFieldsOnUserMessages()
    {
        var assistant = new ChatMessage(ChatRole.Assistant,
            [new TextContent("已答"), new TextReasoningContent("不可公开的推理") { ProtectedData = "opaque-protected" }]);
        AiChatMessages.Bind(assistant, new("openai-chat", "fixture-binding", 1, JsonNode.Parse("""
            {"role":"assistant","content":"已答","provider_extension":{"signed":"opaque-原文"}}
            """)!));
        var stored = new JsonArray(new JsonObject { ["role"] = "user", ["text"] = "保留原文" }, AiChatHistory.Write(assistant));
        var saved = JsonNode.Parse(stored.ToJsonString())!.AsArray();
        var history = AiChatHistory.Read(saved);
        Assert.AreEqual("opaque-protected", history[1].Contents.OfType<TextReasoningContent>().Single().ProtectedData);
        Assert.DoesNotContain("opaque-protected", AiContextArchives.Portable(saved).ToJsonString());
        var wire = JsonSerializer.SerializeToNode(AiChatWire.Build(Context("openai-chat"), history, [], 256),
            AssistantJsonContext.Default.AiProviderRequest)!;
        Assert.AreEqual("保留原文", wire["messages"]![0]!["content"]!.GetValue<string>());
        Assert.AreEqual("opaque-原文", wire["messages"]![1]!["provider_extension"]!["signed"]!.GetValue<string>());
        var switched = JsonSerializer.SerializeToNode(AiChatWire.Build(Context("openai-chat") with { ServiceRevision = 2 }, history, [], 256),
            AssistantJsonContext.Default.AiProviderRequest)!;
        Assert.IsNull(switched["messages"]![1]!["provider_extension"], "Opaque state must not cross a service revision.");
        Assert.AreEqual("已答", switched["messages"]![1]!["content"]!.GetValue<string>());
        var portable = AiChatMessages.ForProvider(Context("openai-chat") with { ServiceRevision = 2 }, history);
        Assert.DoesNotContain(content => content is TextReasoningContent, portable[1].Contents);
        Assert.IsNull(AiChatMessages.Native(portable[1]));
        history[1].Contents.OfType<TextContent>().Single().Text = "已编辑";
        var edited = JsonSerializer.SerializeToNode(AiChatWire.Build(Context("openai-chat"), history, [], 256),
            AssistantJsonContext.Default.AiProviderRequest)!;
        Assert.IsNull(edited["messages"]![1]!["provider_extension"], "Same-binding edits must invalidate the original native snapshot.");
        Assert.AreEqual("已编辑", edited["messages"]![1]!["content"]!.GetValue<string>());
    }

    private static string[] Frames(string protocol) => protocol switch
    {
        "openai-chat" =>
        [
            """{"choices":[{"index":0,"delta":{"content":"已收到😀","tool_calls":[{"index":0,"id":"call-1","function":{"name":"read_note","arguments":"{\"path\":"}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"内容.txt\"}"}}]},"finish_reason":"tool_calls"}]}""",
            "[DONE]"
        ],
        "openai-responses" =>
        [
            """{"type":"response.output_text.delta","delta":"已收到😀"}""",
            """{"type":"response.output_item.added","item":{"type":"function_call","id":"fc-1","call_id":"call-1","name":"read_note","arguments":""}}""",
            """{"type":"response.function_call_arguments.delta","item_id":"fc-1","delta":"{\"path\":\"内容.txt\"}"}""",
            """{"type":"response.completed","response":{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"已收到😀"}]},{"type":"function_call","id":"fc-1","call_id":"call-1","name":"read_note","arguments":"{\"path\":\"内容.txt\"}"}]}}"""
        ],
        "anthropic-messages" =>
        [
            """{"type":"message_start","message":{"id":"msg-1","usage":{"input_tokens":8}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"已收到😀"}}""",
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"call-1","name":"read_note","input":{}}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"内容.txt\"}"}}""",
            """{"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":12}}""",
            """{"type":"message_stop"}"""
        ],
        "gemini" =>
        [
            """{"candidates":[{"content":{"parts":[{"text":"已收到😀"},{"functionCall":{"id":"call-1","name":"read_note","args":{"path":"内容.txt"}},"thoughtSignature":"opaque-signature"}]},"finishReason":"STOP"}]}"""
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };

    /// <summary>Gets or sets MSTest's bounded cancellation context.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] expected = ["call-1", "call-2"];
}