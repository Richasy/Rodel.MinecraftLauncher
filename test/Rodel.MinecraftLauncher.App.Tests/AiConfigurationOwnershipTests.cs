// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using Sprout.Reactive;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Exercises the real App owners with Core files; only package preferences and optional artwork are substituted.</summary>
[TestClass]
public sealed class AiConfigurationOwnershipTests
{
    private static Task<AiModelIdentity> NoArtwork(string? endpoint, string model, CancellationToken cancellation)
        => Task.FromResult(new AiModelIdentity(null, null, null));

    /// <summary>Entering AI during a settings write must initialize automatically after that write finishes.</summary>
    [TestMethod]
    public void FirstEntry_WaitsForTheExistingSettingsOperation() => StateTestScope.Run(async () =>
    {
        // Author review: joining another load does not cover a configuration operation owning the gate.
        using var scope = new StateTestScope();
        var gate = new AiOperationGate();
        using var configuration = new AiConfigurationStore(scope.Client, gate, NoArtwork);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>(), gate);
        var release = new TaskCompletionSource();
        var operation = ui.RunAsync(() => release.Task);
        Assert.IsTrue(ui.Busy);
        var loading = ui.LoadAsync();
        var waited = !loading.IsCompleted;
        try { release.TrySetResult(); await Task.WhenAll(operation, loading); }
        finally { release.TrySetResult(); }

        Assert.IsTrue(waited, "initialization must wait for the existing short operation.");
        Assert.IsTrue(ui.Loaded);
        Assert.IsFalse(ui.Busy);
        await configuration.ArtworkCompletion;
    });

    /// <summary>A first-entry follow-up joins initialization before acquiring the shared operation gate.</summary>
    [TestMethod]
    public void FirstEntry_FollowUpWaitsForTheExistingHistoryLoad() => StateTestScope.Run(async () =>
    {
        // Author review: sequential initialization never covers Navigate -> Activate -> OpenRepair's nested load.
        using var scope = new StateTestScope();
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: NoArtwork);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        Assert.IsTrue(await configuration.LoadAsync());
        var pending = new TaskCompletionSource<bool>();
        var field = typeof(AiConfigurationStore).GetField("_load", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        // Hold the existing configuration single-flight without wall-clock or thread-pool timing assumptions.
        field.SetValue(configuration, pending.Task);
        var loading = ui.LoadAsync();
        var ran = false;
        async Task FollowUp()
        {
            await ui.LoadAsync();
            await ui.RunAsync(() =>
            {
                ran = true;
                return Task.CompletedTask;
            });
        }

        var followUp = FollowUp();
        var waited = !followUp.IsCompleted;
        try { pending.TrySetResult(true); await Task.WhenAll(loading, followUp); }
        finally { pending.TrySetResult(true); field.SetValue(configuration, null); }

        Assert.IsTrue(waited, "a repair/follow-up must join initialization rather than return through the busy gate.");
        Assert.IsTrue(ran);
        Assert.IsTrue(ui.Loaded);
    });

    /// <summary>Startup and background history defer event payloads; selecting saved history restores the complete transcript.</summary>
    [TestMethod]
    public void StartupConfiguration_DoesNotHydrateSavedHistory_AndExplicitEntryRestoresIt() => StateTestScope.Run(async () =>
    {
        // Author review: ownership tests miss eager history payloads, loading before disk returns and selection after deletion.
        using var scope = new StateTestScope();
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: NoArtwork);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        Assert.IsTrue(await configuration.SaveServiceAsync(null, "fixture", AiProtocols.OpenAiChat, "http://127.0.0.1:9/",
            string.Empty, [new(string.Empty, "Fixture", "fixture")], true, true));
        var savedId = await WriteTerminalHistoryAsync(scope, configuration.Defaults.ModelId, "Retained history");
        // The second chat crosses the live cursor's 100-event boundary. A terminal journal read must preserve the full order.
        var nextId = await WriteTerminalHistoryAsync(scope, configuration.Defaults.ModelId, "Next history", eventCount: 101);

        Assert.IsTrue(await ui.LoadConfigurationAsync());
        Assert.IsFalse(ui.Loaded);
        Assert.IsEmpty(ui.Conversations);
        await ui.LoadAsync();
        Assert.IsTrue(ui.Loaded);
        Assert.HasCount(2, ui.Conversations);
        var history = ui.Conversations.Single(item => item.Id == savedId);
        Assert.AreEqual("Retained history", history.Name);
        var turn = history.Turns.Single();
        Assert.IsEmpty(turn.Events, "background terminal history must not retain every event payload.");
        Assert.IsFalse(turn.ObservedTerminal);
        // Hold the production storage lease so loading is observed before data returns, without timing sleeps.
        Task hydration;
        using (await ResourceLease.AcquireAsync(Path.Combine(scope.Root, "ai", "conversations", savedId, "turns", turn.Id + ".json"), TestContext.CancellationToken))
        {
            history.TranscriptOffset = new(0f, 120f);
            ui.Select(history);
            hydration = ui.Chats.SelectAsync(history);
            Assert.IsTrue(history.HistoryLoading);
            Assert.IsFalse(hydration.IsCompleted);
        }

        await hydration;
        Assert.IsFalse(history.HistoryLoading);
        Assert.IsFalse(history.HistoryFailed);
        Assert.IsNull(history.TranscriptOffset, "Completed history requests a bottom layout instead of restoring the released placeholder offset.");
        Assert.IsTrue(turn.ObservedTerminal);
        Assert.HasCount(1, turn.Events);
        Assert.AreEqual("Saved answer", turn.Text);
        Assert.AreEqual("Saved answer", turn.Record.Answer);
        history.Draft = "Keep draft";
        turn.ExpandedParts.Add("keep expansion");
        ui.SetTranscriptActive(false);
        Assert.IsEmpty(turn.Events);
        Assert.IsNull(turn.Record.Answer);
        ui.SetTranscriptActive(true);
        await ui.Chats.SelectAsync(history);
        Assert.HasCount(1, turn.Events, "re-entry must restore rather than duplicate the persisted events.");
        Assert.AreEqual("Saved answer", turn.Record.Answer);
        Assert.AreEqual("Keep draft", history.Draft);
        Assert.Contains("keep expansion", turn.ExpandedParts);
        var next = ui.Conversations.Single(item => item.Id == nextId);
        Assert.IsEmpty(next.Turns.Single().Events);
        ui.Select(next);
        await ui.Chats.SelectAsync(next);
        Assert.AreSame(next, ui.Current);
        Assert.IsTrue(next.Turns.Single().ObservedTerminal);
        CollectionAssert.AreEqual(Enumerable.Range(1, 101).Select(sequence => (long?)sequence).ToArray(),
            next.Turns.Single().Events.Select(item => item.Sequence).ToArray());
        Assert.AreEqual(101L, next.Turns.Single().Cursor);
        Assert.AreEqual("Saved answer", next.Turns.Single().Text);
        await configuration.ArtworkCompletion;
    });

    /// <summary>Switching away from a blocked history read cannot let its late completion clear the current load or draft.</summary>
    [TestMethod]
    public void HistoryLoading_FastSwitchKeepsTheLatestSelectionAndRestoresEachEventOnce() => StateTestScope.Run(async () =>
    {
        // Review: the normal reopen case has no stale read in flight. Hold the real turn lease, switch A -> B -> A,
        // then release it and inspect final content, loading completion and drafts. No service or renderer is mocked.
        using var scope = new StateTestScope();
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: NoArtwork);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        Assert.IsTrue(await configuration.SaveServiceAsync(null, "fixture", AiProtocols.OpenAiChat, "http://127.0.0.1:9/",
            string.Empty, [new(string.Empty, "Fixture", "fixture")], true, true));
        var firstId = await WriteTerminalHistoryAsync(scope, configuration.Defaults.ModelId, "First history");
        var secondId = await WriteTerminalHistoryAsync(scope, configuration.Defaults.ModelId, "Second history");
        await ui.LoadAsync();
        var first = ui.Conversations.Single(item => item.Id == firstId);
        var second = ui.Conversations.Single(item => item.Id == secondId);
        first.Draft = "Keep first draft";
        second.Draft = "Keep second draft";
        Task oldRead, middleRead, selectedRead;
        using (await ResourceLease.AcquireAsync(Path.Combine(scope.Root, "ai", "conversations", firstId, "turns", first.Turns.Single().Id + ".json"), TestContext.CancellationToken))
        {
            ui.Select(first);
            oldRead = ui.Chats.SelectAsync(first);
            Assert.IsTrue(first.HistoryLoading);
            ui.Select(second);
            middleRead = ui.Chats.SelectAsync(second);
            ui.Select(first);
            selectedRead = ui.Chats.SelectAsync(first);
            Assert.IsTrue(first.HistoryLoading);
            Assert.IsFalse(selectedRead.IsCompleted);
        }

        await Task.WhenAll(oldRead, middleRead, selectedRead);
        Assert.AreSame(first, ui.Current);
        Assert.IsFalse(first.HistoryLoading);
        Assert.IsFalse(first.HistoryFailed);
        Assert.IsTrue(first.Turns.Single().ObservedTerminal);
        Assert.HasCount(1, first.Turns.Single().Events);
        Assert.AreEqual("Saved answer", first.Turns.Single().Record.Answer);
        Assert.AreEqual("Keep first draft", first.Draft);
        Assert.AreEqual("Keep second draft", second.Draft);
        Assert.IsEmpty(ui.Error);
        await configuration.ArtworkCompletion;
    });

    /// <summary>
    /// Configuration changes may seed an untouched draft, not an existing or skin-scoped draft. A notification
    /// listener reading its own revision must not become a dependency of the configuration-to-chat effect.
    /// </summary>
    [TestMethod]
    public void Defaults_SeedOnlyEligibleDrafts_WithoutSubscribingLegacyNotifications() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: NoArtwork);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        var revision = new Signal<int>(0);
        ui.Changed += () => revision.Value++;
        Assert.IsTrue(await configuration.LoadAsync());
        Assert.IsTrue(await configuration.DefaultModeAsync(true));
        Assert.IsTrue(ui.Current.Operate);
        var before = revision.Value;
        revision.Value++;
        Assert.AreEqual(before + 1, revision.Value);
        ui.NewConversation();
        ui.Current.Draft = "keep draft";
        ui.Current.SkinScopeId = "synthetic-ui-scope";
        ui.Current.Files = false;
        ui.Current.Network = false;
        Assert.IsTrue(await configuration.DefaultModeAsync(false));
        Assert.IsTrue(ui.Current.Operate);
        Assert.AreEqual("keep draft", ui.Current.Draft);
        Assert.IsFalse(ui.Current.Files);
        Assert.IsFalse(ui.Current.Network);
        await configuration.ArtworkCompletion;
    });

    /// <summary>
    /// Two editor projects borrow one configuration; closing either must leave the other live. Chat events and
    /// failures must neither republish configuration nor overwrite its errors; failed Core writes preserve storage.
    /// </summary>
    [TestMethod]
    public void BorrowedConfiguration_SurvivesProjectDisposal_AndKeepsErrorsInTheirDomain() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: NoArtwork);
        using var first = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        using var second = new AiUiState(scope.Client, configuration, new Dictionary<string, object>());
        Assert.IsTrue(await configuration.LoadAsync());
        var reads = 0;
        using var effect = new Effect(() => { _ = configuration.Providers; _ = configuration.Error; reads++; });
        var before = reads;
        second.Report(new IOException("chat only"));
        second.Notify();
        Assert.AreEqual(before, reads);
        Assert.AreEqual(string.Empty, configuration.Error);
        first.Dispose();
        Assert.IsTrue(await configuration.DefaultModeAsync(true));
        Assert.IsTrue(second.Current.Operate);
        var stored = await scope.Client.Assistant.Services.GetAsync(TestContext.CancellationToken);
        Assert.IsFalse(await configuration.SaveServiceAsync(null, "invalid", "invalid-protocol", string.Empty, string.Empty, [], false, true));
        Assert.IsFalse(string.IsNullOrEmpty(configuration.Error));
        Assert.AreEqual("chat only", second.Error);
        Assert.AreEqual(stored.Revision, (await scope.Client.Assistant.Services.GetAsync(TestContext.CancellationToken)).Revision);
        await configuration.ArtworkCompletion;
    });

    /// <summary>A held chat request must keep the original shared write exclusion without reporting a skipped write as success.</summary>
    [TestMethod]
    public void SharedGate_DropsConfigurationWriteWhileChatRequestIsInFlight() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var gate = new AiOperationGate();
        using var configuration = new AiConfigurationStore(scope.Client, gate, NoArtwork);
        using var ui = new AiUiState(scope.Client, configuration, new Dictionary<string, object>(), gate);
        Assert.IsTrue(await configuration.LoadAsync());
        var release = new TaskCompletionSource();
        var chat = ui.RunAsync(() => release.Task);
        try
        {
            Assert.IsTrue(configuration.Busy);
            Assert.IsFalse(await configuration.DefaultModeAsync(true));
            Assert.IsFalse(configuration.Defaults.Operate);
            Assert.AreEqual("safe", (await scope.Client.Assistant.Services.GetAsync(TestContext.CancellationToken)).DefaultOperationMode);
        }
        finally { release.TrySetResult(); await chat; }

        Assert.IsTrue(await configuration.DefaultModeAsync(true));
        Assert.IsFalse(ui.Busy);
        await configuration.ArtworkCompletion;
    });

    /// <summary>A non-cooperating artwork reader may finish after disposal; no late image or warning may reach subscribers.</summary>
    [TestMethod]
    public void Dispose_NonCooperatingArtworkCannotPublishIntoReleasedConfiguration() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var started = new TaskCompletionSource();
        var completion = new TaskCompletionSource<AiModelIdentity>();
        CancellationToken observed = default;
        Task<AiModelIdentity> Read(string? endpoint, string model, CancellationToken token)
        {
            observed = token;
            started.TrySetResult();
            return completion.Task;
        }

        using var configuration = new AiConfigurationStore(scope.Client, readIdentity: Read);
        Assert.IsTrue(await configuration.SaveServiceAsync(null, "fixture", AiProtocols.OpenAiChat, "http://127.0.0.1:9/",
            string.Empty, [new(string.Empty, "Fixture", "fixture")], true, true));
        await started.Task;
        var artwork = configuration.ArtworkCompletion;
        var notifications = 0;
        using var effect = new Effect(() => { _ = configuration.ProviderLogos; _ = configuration.ArtworkWarning; notifications++; });
        configuration.Dispose();
        var before = notifications;
        Assert.IsTrue(observed.IsCancellationRequested);
        completion.SetResult(new("provider", null, "<svg/>"));
        await artwork;
        Assert.AreEqual(before, notifications);
        Assert.HasCount(0, configuration.ProviderLogos);
    });

    private async Task<string> WriteTerminalHistoryAsync(StateTestScope scope, string? modelId, string title, int eventCount = 1)
    {
        var saved = await scope.Client.Assistant.CreateConversationAsync(new()
        {
            ModelId = modelId,
            Title = title,
            GenerateTitle = false
        }, ct: TestContext.CancellationToken);
        var turnId = Guid.NewGuid().ToString("N");
        var root = Path.Combine(scope.Root, "ai", "conversations", saved.Id!);
        await JsonFiles.WriteAsync(Path.Combine(root, "turns", turnId + ".json"), new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = turnId,
            ["conversationId"] = saved.Id,
            ["status"] = "completed",
            ["prompt"] = "Fixture",
            ["answer"] = "Saved answer",
            ["createdUtc"] = "2026-01-01T00:00:00Z",
            ["updatedUtc"] = "2026-01-01T00:00:01Z"
        }, TestContext.CancellationToken);
        var journal = new System.Text.StringBuilder();
        for (var sequence = 1; sequence <= eventCount; sequence++)
        {
            var last = sequence == eventCount;
            var item = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["id"] = Guid.NewGuid().ToString("N"),
                ["conversationId"] = saved.Id,
                ["turnId"] = turnId,
                ["sequence"] = sequence,
                ["timeUtc"] = "2026-01-01T00:00:01Z",
                ["kind"] = last ? "text_delta" : "model_progress",
                ["data"] = last ? new JsonObject { ["text"] = "Saved answer" } : []
            };
            journal.Append(item.ToJsonString()).Append('\n');
        }

        await File.WriteAllTextAsync(Path.Combine(root, "events.jsonl"), journal.ToString(), TestContext.CancellationToken);
        return saved.Id!;
    }

    public TestContext TestContext { get; set; }
}