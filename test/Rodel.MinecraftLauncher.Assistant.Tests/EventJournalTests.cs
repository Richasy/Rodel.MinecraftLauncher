// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Verifies the physical conversation journal and its durable cursor across independent store instances.</summary>
[TestClass]
public sealed class EventJournalTests
{
    /// <summary>Two turns share one journal while each keeps an independent, resumable sequence.</summary>
    [TestMethod]
    public async Task InterleavedWriters_ShareOneFileAndKeepPerTurnCursors()
    {
        // Workflow tests see events but cannot detect per-event files or a cursor mixing two turns.
        // Hold the real journal lease before starting both writers, then read through a separate store.
        using var sandbox = new AiSandbox();
        var ct = TestContext.CancellationToken;
        var conversation = await sandbox.ConversationAsync(ct);
        var conversationId = conversation["id"]!.ToString();
        var first = await sandbox.TurnAsync(conversation, false, ct);
        var second = await sandbox.TurnAsync(conversation, false, ct);
        var other = new AiChatStore(sandbox.Paths);
        var file = JournalPath(sandbox, conversationId);
        var firstId = first["id"]!.ToString();
        var secondId = second["id"]!.ToString();
        var largeText = new string('x', 70 * 1024) + "\n尾";
        await sandbox.Store.AppendEventAsync(first, "text_delta", null, new() { ["text"] = largeText }, ct);
        var initial = await other.EventsAsync(conversationId, firstId, ct: ct);
        Assert.AreEqual(largeText, initial.Items.Single().Data?["text"]?.GetValue<string>());

        Task<JsonObject> firstWrite, secondWrite;
        using (await ResourceLease.AcquireAsync(file, ct))
        {
            firstWrite = sandbox.Store.AppendEventAsync(first, "text_delta", null, new() { ["text"] = "first again" }, ct);
            secondWrite = other.AppendEventAsync(second, "tool_started", "step", new() { ["name"] = "fixture" }, ct);
            Assert.IsFalse(firstWrite.IsCompleted);
            Assert.IsFalse(secondWrite.IsCompleted);
        }

        await Task.WhenAll(firstWrite, secondWrite);
        var firstPage = await other.EventsAsync(conversationId, firstId, limit: 1, ct: ct);
        Assert.IsTrue(firstPage.HasMore);
        Assert.AreEqual(1L, firstPage.NextSequence);
        var continued = await other.EventsAsync(conversationId, firstId, firstPage.NextSequence, ct: ct);
        Assert.AreEqual(2L, continued.NextSequence);
        Assert.AreEqual("first again", continued.Items.Single().Data?["text"]?.GetValue<string>());
        var independent = await sandbox.Store.EventsAsync(conversationId, secondId, ct: ct);
        Assert.AreEqual(1L, independent.NextSequence);
        Assert.AreEqual("tool_started", independent.Items.Single().Kind);
        Assert.HasCount(3, File.ReadLines(file));
        CollectionAssert.AreEquivalent(new[] { "conversation.json", "events.jsonl", firstId + ".json", secondId + ".json" },
            Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray());
    }

    /// <summary>Recovery preserves the committed prefix and replaces a torn suffix without replaying tools.</summary>
    [TestMethod]
    public async Task InterruptedAppend_RecoversFromLastCompleteLine()
    {
        // A partial byte suffix models a process exit before the terminating newline is committed.
        // Exercise the normal interrupted-turn recovery, then reopen the resulting journal with a fresh store.
        using var sandbox = new AiSandbox();
        var ct = TestContext.CancellationToken;
        var conversation = await sandbox.ConversationAsync(ct);
        var conversationId = conversation["id"]!.ToString();
        var turn = await sandbox.TurnAsync(conversation, false, ct);
        var turnId = turn["id"]!.ToString();
        await sandbox.Store.AppendEventAsync(turn, "text_delta", null, new() { ["text"] = "committed" }, ct);
        var file = JournalPath(sandbox, conversationId);
        var prefix = await File.ReadAllTextAsync(file, ct);
        await File.AppendAllTextAsync(file, "{\"turnId\":\"torn", ct);

        var recovery = new AiChatStore(sandbox.Paths);
        var before = await recovery.EventsAsync(conversationId, turnId, ct: ct);
        Assert.AreEqual(1L, before.NextSequence);
        await recovery.RecoverInterruptedAsync(ct);

        var reopened = new AiChatStore(sandbox.Paths);
        Assert.AreEqual("interrupted", reopened.GetTurn(conversationId, turnId).Status);
        var events = await reopened.CompletedEventsAsync(conversationId, turnId, ct);
        Assert.AreEqual(2L, events.NextSequence);
        Assert.AreEqual("committed", events.Items[0].Data?["text"]?.GetValue<string>());
        Assert.AreEqual("turn_finished", events.Items[1].Kind);
        var saved = await File.ReadAllTextAsync(file, ct);
        Assert.StartsWith(prefix, saved);
        Assert.DoesNotContain("torn", saved);
    }

    /// <summary>A committed terminal reply restores its answer after an exit before the turn snapshot save.</summary>
    [TestMethod]
    public async Task CommittedReply_RecoversTheAnswerWithoutDuplicatingTheReceipt()
    {
        // The torn-tail case has no terminal receipt. Seed a complete reply while the turn snapshot is still running.
        using var sandbox = new AiSandbox();
        var ct = TestContext.CancellationToken;
        var conversation = await sandbox.ConversationAsync(ct);
        var conversationId = conversation["id"]!.ToString();
        var turn = await sandbox.TurnAsync(conversation, false, ct);
        var turnId = turn["id"]!.ToString();
        const string answer = "Reply was committed before the snapshot";
        await sandbox.Store.AppendEventAsync(turn, "turn_finished", null, new() { ["status"] = "completed", ["answer"] = answer }, ct);

        var recovery = new AiChatStore(sandbox.Paths);
        await recovery.RecoverInterruptedAsync(ct);

        var restored = recovery.GetTurn(conversationId, turnId);
        Assert.AreEqual("completed", restored.Status);
        Assert.AreEqual(answer, restored.Answer);
        Assert.HasCount(1, (await recovery.CompletedEventsAsync(conversationId, turnId, ct)).Items);
    }

    private static string JournalPath(AiSandbox sandbox, string conversationId) =>
        Path.Combine(sandbox.Root, "ai", "conversations", conversationId, "events.jsonl");

    /// <summary>Provides cancellation for the real file operations.</summary>
    public TestContext TestContext { get; set; }
}