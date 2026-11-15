using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects provider-neutral typed output through our real HTTP/SSE clients, not a supplier SDK or fake client.</summary>
[TestClass]
public sealed class MeaiStructuredOutputTests
{
    /// <summary>
    /// A model that returns fenced JSON must work without native schema support, hidden prompt augmentation or retries.
    /// Four actual wire protocols exercise MEAI typed parsing with our source-generated contract; caller packet/schema
    /// budgeting and semantic ID validation remain separately covered by the Core recovery/checkpoint cases.
    /// </summary>
    [TestMethod]
    [DataRow("openai-chat")]
    [DataRow("openai-responses")]
    [DataRow("anthropic-messages")]
    [DataRow("gemini")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task TypedSelection_UsesSelfAuthoredClient_WithoutNativeSchemaRequirement(string protocol)
    {
        var requests = 0;
        JsonObject? wire = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            wire = await ModelEndpoint.ReadRequestAsync(context);
            await ModelEndpoint.SseAsync(context, Frames(protocol, "```json\n{\"keep\":[0],\"omit\":[1]}\n```"));
        }, TestContext.CancellationToken);
        var response = await AiModelStream.RunStructuredAsync(
            new(protocol, 1, "binding", "fixture", null, 0, 256), endpoint.Endpoint,
            [new ChatMessage(ChatRole.User, "已预算的schema和待选择原文")], 256,
            AiContextJsonContext.Default.AiContextSelection, TestContext.CancellationToken);
        Assert.AreEqual("completed", AiChatMessages.Status(response));
        Assert.IsTrue(response.TryGetResult(out var result));
        CollectionAssert.AreEqual(expectedKeep, result.Keep);
        CollectionAssert.AreEqual(expectedOmit, result.Omit);
        Assert.AreEqual(1, requests, "No implicit schema fallback or parse retry may issue another model request.");
        Assert.IsNotNull(wire);
        Assert.IsNull(wire["response_format"]);
        Assert.IsNull(wire["output_config"]);
        Assert.IsNull(wire["text"], "Responses native schema configuration is not required for prompt-based selection.");
        Assert.IsNull(wire["tools"]);
        Assert.IsNull(wire["generationConfig"]?["responseJsonSchema"]);
        var contents = protocol == "openai-responses" ? wire["input"] : protocol == "gemini" ? wire["contents"] : wire["messages"];
        Assert.HasCount(1, contents!.AsArray(), "Structured parsing must not append an unbudgeted extra message.");
    }

    private static string[] Frames(string protocol, string text)
    {
        var encoded = JsonValue.Create(text)!.ToJsonString();
        return protocol switch
        {
            "openai-chat" =>
            [
                $$$"""{"choices":[{"index":0,"delta":{"content":{{{encoded}}}},"finish_reason":"stop"}]}""", "[DONE]"
            ],
            "openai-responses" =>
            [
                $$$"""{"type":"response.completed","response":{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":{{{encoded}}}}]}]}}"""
            ],
            "anthropic-messages" =>
            [
                $$$"""{"type":"content_block_start","index":0,"content_block":{"type":"text","text":{{{encoded}}}}}""",
                """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""", """{"type":"message_stop"}"""
            ],
            _ =>
            [
                $$$"""{"candidates":[{"content":{"parts":[{"text":{{{encoded}}}}]},"finishReason":"STOP"}]}"""
            ]
        };
    }

    /// <summary>Gets or sets the bounded HTTP test cancellation context.</summary>
    public TestContext TestContext { get; set; }

    private static readonly int[] expectedKeep = [0];
    private static readonly int[] expectedOmit = [1];
}