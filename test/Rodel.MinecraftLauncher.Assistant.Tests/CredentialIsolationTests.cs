using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects credential handling across real protected storage, Core event metadata and private provider-state validation.</summary>
[TestClass]
public sealed class CredentialIsolationTests
{
    /// <summary>
    /// A short credential must not corrupt host-owned identifiers or protocol/status fields; a genuine provider echo must still fail.
    /// Synthetic DPAPI-protected input replaces no persistence behavior and never opens a real account.
    /// </summary>
    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task ShortCredential_PreservesHostMetadata_ButRejectsProviderStateEcho()
    {
        using var sandbox = new AiSandbox();
        var calls = 0;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            var text = Interlocked.Increment(ref calls) == 1 ? "42" : "e";
            await ModelEndpoint.SseAsync(context,
            [
                new JsonObject { ["choices"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["index"] = 0, ["delta"] = new JsonObject { ["content"] = text }, ["finish_reason"] = "stop"
                }) }.ToJsonString(), "[DONE]"
            ]);
        }, TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new AiServiceUpdate
        {
            Protocol = AiProtocols.OpenAiChat,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new AiModelUpdate { Model = "fixture" }]
        }, secret: "e", ct: TestContext.CancellationToken);
        var conversation = (await session.InvokeAsync("chat.create", [], TestContext.CancellationToken))["id"]!.ToString();
        AiChatSend Request(long revision) => new()
        {
            ConversationId = conversation,
            ClientKey = Guid.NewGuid().ToString("N"),
            ExpectedRevision = revision,
            Prompt = "42",
            UseTools = false
        };
        var first = await session.SendAsync(Request(1), TestContext.CancellationToken);
        Assert.AreEqual("completed", (await session.FinishAsync(first, TestContext.CancellationToken))["status"]!.ToString());
        var page = await session.InvokeAsync("chat.events", ChatSession.Reference(first), TestContext.CancellationToken);
        var events = page["items"]!.AsArray();
        var model = events.Single(item => item!["kind"]!.ToString() == "model_started")!;
        Assert.AreEqual("openai-chat", model["data"]!["protocol"]!.ToString());
        var finished = events.Single(item => item!["kind"]!.ToString() == "turn_finished")!;
        Assert.AreEqual("completed", finished["data"]!["status"]!.ToString());
        Assert.IsTrue(events.Where(item => item!["kind"]!.ToString() is "message_started" or "text_delta")
            .All(item => item!["data"]!["parentStepId"]!.ToString() == model["stepId"]!.ToString()));

        var second = await session.SendAsync(Request(2), TestContext.CancellationToken);
        var rejected = await session.FinishAsync(second, TestContext.CancellationToken);
        Assert.AreEqual("failed", rejected["status"]!.ToString());
        Assert.AreEqual("AI_CREDENTIAL_IN_PROVIDER_STATE", rejected["error"]!["code"]!.ToString());
        Assert.DoesNotContain("[r[r", rejected["error"]!["message"]!.ToString());
    }

    /// <summary>Gets or sets the deadline for synthetic secret leases and actual HTTP cancellation.</summary>
    public TestContext TestContext { get; set; }
}