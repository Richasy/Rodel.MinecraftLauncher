// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects message acceptance during real Core preparation; history hydration is covered separately.</summary>
[TestClass]
public sealed class AiSubmissionTests
{
    /// <summary>Preparation belongs to the response, and Stop before a receipt must target the eventual durable turn.</summary>
    [TestMethod]
    public void FirstSend_ShowsMessageBeforePreparation_AndPromotesOnlyOneStoppedTurn() => StateTestScope.Run(async () =>
    {
        // Review: existing history/configuration cases never send a new draft. Hold the real services lease,
        // inspect immediate UI acceptance, then release it and check real Core history and the permission snapshot.
        // The loopback endpoint has no model server; cancellation and storage remain real, without player data.
        using var scope = new StateTestScope();
        using var configuration = await ConfigureAsync(scope);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        await ui.LoadAsync();
        var instance = await scope.Client.Instances.CreateAsync("Submission fixture", "synthetic", TestContext.CancellationToken);
        ui.NewConversation();
        var conversation = ui.Current;
        await ui.Chats.ChangeContextAsync(conversation, instance.Id, instance: true);
        await ui.Chats.ChangeAccessAsync(conversation, operate: true);
        conversation.Draft = "Install the selected shader";
        Task sending;
        AiUiTurn pending;
        bool responding, cleared, notPersisted, incomplete;
        using (await ResourceLease.AcquireAsync(Path.Combine(scope.Root, "ai", "services.json"), TestContext.CancellationToken))
        {
            sending = ui.Chats.SendAsync(conversation);
            pending = conversation.DisplayTurns.Single();
            responding = conversation.Responding;
            cleared = conversation.Draft.Length == 0;
            notPersisted = !conversation.Persisted;
            incomplete = !sending.IsCompleted;
            conversation.Draft = "My next draft";
            await ui.Chats.StopAsync(conversation);
        }

        await sending.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken);
        await WaitForObservationAsync(conversation);
        Assert.IsTrue(responding && cleared && notPersisted && incomplete, "The original message and response state must precede Core's blocked preparation.");
        Assert.IsNull(conversation.PendingTurn);
        Assert.AreSame(pending, conversation.Turns.Single(), "Receipt promotion must retain one message instead of appending a duplicate.");
        Assert.AreEqual("My next draft", conversation.Draft);
        var saved = await scope.Client.Assistant.GetConversationAsync(conversation.Id, ct: TestContext.CancellationToken);
        var turn = saved.Turns!.Single();
        Assert.AreEqual("Install the selected shader", turn.Prompt);
        Assert.AreEqual(instance.Id, turn.InstanceId);
        Assert.AreEqual("allow_operations", turn.Mode);
        Assert.AreEqual("canceled", turn.Status, "Stop during preparation must reach the exact eventual receipt.");
        Assert.IsFalse(conversation.Responding);
        Assert.IsFalse(ui.Busy);
        Assert.IsEmpty(ui.Error);
    });

    /// <summary>A rejected first message stays readable and retryable without losing a newer draft or duplicating history.</summary>
    [TestMethod]
    public void PreparationFailure_RetainsOriginalMessage_AndRetriesWithoutConsumingTheNewDraft() => StateTestScope.Run(async () =>
    {
        // Review: a real missing-instance rejection occurs after the held services read, before a turn exists.
        // This distinct recovery window catches implementations that clear input early but lose it on failure.
        using var scope = new StateTestScope();
        using var configuration = await ConfigureAsync(scope);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        await ui.LoadAsync();
        ui.NewConversation();
        var conversation = ui.Current;
        conversation.InstanceId = Guid.NewGuid().ToString("N");
        conversation.Draft = "Keep this first message";
        Task sending;
        using (await ResourceLease.AcquireAsync(Path.Combine(scope.Root, "ai", "services.json"), TestContext.CancellationToken))
        {
            sending = ui.Chats.SendAsync(conversation);
            conversation.Draft = "Keep this newer draft too";
        }

        await sending.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CancellationToken);
        var failed = conversation.DisplayTurns.Single();
        Assert.AreEqual("Keep this first message", failed.Record.Prompt);
        Assert.AreEqual("failed", failed.Record.Status);
        Assert.IsNotNull(failed.Record.Error);
        Assert.IsFalse(conversation.Responding);
        await ui.Chats.SendAsync(conversation, failed);
        Assert.AreSame(failed, conversation.DisplayTurns.Single());
        Assert.AreEqual("Keep this newer draft too", conversation.Draft);
        Assert.IsEmpty(conversation.Turns, "A preparation rejection cannot invent a persisted Core turn.");
        Assert.IsFalse(conversation.Persisted);
        Assert.IsFalse(ui.Busy);
        Assert.IsEmpty(ui.Error, "The failed message owns its error; it must not also produce a duplicate page error.");
    });

    private static async Task<AiConfigurationStore> ConfigureAsync(StateTestScope scope)
    {
        var configuration = new AiConfigurationStore(scope.Client,
            readIdentity: (_, _, _) => Task.FromResult(new AiModelIdentity(null, null, null)));
        Assert.IsTrue(await configuration.SaveServiceAsync(null, "fixture", AiProtocols.OpenAiChat, "http://127.0.0.1:9/",
            string.Empty, [new(string.Empty, "Fixture", "fixture")], true, true));
        await configuration.ArtworkCompletion;
        return configuration;
    }

    private async Task WaitForObservationAsync(AiUiConversation conversation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!conversation.Turns.Single().ObservedTerminal)
            await Task.Delay(20, deadline.Token);
    }

    /// <summary>Gets or sets cancellation for real isolated storage and response observation.</summary>
    public TestContext TestContext { get; set; }
}