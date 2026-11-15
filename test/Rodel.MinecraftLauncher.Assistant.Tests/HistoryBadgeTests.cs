// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects history's semantic projection while native identity and menu wiring are verified separately.</summary>
[TestClass]
public sealed class HistoryBadgeTests
{
    /// <summary>
    /// A single bool cannot preserve pending-input, running, queued and unread distinctions. Mixed Core turn
    /// snapshots must retain their priority as an earlier turn completes and the next one becomes actionable.
    /// </summary>
    [TestMethod]
    public void MixedTurns_PrioritizePendingInputThenResponseQueueAndUnread()
    {
        var completed = Turn("done", "completed", "2026-10-27T01:00:00Z");
        var queued = Turn("queued", "queued", "2026-10-27T01:01:00Z");
        var running = Turn("running", "generating", "2026-10-27T01:02:00Z");
        var question = Turn("question", "awaiting_user", "2026-10-27T01:03:00Z");
        Assert.AreEqual(AiHistoryBadge.NeedsInput, AiHistoryItem.ReadBadge([completed, queued, running, question], null));
        Assert.AreEqual(AiHistoryBadge.Responding, AiHistoryItem.ReadBadge([completed, queued, running], null));
        Assert.AreEqual(AiHistoryBadge.Queued, AiHistoryItem.ReadBadge([completed, queued], null));
        Assert.AreEqual(AiHistoryBadge.Unread, AiHistoryItem.ReadBadge([completed], null));
        Assert.AreEqual(AiHistoryBadge.None, AiHistoryItem.ReadBadge([completed], "done|2026-10-27T01:00:00Z"));
    }

    /// <summary>
    /// History order is not necessarily terminal completion order. Receipts bind both turn identity and update
    /// time, so acknowledging an old completion must not hide a newer failure or a subsequently updated result.
    /// </summary>
    [TestMethod]
    public void ReadReceipt_TracksLatestTerminalIdentityAndUpdateRatherThanListPosition()
    {
        var old = Turn("old", "completed", "2026-10-27T01:00:00Z");
        var failed = Turn("new", "failed", "2026-10-27T02:00:00Z");
        Assert.AreEqual(AiHistoryBadge.NeedsInput, AiHistoryItem.ReadBadge([failed, old], "old|2026-10-27T01:00:00Z"));
        Assert.AreEqual(AiHistoryBadge.None, AiHistoryItem.ReadBadge([failed, old], "new|2026-10-27T02:00:00Z"));
        var updated = Turn("new", "completed", "2026-10-27T03:00:00Z");
        Assert.AreEqual(AiHistoryBadge.Unread, AiHistoryItem.ReadBadge([updated, old], "new|2026-10-27T02:00:00Z"));
    }

    private static AiChatRecord Turn(string id, string status, string updated)
        => new() { Id = id, Status = status, UpdatedUtc = updated };
}