using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects question responses across the real CLI command tree and independently running Core host.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class UserPromptCommandTests
{
    /// <summary>Value/design review: Core cases cannot detect missing command registration, generated metadata,
    /// file input or host serialization. One free-text question crosses all four boundaries; the pending
    /// question is read through RPC before submitting its answer. The model alone is a loopback fixture.</summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ResponseFile_ReachesWaitingHost_WithExactQuestionBinding()
    {
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var input = await ModelEndpoint.ReadRequestAsync(context);
            if (Interlocked.Increment(ref calls) == 1)
            {
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"ask-cli","type":"function","function":{"name":"ask_user","arguments":"{\"question\":\"What style?\",\"options\":[],\"multiple\":false}"}}]},"finish_reason":"tool_calls"}]}""", "[DONE]"]);
            }
            else
            {
                var result = input["messages"]!.AsArray().Single(item => item?["role"]?.ToString() == "tool");
                Assert.AreEqual("ask-cli", result!["tool_call_id"]!.ToString());
                Assert.AreEqual("pixel art", JsonNode.Parse(result["content"]!.ToString())!["data"]!["text"]!.ToString());
                await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Understood."},"finish_reason":"stop"}]}""", "[DONE]"]);
            }
        }, TestContext.CancellationToken);
        await using var session = await AssistantCliSession.StartAsync();
        await session.ConfigureAsync(endpoint.Endpoint);
        var cid = (await session.Cli.RunAsync("ai", "chat", "create")).ReadEnvelope(0)["data"]!["id"]!.ToString();
        var tid = (await session.Cli.RunAsync("ai", "chat", "send", cid, "--expected-revision", "1", "--prompt", "Ask my preference"))
            .ReadEnvelope(0)["data"]!["id"]!.ToString();
        var required = await session.EventAsync(cid, tid, "user_prompt_required", TestContext.CancellationToken);
        var turn = (await session.Cli.RunAsync("ai", "chat", "turn", "show", cid, tid)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("awaiting_user", turn["status"]!.ToString());
        Assert.AreEqual(required["data"]!["id"]!.ToString(), turn["pendingUserPrompt"]!["id"]!.ToString());
        var response = new JsonObject
        {
            ["conversationId"] = cid,
            ["turnId"] = tid,
            ["promptId"] = turn["pendingUserPrompt"]!["id"]!.ToString(),
            ["text"] = "pixel art"
        };
        var file = Path.Combine(session.Cli.Root, "response.json");
        await File.WriteAllTextAsync(file, response.ToJsonString(), TestContext.CancellationToken);
        var accepted = (await session.Cli.RunAsync("ai", "chat", "respond", "--file", file)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("answered", accepted["status"]!.ToString());
        var finished = (await session.Cli.RunAsync("ai", "chat", "turn", "wait", cid, tid)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("completed", finished["status"]!.ToString());
        Assert.AreEqual("Understood.", finished["answer"]!.ToString());
        Assert.AreEqual(2, calls);
    }

    /// <summary>Gets or sets cancellation for the real host test and local HTTP fixture.</summary>
    public TestContext TestContext { get; set; }
}