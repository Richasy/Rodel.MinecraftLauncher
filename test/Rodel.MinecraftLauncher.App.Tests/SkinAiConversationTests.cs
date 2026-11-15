// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Features.Skins;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Tests;
using System.Reflection;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Exercises the skin workspace's real shared chat owner and exact Core removal, without a native window.</summary>
[TestClass]
public sealed class SkinAiConversationTests
{
    /// <summary>
    /// Native validation exposed a late observer reading deleted history into the new chat's error surface.
    /// Hold an actual provider request, wait until the real UI observer has consumed model_started, then
    /// verify its captured lifetime has ended when replacement returns. Reflection observes that existing
    /// task only; no fake read, timer race or replacement implementation is injected.
    /// </summary>
    [TestMethod]
    public void Restart_ActiveChatDrainsObservation_BeforeRemovingItsHistory() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var endpoint = await ModelEndpoint.StartAsync(async context =>
        {
            _ = await ModelEndpoint.ReadRequestAsync(context);
            requested.TrySetResult();
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }, TestContext.CancellationToken);
        await new AiServiceStore(scope.Client.Paths).SaveServiceAsync(new()
        {
            Protocol = "openai-chat",
            BaseUrl = endpoint.Endpoint.AbsoluteUri,
            Models = [new() { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: static (_, _, _) => Task.FromResult(new AiModelIdentity(null, null, null)));
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        Assert.IsTrue(await configuration.LoadAsync());
        var id = await scope.Client.Skins.CreateAsync(SkinModel.Classic, cancellationToken: TestContext.CancellationToken);
        var grant = await scope.Client.Skins.Proposals.CreateScopeAsync(id, new(DirectEditing: true), TestContext.CancellationToken);
        var record = await ui.Service.CreateConversationAsync(new() { SkinScopeId = grant.ScopeId, GenerateTitle = false }, ct: TestContext.CancellationToken);
        await ui.Chats.LoadConversationAsync(record.Id!);
        var previous = ui.Current;
        previous.Draft = "Keep responding until the chat is explicitly replaced";
        await ui.Chats.SendAsync(previous);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await requested.Task.WaitAsync(deadline.Token);
        var turn = previous.Turns.Single();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (!turn.Events.Any(item => item.Kind == "model_started"))
            await timer.WaitForNextTickAsync(deadline.Token);
        var observers = (Dictionary<string, Task>)typeof(AiChatController).GetField("_observers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.Chats)!;
        var observation = observers[turn.Id];
        Assert.IsFalse(observation.IsCompleted);
        var review = await ui.Service.ReviewConversationRemovalAsync(previous.Id, ct: deadline.Token);

        Assert.IsTrue(await new SkinAiWorkspace(id, ui).RestartAsync(previous, review), ui.Error);
        Assert.IsTrue(observation.IsCompleted, "A removed chat must have no remaining UI reader that can report into its replacement.");
        Assert.IsEmpty(ui.Error);
        Assert.IsEmpty(ui.Current.DisplayTurns);
    });

    /// <summary>
    /// Author review: general chat removal does not verify that the focused editor replaces its scoped draft,
    /// or that a failed review leaves the original selected. Use actual persisted history and a skin byte witness;
    /// no model server or user installation is involved. Merely reading the review must not clear the chat.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Restart_ReplacesOnlyReviewedChat_AndPreservesSkin(bool stale) => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        await new AiServiceStore(scope.Client.Paths).SaveServiceAsync(new()
        {
            Protocol = "openai-chat",
            BaseUrl = "http://127.0.0.1:1/",
            Models = [new() { Model = "fixture" }]
        }, ct: TestContext.CancellationToken);
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: static (_, _, _) => Task.FromResult(new AiModelIdentity(null, null, null)));
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        Assert.IsTrue(await configuration.LoadAsync());
        var id = await scope.Client.Skins.CreateAsync(SkinModel.Classic, template: "blank", cancellationToken: TestContext.CancellationToken);
        var grant = await scope.Client.Skins.Proposals.CreateScopeAsync(id, new(DirectEditing: true), TestContext.CancellationToken);
        var record = await ui.Service.CreateConversationAsync(new() { SkinScopeId = grant.ScopeId, GenerateTitle = false }, ct: TestContext.CancellationToken);
        await ui.Chats.LoadConversationAsync(record.Id!);
        var previous = ui.Current;
        previous.Draft = "Keep this chat until I confirm its replacement";
        await ui.Chats.SendAsync(previous);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (!previous.Turns.Single().ObservedTerminal)
            await timer.WaitForNextTickAsync(deadline.Token);
        var other = await ui.Service.CreateConversationAsync(new() { Title = "Unrelated chat" }, ct: TestContext.CancellationToken);
        var workspace = new SkinAiWorkspace(id, ui);
        var skinPath = Path.Combine(scope.Root, "skins", id + ".rmcs");
        var bytes = await File.ReadAllBytesAsync(skinPath, TestContext.CancellationToken);
        var review = await ui.Service.ReviewConversationRemovalAsync(previous.Id, ct: TestContext.CancellationToken);
        Assert.AreSame(previous, ui.Current);
        Assert.HasCount(1, (await ui.Service.GetConversationAsync(previous.Id, ct: TestContext.CancellationToken)).Turns!);
        if (stale)
            await ui.Service.UpdateConversationAsync(new() { ConversationId = previous.Id, ExpectedRevision = previous.Revision, Title = "Changed after review" }, ct: TestContext.CancellationToken);

        var replaced = await workspace.RestartAsync(previous, review);
        Assert.AreEqual(!stale, replaced, ui.Error);
        var saved = await ui.Service.ListConversationsAsync(ct: TestContext.CancellationToken);
        Assert.Contains(item => item.Id == other.Id, saved.Items);
        if (stale)
        {
            Assert.AreSame(previous, ui.Current);
            Assert.Contains(item => item.Id == previous.Id, saved.Items);
        }
        else
        {
            Assert.AreNotSame(previous, ui.Current);
            Assert.DoesNotContain(item => item.Id == previous.Id, saved.Items);
            Assert.AreSame(ui.Current, ui.Conversations.Single());
            Assert.IsFalse(ui.Current.Persisted);
            Assert.IsEmpty(ui.Current.DisplayTurns);
            Assert.AreEqual(grant.ScopeId, ui.Current.SkinScopeId);
            Assert.AreEqual(previous.ModelId, ui.Current.ModelId);
        }

        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(skinPath, TestContext.CancellationToken));
    });

    /// <summary>Gets or sets the cancellation context for the isolated real Core workflow.</summary>
    public TestContext TestContext { get; set; }
}