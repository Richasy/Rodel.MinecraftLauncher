// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal enum AiHistoryBadge
{
    None,
    NeedsInput,
    Responding,
    Queued,
    Unread
}

/// <summary>Freezes one history row without exposing a mutable conversation or caching localized text.</summary>
internal sealed record AiHistoryItem(
    string Key,
    string ConversationId,
    string Name,
    string? InstanceId,
    string? InstanceName,
    bool Repair,
    bool Selected,
    AiHistoryBadge Badge,
    bool NeedsInput)
{
    internal static AiHistoryBadge ReadBadge(IEnumerable<AiChatRecord> turns, string? readReceipt)
    {
        var records = turns.ToArray();
        if (records.Any(turn => turn.Status is "awaiting_user" or "awaiting_confirmation" or "awaiting_access"))
            return AiHistoryBadge.NeedsInput;
        if (records.Any(turn => turn.Status is "connecting" or "generating" or "canceling"))
            return AiHistoryBadge.Responding;
        if (records.Any(turn => turn.Status == "queued"))
            return AiHistoryBadge.Queued;
        var latest = records.Where(turn => AiChatStates.IsTerminal(turn.Status)).OrderByDescending(turn => turn.UpdatedUtc, StringComparer.Ordinal).FirstOrDefault();
        return latest is null || latest.Id + "|" + latest.UpdatedUtc == readReceipt
            ? AiHistoryBadge.None
            : latest.Status == "completed" ? AiHistoryBadge.Unread : AiHistoryBadge.NeedsInput;
    }
}