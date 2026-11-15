// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the chat page's navigation after a confirmed deletion.</summary>
[TestClass]
public sealed class AiConversationRemovalTests
{
    /// <summary>Deleting the selected chat opens a blank draft while another saved chat remains available.</summary>
    [TestMethod]
    public void RemoveCurrentChat_LeavesBlankDraftWithoutOpeningRemainingHistory() => StateTestScope.Run(async () =>
    {
        // History hydration covers explicit selection and skin restart replaces its selection.
        // Exercise confirmed deletion through real Core storage and detect any implicit history load.
        using var scope = new StateTestScope();
        using var configuration = new AiConfigurationStore(scope.Client,
            readIdentity: (_, _, _) => Task.FromResult(new AiModelIdentity(null, null, null)));
        Assert.IsTrue(await configuration.SaveServiceAsync(null, "fixture", AiProtocols.OpenAiChat, "http://127.0.0.1:9/",
            string.Empty, [new(string.Empty, "Fixture", "fixture")], true, true));
        await configuration.ArtworkCompletion;
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        var cancellation = TestContext.CancellationToken;
        var removed = await ui.Service.CreateConversationAsync(new() { Title = "Remove this chat" }, ct: cancellation);
        var remaining = await ui.Service.CreateConversationAsync(new() { Title = "Keep this chat" }, ct: cancellation);
        await ui.LoadAsync();
        var selected = ui.Conversations.Single(chat => chat.Id == removed.Id);
        var kept = ui.Conversations.Single(chat => chat.Id == remaining.Id);
        ui.Select(selected);
        await ui.Chats.SelectAsync(selected);
        var review = await ui.Service.ReviewConversationRemovalAsync(selected.Id, ct: cancellation);
        var openedRemainingHistory = false;
        ui.Changed += () => openedRemainingHistory |= kept.HistoryLoading;

        Assert.IsTrue(await ui.RunAsync(() => ui.Chats.RemoveAsync(selected, review)), ui.Error);

        Assert.IsFalse(openedRemainingHistory, "Deletion started loading an unrelated conversation.");
        Assert.IsFalse(ui.Current.Persisted);
        Assert.IsEmpty(ui.Current.DisplayTurns);
        Assert.IsEmpty(ui.Current.Draft);
        Assert.AreSame(kept, ui.Conversations.Single());
        var saved = await ui.Service.ListConversationsAsync(ct: cancellation);
        Assert.AreEqual(remaining.Id, saved.Items.Single().Id);
    });

    /// <summary>Provides cancellation for isolated Core storage operations.</summary>
    public TestContext TestContext { get; set; }
}