// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Removes only explicitly game-owned history after its exact Java process exits; launcher history remains persistent.</summary>
internal sealed class AiGameHistory(LauncherPaths paths)
{
    /// <summary>Reads temporary conversation ownership independently of the launcher-only history catalog.</summary>
    internal JsonObject[] Records(string? session = null)
    {
        var root = Path.Combine(paths.Root, "ai", "conversations");
        var records = new List<JsonObject>();
        if (!Directory.Exists(root)) return [];
        var store = new AiChatStore(paths);
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                var document = store.GetDocument(id);
                if (document["gameSessionId"] is JsonValue value && value.TryGetValue<string>(out var owner) && (session is null || owner == session))
                    records.Add(document);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or LauncherOperationException) { }
        }

        return [.. records];
    }

    /// <summary>Prunes abandoned game history under the caller's root generation lease, without replaying tools or canceling independent jobs.</summary>
    internal async Task RemoveClosedAsync(CancellationToken ct)
    {
        foreach (var document in Records())
        {
            ct.ThrowIfCancellationRequested();
            if (document["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var schema) || schema != 1) continue;
            if (ProcessAlive(document)) continue;
            var id = document["id"]!.ToString();
            try
            {
                using var operation = await ResourceLease.AcquireAsync(Path.Combine(paths.Root, "ai", "conversation-operations", id), ct).ConfigureAwait(false);
                var store = new AiChatStore(paths);
                var current = store.GetDocument(id);
                if (current["gameSessionId"]?.ToString() != document["gameSessionId"]!.ToString() || ProcessAlive(current)) continue;
                if (store.TurnDocuments(id).Any(turn => !AiChatStates.IsTerminal(turn["status"]?.ToString()) && AiGenerationLease.OwnerAlive(turn))) continue;
                var history = new AiConversationHistory(paths);
                var review = history.ReviewRemoval(id);
                await history.RemoveAsync(new() { ConversationId = id, ExpectedRevision = review.Revision.GetValue<long>(), Fingerprint = review.Fingerprint, Confirm = true }, ct).ConfigureAwait(false);
                var session = document["gameSessionId"]!.ToString();
                if (Records(session).Length == 0) RemoveSlots(paths, session);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            catch (LauncherOperationException error) when (error.Code == "AI_CONVERSATION_NOT_FOUND") { }
        }
    }

    private static bool ProcessAlive(JsonObject document)
    {
        if (document["gameProcessId"] is not JsonValue pid || !pid.TryGetValue<int>(out var id)
            || document["gameProcessStartUtc"] is not JsonValue start || !start.TryGetValue<string>(out var started)
            || !DateTimeOffset.TryParse(started, out _))
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().ToString("O") == started;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; }
    }

    /// <summary>Gets the exact game-owned slot directory; no slot is reused by a later game launch.</summary>
    internal static string SlotsFor(LauncherPaths paths, string session) => Path.Combine(paths.Root, "ai", "game-slots", AiChatStore.CheckId(session));

    /// <summary>Removes only this game's transport receipts after all its conversations have been removed.</summary>
    internal static void RemoveSlots(LauncherPaths paths, string session)
    {
        var directory = SlotsFor(paths, session);
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}