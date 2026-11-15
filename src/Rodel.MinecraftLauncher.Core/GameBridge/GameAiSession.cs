// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Owns temporary, per-world conversations for one game process while reusing the Core conversation runtime.</summary>
internal sealed class GameAiSession(LauncherPaths paths, string session)
{
    private readonly AssistantService _assistant = new(paths);

    internal async Task<GameAiState> HandleAsync(GameAiRequest request, CancellationToken ct)
    {
        Validate(request);
        var client = new LauncherClient(paths);
        var instance = client.Sessions.Get(session).InstanceId;
        var live = await client.GameBridge.SnapshotAsync(session, ct).ConfigureAwait(false);
        if (!live.Ready || request.Snapshot.SessionId != session || AiGameContext.FromSnapshot(live) != AiGameContext.FromSnapshot(request.Snapshot))
            throw new LauncherOperationException("AI_GAME_WORLD_CHANGED", "The game world changed. Read the current world before sending another request.");

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(instance + "\0" + request.WorldKey)));
        var quick = request.Action == "quick" ? request.RequestId : request.QuickId;
        var file = Path.Combine(AiGameHistory.SlotsFor(paths, session), key + ".json");
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        var slot = File.Exists(file) ? JsonFiles.ReadSnapshot(file, stream => JsonSerializer.Deserialize(stream, GameAiSlotJsonContext.Default.GameAiSlot)!) : new GameAiSlot();
        var config = await _assistant.Services.GetAsync(ct).ConfigureAwait(false);
        var model = config.Services.Where(service => service.Enabled).SelectMany(service => service.Models).FirstOrDefault(item => item.Id == config.DefaultModelId);
        if (slot.ClearingConversationId is not null) slot = await FinishResetAsync(file, slot, ct).ConfigureAwait(false);
        if (slot.ConversationId is { } retained)
        {
            try
            {
                var retainedConversation = await _assistant.GetConversationAsync(retained, ct: ct).ConfigureAwait(false);
                if (retainedConversation.GameSessionId != session)
                    throw new LauncherOperationException("AI_GAME_CHAT_SCOPE_MISMATCH", "This conversation does not belong to the current game session.");
            }
            catch (LauncherOperationException error) when (error.Code == "AI_CONVERSATION_NOT_FOUND")
            {
                slot = slot with { ConversationId = null, Replies = [] };
                await SaveAsync(file, slot, ct).ConfigureAwait(false);
            }
        }

        if (request.Action == "new")
        {
            if (quick is not null) throw new ArgumentException("Only this game's current world conversation can be reset.");
            if (slot.ResetId != request.RequestId)
            {
                slot = new() { ResetId = request.RequestId, ClearingConversationId = slot.ConversationId };
                await SaveAsync(file, slot, ct).ConfigureAwait(false);
                slot = await FinishResetAsync(file, slot, ct).ConfigureAwait(false);
            }
        }

        if (request.Action is "send" or "quick")
        {
            if (slot.ConversationId is null)
            {
                if (model is null) throw new LauncherOperationException("AI_DEFAULT_MODEL_MISSING", "Select a default AI model in the launcher first.");
                var title = request.WorldName ?? "Minecraft";
                if (title.Length > 160) title = title[..160];
                var created = await _assistant.CreateConversationAsync(new() { InstanceId = instance, GameSessionId = session, ModelId = model.Id, Title = title }, ct: ct).ConfigureAwait(false);
                var configured = await _assistant.UpdateConversationAsync(new() { ConversationId = created.Id!, ExpectedRevision = created.Revision!.Value, Mode = config.DefaultOperationMode }, ct: ct).ConfigureAwait(false);
                slot = slot with { ConversationId = configured.Id };
                await SaveAsync(file, slot, ct).ConfigureAwait(false);
            }

            var conversation = await _assistant.GetConversationAsync(slot.ConversationId!, ct: ct).ConfigureAwait(false);
            var existing = conversation.Turns?.FirstOrDefault(turn => turn.ClientKey == request.RequestId);
            if (existing is not null)
            {
                if (existing.Prompt != request.Message) throw new LauncherOperationException("AI_SUBMISSION_CONFLICT", "This submission identity already contains another message.");
            }
            else
            {
                var messageHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Message!)));
                var reply = slot.Replies.FirstOrDefault(item => item.RequestId == request.RequestId);
                if (reply is not null && reply.MessageSha256 != messageHash)
                    throw new LauncherOperationException("AI_SUBMISSION_CONFLICT", "This submission identity already contains another reply.");
                var prompts = new AiUserPrompts(paths);
                var waiting = reply is null ? conversation.Turns?.FirstOrDefault(turn => turn.Status == "awaiting_user"
                    && turn.PendingUserPrompt is { } prompt && prompts.IsPending(slot.ConversationId!, turn.Id!, prompt.Id)) : null;
                if (reply is null && waiting?.PendingUserPrompt is { } question)
                {
                    reply = new(request.RequestId!, waiting.Id!, question.Id, messageHash);
                    slot = slot with { Replies = [.. slot.Replies, reply] };
                    // Persist the routing decision first: loss of the reply receipt must not turn a retry into a new turn.
                    await SaveAsync(file, slot, ct).ConfigureAwait(false);
                }

                if (reply is not null)
                {
                    await _assistant.RespondToUserPromptAsync(new(slot.ConversationId!, reply.TurnId, reply.PromptId, text: request.Message!), ct: ct).ConfigureAwait(false);
                }
                else
                {
                    if (model is null) throw new LauncherOperationException("AI_DEFAULT_MODEL_MISSING", "Select a default AI model in the launcher first.");
                    await _assistant.SendAsync(new()
                    {
                        ConversationId = slot.ConversationId!,
                        ClientKey = request.RequestId!,
                        Prompt = request.Message!,
                        ExpectedRevision = conversation.Revision!.Value,
                        ModelId = model.Id,
                        AllowOperations = true,
                        AllowUserPrompts = request.Action != "quick",
                        GameContext = AiGameContext.FromSnapshot(live)
                    }, ct: ct).ConfigureAwait(false);
                }
            }
        }

        if (request.Action is "cancel" or "confirm" or "answer")
        {
            var conversation = slot.ConversationId ?? throw new LauncherOperationException("AI_GAME_CHAT_MISSING", "This world has no conversation to act on.");
            if (request.Action == "cancel")
                await _assistant.CancelTurnAsync(new() { ConversationId = conversation, TurnId = request.TurnId!, CancelQueued = true }, ct: ct).ConfigureAwait(false);
            else if (request.Action == "confirm")
                await _assistant.ConfirmToolAsync(new() { ConversationId = conversation, TurnId = request.TurnId!, InvocationId = request.InvocationId!, Fingerprint = request.Fingerprint!, Approve = request.Approve!.Value }, ct: ct).ConfigureAwait(false);
            else
                await _assistant.RespondToUserPromptAsync(new() { ConversationId = conversation, TurnId = request.TurnId!, PromptId = request.InvocationId!, Text = request.Message ?? string.Empty, OptionIds = request.OptionIds, Skip = request.Skip }, ct: ct).ConfigureAwait(false);
        }

        return await ReadAsync(slot, quick, model, request, ct).ConfigureAwait(false);
    }

    private async Task<GameAiSlot> FinishResetAsync(string file, GameAiSlot slot, CancellationToken ct)
    {
        if (slot.ClearingConversationId is not { } previous) return slot;
        try
        {
            if ((await _assistant.GetConversationAsync(previous, ct: ct).ConfigureAwait(false)).GameSessionId != session)
                throw new LauncherOperationException("AI_GAME_CHAT_SCOPE_MISMATCH", "Only this game's temporary history can be cleared.");
            var review = await _assistant.ReviewConversationRemovalAsync(previous, ct: ct).ConfigureAwait(false);
            await _assistant.RemoveConversationAsync(new() { ConversationId = previous, ExpectedRevision = review.Revision.GetValue<long>(), Fingerprint = review.Fingerprint, Confirm = true }, ct: ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (LauncherOperationException error) when (error.Code == "AI_CONVERSATION_NOT_FOUND") { }

        slot = slot with { ClearingConversationId = null };
        await SaveAsync(file, slot, ct).ConfigureAwait(false);
        return slot;
    }

    internal async Task StopAsync()
    {
        var history = new AiGameHistory(paths);
        foreach (var conversation in history.Records(session))
        {
            try
            {
                var id = conversation["id"]!.ToString();
                var review = await _assistant.ReviewConversationRemovalAsync(id).ConfigureAwait(false);
                await _assistant.RemoveConversationAsync(new() { ConversationId = id, ExpectedRevision = review.Revision.GetValue<long>(), Fingerprint = review.Fingerprint, Confirm = true }).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or LauncherOperationException) { }
        }

        if (history.Records(session).Length == 0) AiGameHistory.RemoveSlots(paths, session);
    }

    private async Task<GameAiState> ReadAsync(GameAiSlot slot, string? quick, AiModelBinding? model, GameAiRequest request, CancellationToken ct)
    {
        if (slot.ConversationId is null) return new() { Mode = "allow_operations", ModelAvailable = model is not null, ModelName = model?.DisplayName, QuickId = quick };
        var conversation = await _assistant.GetConversationAsync(slot.ConversationId, ct: ct).ConfigureAwait(false);
        var all = conversation.Turns ?? [];
        // Console submissions share the save's context, but each observer must follow its own turn,
        // including when a later message is already queued behind it.
        var reply = quick is null ? null : slot.Replies.FirstOrDefault(item => item.RequestId == quick);
        var turns = quick is not null ? all.Where(turn => reply is null ? turn.ClientKey == quick : turn.Id == reply.TurnId).ToArray()
            : [.. all.Take(Math.Max(0, all.Length - request.Offset)).TakeLast(20)];
        var projected = new List<GameAiTurn>();
        foreach (var turn in turns)
        {
            GameAiConfirmation? confirmation = null;
            if (turn.Status == "awaiting_confirmation" && turn.AdditionalData?.GetValueOrDefault("pendingConfirmation") is { ValueKind: JsonValueKind.Object } pending)
            {
                var invocation = pending.GetProperty("id").GetString()!;
                confirmation = new()
                {
                    InvocationId = invocation,
                    Name = pending.GetProperty("name").GetString()!,
                    Fingerprint = pending.GetProperty("fingerprint").GetString()!,
                    Details = await _assistant.ReadToolConfirmationAsync(new() { ConversationId = slot.ConversationId, TurnId = turn.Id!, InvocationId = invocation }, ct: ct).ConfigureAwait(false)
                };
            }

            using var errorDocument = turn.Error is null ? null : JsonDocument.Parse(turn.Error.ToJsonString());
            projected.Add(new()
            {
                Id = turn.Id!,
                SubmissionId = reply?.RequestId ?? turn.ClientKey,
                Prompt = turn.Prompt ?? string.Empty,
                Status = turn.Status ?? "unknown",
                Answer = turn.Answer,
                Error = errorDocument?.RootElement.Clone(),
                Confirmation = confirmation,
                Question = turn.Status == "awaiting_user" && turn.PendingUserPrompt is { } prompt
                    && new AiUserPrompts(paths).IsPending(slot.ConversationId, turn.Id!, prompt.Id) ? prompt : null
            });
        }

        var eventTurn = request.TurnId is { } selected ? all.FirstOrDefault(turn => turn.Id == selected) : all.LastOrDefault();
        var events = eventTurn is null || quick is not null ? null : await _assistant.GetEventsAsync(slot.ConversationId, eventTurn.Id!, request.After, ct: ct).ConfigureAwait(false);
        if (events is not null) events = events with { Items = [.. events.Items.Where(item => item.Kind is "message_started" or "text_delta")] };
        var result = new GameAiState
        {
            ConversationId = slot.ConversationId,
            QuickId = quick,
            Mode = "allow_operations",
            ModelName = model?.DisplayName,
            ModelAvailable = model is not null,
            Turns = [.. projected],
            HasOlder = quick is null && all.Length > request.Offset + turns.Length,
            EventTurnId = eventTurn?.Id,
            Events = events
        };
        // Keep chronological pages below the existing two-MiB transport bound without clipping a message.
        while (JsonSerializer.SerializeToUtf8Bytes(result, GameAiJsonContext.Default.GameAiState).Length > FramedJsonPipe.MaximumFrameBytes * 3 / 4)
        {
            if (result.Turns.Length <= 1)
                throw new LauncherOperationException("GAME_AI_MESSAGE_TOO_LARGE", "This response exceeds the game bridge's message limit. Read the complete conversation in the launcher.");
            result = result with { Turns = result.Turns[1..], HasOlder = true };
        }

        return result;
    }

    private static Task SaveAsync(string file, GameAiSlot slot, CancellationToken ct) => JsonFiles.WriteBytesAsync(file, JsonSerializer.SerializeToUtf8Bytes(slot, GameAiSlotJsonContext.Default.GameAiSlot), ct);

    private static void Validate(GameAiRequest request)
    {
        if (request.Action is not ("open" or "send" or "quick" or "read" or "new" or "cancel" or "confirm" or "answer")
            || string.IsNullOrWhiteSpace(request.WorldKey) || request.WorldKey.Length > 4096 || request.Offset < 0 || request.After < 0)
        {
            throw new ArgumentException("A known game AI action and a bounded save identity are required.");
        }

        if (request.QuickId is { } quick) GameBridgeService.ValidateId(quick);
        if (request.Action is "send" or "quick" or "new") GameBridgeService.ValidateId(request.RequestId ?? string.Empty);
        if (request.Action is "send" or "quick") AssistantService.ValidateRequest(new() { Prompt = request.Message ?? string.Empty });
        if (request.Action is "cancel" or "confirm" or "answer") GameBridgeService.ValidateId(request.TurnId ?? string.Empty);
        if (request.Action is "confirm" or "answer") GameBridgeService.ValidateId(request.InvocationId ?? string.Empty);
        if (request.Action == "confirm" && (request.Approve is null || string.IsNullOrWhiteSpace(request.Fingerprint)))
            throw new ArgumentException("An exact fingerprint and an explicit decision are required.");
    }
}