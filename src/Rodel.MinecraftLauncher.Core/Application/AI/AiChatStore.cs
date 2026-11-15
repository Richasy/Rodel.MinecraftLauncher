// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Atomic chat snapshots and append-only event journals shared by all AI conversation scopes.</summary>
internal sealed class AiChatStore(LauncherPaths paths)
{
    private readonly AiEventJournal _events = new();
    private string Root => Path.Combine(paths.Root, "ai", "conversations");
    public static bool IsTerminal(string? status) => status is "completed" or "canceled" or "failed" or "incomplete" or "interrupted" or "refused";
    public static string CheckId(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new ArgumentException("Expected a 32-digit conversation or turn identifier.");
    private string DirectoryFor(string id) => Path.Combine(Root, CheckId(id));
    private string TurnPath(string conversationId, string turnId) => Path.Combine(DirectoryFor(conversationId), "turns", CheckId(turnId) + ".json");
    private string EventPath(string conversationId) => Path.Combine(DirectoryFor(conversationId), "events.jsonl");
    private string CancellationPath(string conversationId, string turnId) => Path.Combine(DirectoryFor(conversationId), "turns", CheckId(turnId) + ".cancel");
    public bool HasCancellationSignal(string conversationId, string turnId) => File.Exists(CancellationPath(conversationId, turnId));
    public Task SignalCancellationAsync(string conversationId, string turnId, CancellationToken ct)
        => JsonFiles.WriteAsync(CancellationPath(conversationId, turnId), new JsonObject { ["turnId"] = turnId, ["requestedUtc"] = DateTimeOffset.UtcNow.ToString("O") }, ct);

    /// <summary>Reads one durable conversation, retaining unknown and noncanonical fields.</summary>
    public AiChatRecord Get(string id) => AiChatRecord.Read(GetDocument(id));

    /// <summary>Reads a public turn snapshot without private provider protocol state.</summary>
    public AiChatRecord GetTurn(string conversationId, string turnId) => AiChatRecord.Read(GetTurnDocument(conversationId, turnId));

    internal JsonObject GetDocument(string id)
    {
        var file = Path.Combine(DirectoryFor(id), "conversation.json");
        var record = ReadRecord(file, "AI_CONVERSATION_NOT_FOUND", "This conversation no longer exists.");
        if (!string.Equals(record["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The conversation record identity is missing or inconsistent.");
        return record;
    }

    internal JsonObject GetTurnDocument(string conversationId, string turnId, bool includeProtocolState = false)
    {
        var file = TurnPath(conversationId, turnId);
        var turn = ReadRecord(file, "AI_TURN_NOT_FOUND", "This turn no longer exists.", visibleTurn: !includeProtocolState);
        if (!string.Equals(turn["id"]?.ToString(), turnId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(turn["conversationId"]?.ToString(), conversationId, StringComparison.OrdinalIgnoreCase)
            || turn["status"] is not JsonValue status || !status.TryGetValue<string>(out _))
        {
            throw new InvalidDataException("The turn record identity or status is missing or inconsistent.");
        }

        if (!includeProtocolState)
        {
            if (turn["intents"] is JsonArray intents)
            {
                foreach (var intent in intents.OfType<JsonObject>())
                    if (intent["result"] is JsonObject result) intent["result"] = Assistant.AiChatMessages.VisibleToolResult(result);
            }

            if (turn["contextCheckpoint"] is JsonObject checkpoint)
            {
                if (checkpoint["excerpts"] is JsonArray excerpts)
                {
                    checkpoint["excerptCount"] = excerpts.Count;
                    checkpoint.Remove("excerpts");
                }

                if (checkpoint["summary"]?.GetValue<string>() is { } summary)
                {
                    var end = Math.Min(summary.Length, 8000);
                    if (end > 0 && end < summary.Length && char.IsHighSurrogate(summary[end - 1]) && char.IsLowSurrogate(summary[end])) end--;
                    checkpoint["summaryCharacters"] = summary.Length;
                    checkpoint["summaryTruncated"] = end < summary.Length;
                    checkpoint["summary"] = summary[..end];
                }
            }
        }

        return turn;
    }

    /// <summary>Reads a stable bounded page while retaining unreadable records as explicit diagnostics.</summary>
    public AiConversationPage List(int offset = 0, int limit = 20)
    {
        PageBounds(offset, limit);
        var entries = new List<(AiChatRecord Report, string? UpdatedUtc)>();
        if (Directory.Exists(Root))
        {
            foreach (var directory in Directory.EnumerateDirectories(Root))
            {
                var id = Path.GetFileName(directory);
                if (!Guid.TryParseExact(id, "N", out _))
                    continue;
                try
                {
                    var document = GetDocument(id);
                    if (document["gameSessionId"] is not null) continue;
                    var updatedUtc = document["updatedUtc"]?.ToString();
                    entries.Add((AiChatRecord.Read(document), updatedUtc));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    entries.Add((new() { Id = id, Status = "unreadable", Error = LauncherOperationException.ToResult(error)["error"]!.DeepClone() }, null));
                }
            }
        }

        var ordered = entries.OrderByDescending(item => item.UpdatedUtc, StringComparer.Ordinal).ToArray();
        return new()
        {
            Items = [.. ordered.Skip(offset).Take(limit).Select(item => item.Report)],
            Offset = offset,
            Limit = limit,
            Total = ordered.Length,
            HasMore = (long)offset + limit < ordered.Length
        };
    }

    /// <summary>Reads public turn snapshots in durable creation order.</summary>
    public AiTurnCatalog Turns(string conversationId) => new() { Items = [.. TurnDocuments(conversationId).Select(AiChatRecord.Read)] };

    internal JsonObject[] TurnDocuments(string conversationId, bool includeProtocolState = false)
    {
        _ = GetDocument(conversationId);
        var directory = Path.Combine(DirectoryFor(conversationId), "turns");
        var values = new List<JsonObject>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                try { values.Add(GetTurnDocument(conversationId, id, includeProtocolState)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    values.Add(new JsonObject { ["id"] = id, ["status"] = "unreadable", ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
                }
            }
        }

        return [.. values.OrderBy(item => item["createdUtc"]?.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>Reads committed events after a durable cursor without replaying effects.</summary>
    public Task<AiEventPage> EventsAsync(string conversationId, string turnId, long after = 0, int limit = 100, CancellationToken ct = default)
    {
        if (after < 0 || limit is < 1 or > 100)
            throw new ArgumentException("Event after must be nonnegative; limit must be 1 through 100.");
        ct.ThrowIfCancellationRequested();
        _ = GetTurnDocument(conversationId, turnId);
        return ReadEventPageAsync(conversationId, turnId, after, limit, ct);
    }

    internal Task<AiEventPage> CompletedEventsAsync(string conversationId, string turnId, CancellationToken ct)
    {
        // Core publishes the terminal turn snapshot after its final generation event is committed.
        // This path holds the conversation operation lease, so an effect acknowledgement or deletion cannot interleave.
        ct.ThrowIfCancellationRequested();
        if (!IsTerminal(GetTurnDocument(conversationId, turnId)["status"]?.ToString()))
            throw new LauncherOperationException("AI_TURN_ACTIVE", "Read active turn events using the live cursor.");
        return ReadEventPageAsync(conversationId, turnId, 0, int.MaxValue, ct);
    }

    private async Task<AiEventPage> ReadEventPageAsync(string conversationId, string turnId, long after, int limit, CancellationToken ct)
    {
        var items = new List<AiChatEvent>();
        var (next, hasMore) = await _events.ReadAsync(EventPath(conversationId), turnId,
            after, limit, item => items.Add(AiChatEvent.Read(item)), ct).ConfigureAwait(false);
        return new() { Items = [.. items], NextSequence = next, HasMore = hasMore };
    }

    public Task SaveConversationAsync(JsonObject conversation, CancellationToken ct = default)
    {
        Writable(conversation);
        conversation["updatedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        return WriteRecordAsync(Path.Combine(DirectoryFor(conversation["id"]!.GetValue<string>()), "conversation.json"), conversation, ct);
    }

    public Task SaveTurnAsync(JsonObject turn, CancellationToken ct = default)
    {
        Writable(turn);
        turn["updatedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        return WriteRecordAsync(TurnPath(turn["conversationId"]!.GetValue<string>(), turn["id"]!.GetValue<string>()), turn, ct);
    }

    internal Task AppendUserMessageAsync(JsonObject turn, CancellationToken ct) => AppendEventAsync(turn, "user_message", null, new()
    {
        ["text"] = turn["prompt"]?.DeepClone(),
        ["images"] = turn["images"]?.DeepClone()
    }, ct);

    public async Task<JsonObject> AppendEventAsync(JsonObject turn, string kind, string? stepId, JsonObject data, CancellationToken ct = default)
    {
        var conversationId = turn["conversationId"]!.GetValue<string>();
        var turnId = CheckId(turn["id"]!.GetValue<string>());
        var item = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = Guid.NewGuid().ToString("N"),
            ["conversationId"] = conversationId,
            ["turnId"] = turnId,
            ["timeUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["kind"] = kind,
            ["stepId"] = stepId,
            ["data"] = data.DeepClone()
        };
        await _events.AppendAsync(EventPath(conversationId), turnId, item, ct).ConfigureAwait(false);
        return item;
    }

    public async Task RecoverInterruptedAsync(CancellationToken ct, string? lockedConversation = null)
    {
        if (!Directory.Exists(Root))
            return;
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var conversationId = Path.GetFileName(directory);
            if (!Guid.TryParseExact(conversationId, "N", out _))
                continue;
            using var operation = conversationId == lockedConversation ? null : await ResourceLease.AcquireAsync(
                Path.Combine(paths.Root, "ai", "conversation-operations", conversationId), ct).ConfigureAwait(false);
            var turns = Path.Combine(directory, "turns");
            if (!Directory.Exists(turns))
                continue;
            foreach (var file in Directory.EnumerateFiles(turns, "*.json"))
            {
                JsonObject turn;
                var turnId = Path.GetFileNameWithoutExtension(file);
                if (!Guid.TryParseExact(turnId, "N", out _)) continue;
                try { turn = GetTurnDocument(conversationId, turnId, true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    // Corruption remains visible through Turns; unrelated valid conversations can still run.
                    continue;
                }

                if (turn["schemaVersion"]?.GetValue<int>() != 1 || IsTerminal(turn["status"]?.ToString()))
                    continue;
                if (AiGenerationLease.OwnerAlive(turn))
                    continue;
                JsonObject? terminal = null;
                var hasUserMessage = false;
                var openSteps = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                var eventsReadable = true;
                try
                {
                    await _events.ReadAsync(EventPath(conversationId), turnId, 0, int.MaxValue, item =>
                    {
                        if (item["kind"]?.ToString() == "turn_finished" && item["data"] is JsonObject finished
                            && finished["status"] is JsonValue value && value.TryGetValue<string>(out var finalStatus) && IsTerminal(finalStatus))
                        {
                            terminal = item;
                        }

                        var kind = item["kind"]?.ToString() ?? string.Empty;
                        hasUserMessage |= kind == "user_message";
                        if (item["stepId"]?.GetValue<string>() is { } step)
                        {
                            if (kind.EndsWith("_started", StringComparison.Ordinal) && item["data"] is JsonObject) openSteps[step] = item;
                            else if (kind.EndsWith("_finished", StringComparison.Ordinal)) openSteps.Remove(step);
                        }
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
                {
                    eventsReadable = false;
                    turn["warning"] = new JsonObject { ["code"] = "AI_EVENT_RECORD_UNREADABLE", ["message"] = "The event journal is unreadable. Other conversations remain available; no call was replayed." };
                }

                turn["status"] = terminal?["data"]?["status"]?.DeepClone() ?? JsonValue.Create("interrupted");
                if (terminal?["data"]?["answer"] is { } answer)
                    turn["answer"] = answer.DeepClone();
                turn.Remove("pendingUserPrompt");
                AiToolJournal.Recover(turn);
                turn["error"] = terminal is null
                    ? new JsonObject { ["code"] = "AI_HOST_INTERRUPTED", ["message"] = "The owning host exited. Durable partial events and tool results are retained; nothing was replayed." }
                    : terminal["data"]?["error"]?.DeepClone();
                if (terminal is null && eventsReadable)
                {
                    // The queued snapshot precedes the user message commit. Preserve the prompt if the host died between them.
                    if (!hasUserMessage && turn["prompt"] is not null)
                        await AppendUserMessageAsync(turn, ct).ConfigureAwait(false);
                    foreach (var (step, started) in openSteps.Reverse())
                    {
                        var receipt = started["data"]?.DeepClone().AsObject() ?? [];
                        receipt["status"] = "interrupted";
                        receipt["recovered"] = true;
                        receipt["partial"] = true;
                        receipt["resultUnavailable"] = true;
                        await AppendEventAsync(turn, started["kind"]!.GetValue<string>().Replace("_started", "_finished", StringComparison.Ordinal), step, receipt, ct).ConfigureAwait(false);
                    }

                    await AppendEventAsync(turn, "turn_finished", null, new JsonObject { ["status"] = "interrupted", ["error"] = turn["error"]!.DeepClone() }, ct).ConfigureAwait(false);
                }

                await SaveTurnAsync(turn, ct).ConfigureAwait(false);
            }
        }
    }

    public static void Writable(JsonObject record)
    {
        if (record["schemaVersion"]?.GetValue<int>() != 1)
            throw new LauncherOperationException("AI_CHAT_VERSION_UNSUPPORTED", "This chat record is viewable but needs a newer launcher for changes.");
    }

    private static async Task WriteRecordAsync(string file, JsonObject record, CancellationToken ct)
    {
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        await JsonFiles.WriteAsync(file, record, ct).ConfigureAwait(false);
    }
    private static JsonObject ReadRecord(string file, string code, string message, bool visibleTurn = false)
    {
        // Queries race the independent turn writer, not the serialized request dispatcher.
        using var lease = ResourceLease.AcquireAsync(file).GetAwaiter().GetResult();
        try { return visibleTurn ? JsonFiles.ReadSnapshot(file, ReadVisibleTurn) : JsonFiles.ReadObject(file); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { throw new LauncherOperationException(code, message); }
    }

    // Public history never needs provider continuation data. Skip those top-level values before creating
    // JsonNodes, so retained public fields cannot keep an entire private protocol document alive.
    private static JsonObject ReadVisibleTurn(Stream stream)
    {
        var length = checked((int)stream.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var bytes = buffer.AsSpan(0, length);
            stream.ReadExactly(bytes);
            if (bytes.StartsWith("\uFEFF"u8)) bytes = bytes[3..];
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a turn object.");
            var result = new JsonObject();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (reader.Read()) throw new JsonException("Unexpected data after the turn object.");
                    return result;
                }

                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a turn property.");
                var name = reader.GetString()!;
                if (!names.Add(name) || !reader.Read()) throw new JsonException("Invalid or repeated turn property.");
                if (name is "wireHistory" or "providerState") reader.Skip();
                else result.Add(name, JsonNode.Parse(ref reader));
            }

            throw new JsonException("The turn object is incomplete.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
    private static void PageBounds(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw new ArgumentException("Offset must be nonnegative; limit must be 1 through 100.");
    }
}