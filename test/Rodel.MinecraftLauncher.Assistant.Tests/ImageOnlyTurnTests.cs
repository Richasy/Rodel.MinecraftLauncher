using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects image-only composer submissions at the real Core admission boundary.</summary>
[TestClass]
public sealed class ImageOnlyTurnTests
{
    /// <summary>Existing protocol image tests begin after request admission and cannot catch rejecting an empty
    /// text draft with a valid attachment. This case reaches actual model HTTP and durable turn reads; an empty
    /// draft without attachments must still reject before provider dispatch.</summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ImageOnlyDraft_PreservesOriginalAttachment_WhileEmptyDraftRemainsInvalid()
    {
        using var sandbox = new AiSandbox();
        JsonObject? wire = null;
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            wire = await ModelEndpoint.ReadRequestAsync(context);
            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"I see the image."},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await new AiServiceStore(sandbox.Paths).SaveServiceAsync(new()
        {
            Protocol = AiProtocols.OpenAiChat,
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new() { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        const string encoded = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";
        var image = await new AiImageStore(sandbox.Paths).PrepareAsync(Convert.FromBase64String(encoded), "pasted.png", TestContext.CancellationToken);
        await using var session = new ChatSession(sandbox);
        var conversation = await session.InvokeAsync("chat.create", [], TestContext.CancellationToken);
        var request = new AiChatSend
        {
            ConversationId = conversation["id"]!.ToString(),
            ExpectedRevision = 1,
            ClientKey = Guid.NewGuid().ToString("N"),
            Prompt = string.Empty,
            UseTools = false,
            ImageIds = [image.Id]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync(request with { ImageIds = [] }, TestContext.CancellationToken));
        Assert.IsNull(wire);
        var finished = await session.FinishAsync(await session.SendAsync(request, TestContext.CancellationToken), TestContext.CancellationToken);
        Assert.AreEqual("completed", finished["status"]!.ToString(), finished.ToJsonString());
        Assert.AreEqual(string.Empty, finished["prompt"]!.ToString());
        Assert.AreEqual(image.Id, finished["images"]![0]!["id"]!.ToString());
        var parts = wire!["messages"]!.AsArray().Last(message => message?["role"]?.ToString() == "user")!["content"]!.AsArray();
        Assert.AreEqual("data:image/png;base64," + encoded, parts.Single(part => part?["type"]?.ToString() == "image_url")!["image_url"]!["url"]!.ToString());
    }

    /// <summary>Gets or sets cancellation for the isolated turn and loopback model.</summary>
    public TestContext TestContext { get; set; }
}