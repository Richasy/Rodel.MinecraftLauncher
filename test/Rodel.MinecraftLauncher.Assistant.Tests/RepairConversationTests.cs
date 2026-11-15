using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Tests;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Exercises the direct Core entry used by App; CLI serialization is deliberately absent.</summary>
[TestClass]
public sealed class RepairConversationTests
{
    /// <summary>Review: CLI RPC converts freshly created numeric JSON values during transport and can hide a broken in-process receipt.
    /// The App entry must create and submit exactly one bound turn without a transport roundtrip. A scripted provider only supplies a
    /// final answer; real Core creation, configuration, admission and persistence remain in use. Reentry must reuse that first turn.</summary>
    [TestMethod]
    public async Task StartRepair_InProcess_CreatesOneBoundTurnAndReusesIt()
    {
        using var sandbox = new AiSandbox(); sandbox.Paths.EnsureDirectories();
        var client = new LauncherClient(sandbox.Paths);
        var instance = await client.Instances.CreateAsync("Direct repair fixture", "fixture", TestContext.CancellationToken);
        var sessionId = Guid.NewGuid().ToString("N");
        await JsonFiles.WriteAsync(Path.Combine(sandbox.Paths.SessionsDirectory, sessionId + ".json"), new JsonObject
        { ["id"] = sessionId, ["instanceId"] = instance.Id, ["gameDirectory"] = instance.GameDirectory, ["status"] = "exited", ["exitCode"] = 1 }, TestContext.CancellationToken);
        var request = new AiRepairCreate { InstanceId = instance.Id, SessionId = sessionId };
        var pending = await client.Assistant.StartRepairAsync(request, ct: TestContext.CancellationToken);
        Assert.IsTrue(pending.NeedsModel);
        Assert.IsNull(pending.ConversationId);
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            _ = await ModelEndpoint.ReadRequestAsync(context);
            await ModelEndpoint.SseAsync(context, ["""{"choices":[{"index":0,"delta":{"content":"Evidence is insufficient."},"finish_reason":"stop"}]}""", "[DONE]"]);
        }, TestContext.CancellationToken);
        await client.Assistant.Services.SaveServiceAsync(new()
        { DisplayName = "Direct fixture", Protocol = AiProtocols.OpenAiChat, BaseUrl = endpoint.Endpoint.AbsoluteUri, Models = [new() { Model = "fixture" }] }, ct: TestContext.CancellationToken);
        var repair = await client.Assistant.StartRepairAsync(request, ct: TestContext.CancellationToken);
        Assert.AreEqual(pending.Id, repair.Id);
        var conversationId = repair.ConversationId!;
        var conversation = await client.Assistant.GetConversationAsync(conversationId, ct: TestContext.CancellationToken);
        Assert.AreEqual(instance.Id, conversation.InstanceId);
        Assert.AreEqual(repair.Id, conversation.RepairId);
        Assert.AreEqual("read_only", conversation.Mode, "The incident must not silently enable general operations.");
        Assert.HasCount(1, conversation.Turns!);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var turn = conversation.Turns![0];
        while (!AiChatStates.IsTerminal(turn.Status))
        {
            await Task.Delay(30, deadline.Token);
            turn = await client.Assistant.GetTurnAsync(conversationId, turn.Id!, ct: deadline.Token);
        }

        var reopened = await client.Assistant.StartRepairAsync(request, ct: deadline.Token);
        Assert.AreEqual(conversationId, reopened.ConversationId);
        Assert.HasCount(1, (await client.Assistant.GetConversationAsync(conversationId, ct: deadline.Token)).Turns!);
    }

    /// <summary>Gets cancellation for this case's actual Core runtime and loopback endpoint.</summary>
    public TestContext TestContext { get; set; } = null!;
}