using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects real-time provider reasoning and its distinction from opaque state or token-only evidence.</summary>
[TestClass]
public sealed class ReasoningEventTests
{
    /// <summary>
    /// Opaque state or reported tokens may establish activity, never invented text or leaked continuation.
    /// Final-only Responses output may contain an OpenAI summary or DeepSeek plaintext content; both must reach
    /// the real runner's durable events without relying on a preceding delta.
    /// </summary>
    [TestMethod]
    [DataRow("opaque", false)]
    [DataRow("summary", true)]
    [DataRow("content", true)]
    [DataRow("usage", false)]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ReasoningEvidence_OnlyDisplaysActualProviderText(string evidence, bool displayable)
    {
        const string reasoningText = "Actual provider reasoning.";
        using var sandbox = new AiSandbox();
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            if (evidence == "usage")
            {
                await ModelEndpoint.SseAsync(context,
                [
                    """{"choices":[{"index":0,"delta":{"content":"42"},"finish_reason":"stop"}],"usage":{"prompt_tokens":9,"completion_tokens":21,"total_tokens":30,"completion_tokens_details":{"reasoning_tokens":17}}}""", "[DONE]"
                ]);
            }
            else
            {
                await ModelEndpoint.SseAsync(context,
                [
                    """{"type":"response.output_text.delta","delta":"42"}""",
                    new JsonObject
                    {
                        ["type"] = "response.completed", ["response"] = new JsonObject
                        {
                            ["status"] = "completed",
                            ["output"] = new JsonArray((JsonNode)new JsonObject
                            {
                                ["type"] = "reasoning", ["id"] = "reason-1", ["encrypted_content"] = "opaque-private-state",
                                ["summary"] = evidence == "summary" ? new JsonArray((JsonNode)new JsonObject { ["type"] = "summary_text", ["text"] = reasoningText }) : [],
                                ["content"] = evidence == "content" ? new JsonArray((JsonNode)new JsonObject { ["type"] = "reasoning_text", ["text"] = reasoningText }) : []
                            }, new JsonObject { ["type"] = "message", ["content"] = new JsonArray((JsonNode)new JsonObject { ["type"] = "output_text", ["text"] = "42" }) }),
                            ["usage"] = new JsonObject { ["input_tokens"] = 9, ["output_tokens"] = 21, ["total_tokens"] = 30, ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 17 } }
                        }
                    }.ToJsonString()
                ]);
            }
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = evidence == "usage" ? AiProtocols.OpenAiChat : AiProtocols.OpenAiResponses,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new AiModelUpdate { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        var conversation = (await session.InvokeAsync("chat.create", [], TestContext.CancellationToken))["id"]!.ToString();
        var receipt = await session.SendAsync(new AiChatSend
        {
            ConversationId = conversation,
            ClientKey = Guid.NewGuid().ToString("N"),
            Prompt = "42",
            ExpectedRevision = 1,
            UseTools = false
        }, TestContext.CancellationToken);
        Assert.AreEqual("completed", (await session.FinishAsync(receipt, TestContext.CancellationToken))["status"]!.ToString());
        var page = await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken);
        var events = page["items"]!.AsArray();
        Assert.HasCount(1, events.Where(item => item!["kind"]!.ToString() == "reasoning_started").ToArray());
        var finished = events.Single(item => item!["kind"]!.ToString() == "reasoning_finished")!;
        Assert.AreEqual(displayable, finished["data"]!["displayableContent"]!.GetValue<bool>());
        Assert.AreEqual(displayable ? reasoningText : string.Empty,
            string.Concat(events.Where(item => item!["kind"]!.ToString() == "reasoning_delta").Select(item => item!["data"]!["text"]!.GetValue<string>())));
        Assert.Contains(item => item!["kind"]!.ToString() == "usage" && item["data"]!["reasoningTokens"]!.GetValue<int>() == 17, events);
        Assert.DoesNotContain("opaque-private-state", page.ToJsonString());
    }

    /// <summary>
    /// The provider holds its terminal frame until the test reads an actual durable reasoning delta.
    /// This catches buffering everything until completion, ignoring DeepSeek's plaintext event, and duplicating
    /// streamed reasoning from the final snapshot. The summary variant protects existing OpenAI behavior.
    /// </summary>
    [TestMethod]
    [DataRow("response.reasoning_text.delta", "content", "reasoning_text")]
    [DataRow("response.reasoning_summary_text.delta", "summary", "summary_text")]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ResponsesReasoning_ReachesDurableEventsBeforeCompletion_WithoutDuplicatingFinalText(
        string eventType, string contentField, string contentType)
    {
        using var sandbox = new AiSandbox();
        // Pass ordinary event coalescing without using a sleep to establish the streaming boundary.
        var reasoningText = string.Concat(Enumerable.Repeat("先核对当前图层与可编辑部位。\n", 64));
        var streamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            await ModelEndpoint.SseAsync(context,
            [
                new JsonObject { ["type"] = eventType, ["delta"] = reasoningText }.ToJsonString()
            ]);
            streamed.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            await ModelEndpoint.SseAsync(context,
            [
                new JsonObject
                {
                    ["type"] = "response.completed", ["response"] = new JsonObject
                    {
                        ["status"] = "completed",
                        ["output"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "reasoning",
                            ["encrypted_content"] = "opaque-private-state",
                            [contentField] = new JsonArray(new JsonObject { ["type"] = contentType, ["text"] = reasoningText })
                        }, new JsonObject
                        {
                            ["type"] = "message",
                            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "Ready." })
                        })
                    }
                }.ToJsonString()
            ]);
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new()
        {
            Protocol = AiProtocols.OpenAiResponses,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new() { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        var conversation = (await session.InvokeAsync("chat.create", [], TestContext.CancellationToken))["id"]!.ToString();
        var receipt = await session.SendAsync(new()
        {
            ConversationId = conversation,
            ClientKey = Guid.NewGuid().ToString("N"),
            Prompt = "Check the layers.",
            ExpectedRevision = 1,
            UseTools = false
        }, TestContext.CancellationToken);
        try
        {
            await streamed.Task.WaitAsync(TestContext.CancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var delta = await session.EventAsync(receipt, "reasoning_delta", deadline.Token);
            Assert.AreEqual(reasoningText, delta["data"]!["text"]!.GetValue<string>());
            Assert.IsFalse(release.Task.IsCompleted, "The provider must still be holding its terminal frame.");
            Assert.AreEqual("generating",
                (await session.InvokeAsync("chat.turn", ChatSession.Reference(receipt), TestContext.CancellationToken))["status"]!.ToString());
        }
        finally { release.TrySetResult(); }

        Assert.AreEqual("completed", (await session.FinishAsync(receipt, TestContext.CancellationToken))["status"]!.ToString());
        var page = await session.InvokeAsync("chat.events", ChatSession.Reference(receipt), TestContext.CancellationToken);
        Assert.AreEqual(reasoningText, string.Concat(page["items"]!.AsArray()
            .Where(item => item!["kind"]!.ToString() == "reasoning_delta").Select(item => item!["data"]!["text"]!.GetValue<string>())));
        Assert.DoesNotContain("opaque-private-state", page.ToJsonString());
    }

    /// <summary>Gets or sets the deadline for the actual model stream and event writer.</summary>
    public TestContext TestContext { get; set; }
}