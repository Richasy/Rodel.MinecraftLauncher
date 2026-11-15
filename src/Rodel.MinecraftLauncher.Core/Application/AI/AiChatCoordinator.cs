// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiChatCoordinator(LauncherPaths paths, SemaphoreSlim generation, AiLocalInferenceRuntime localInference)
{
    private readonly AiChatStore _store = new(paths);
    private readonly AiConversationTitles _titles = new(paths);
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private string? _activeTurn;
    private string? _activeConversation;
    private Exception? _storageFailure;
    private readonly List<PendingTurn> _pending = [];
    private FileStream? _ownership;
    private Task? _worker;
    private bool _stopping;

    /// <summary>Gets whether this host owns executing or queued chat work.</summary>
    public bool IsActive => _ownership is not null;
    internal bool HasBackgroundWork => _titles.HasWork;
    public string? StorageFailure => _storageFailure is null ? null : "AI_CHAT_STORAGE_FAILURE";

    /// <summary>Dispatches typed reads and mutation receipts under the conversation operation lease.</summary>
    public async Task<object> ExecuteAsync(AiChatOperation request, CancellationToken ct)
    {
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_storageFailure is not null && request is AiShowTurnOperation or AiReadEventsOperation or AiReadCompletedTurnEventsOperation)
                throw new LauncherOperationException("AI_CHAT_STORAGE_FAILURE", "The active turn could not persist its state. Read durable records offline, repair storage, then restart the host. No operation was replayed.");
            var conversationId = request.ConversationId;
            var turnId = request.TurnId;
            using var operation = conversationId is null ? null : await ResourceLease.AcquireAsync(
                Path.Combine(paths.Root, "ai", "conversation-operations", AiChatStore.CheckId(conversationId)), ct).ConfigureAwait(false);
            switch (request)
            {
                case AiCreateConversationOperation creation:
                    {
                        var create = creation.Request;
                        var repairs = new LauncherClient(paths).Repairs;
                        using var repairLease = create.RepairId is null ? null : await repairs.AcquireAsync(create.RepairId, ct).ConfigureAwait(false);
                        var repair = create.RepairId is null ? null : repairs.ReadDocument(create.RepairId);
                        if (repair is not null)
                        {
                            if (create.SkinScopeId is not null || (create.InstanceId is not null && create.InstanceId != repair["instanceId"]!.ToString()))
                                throw new ArgumentException("The repair keeps its original instance and cannot be used for skin editing.");
                            if (repair["conversationId"]?.ToString() is { } existing) return _store.Get(existing);
                            create = create with { InstanceId = repair["instanceId"]!.ToString() };
                        }

                        var config = await new AiServiceStore(paths).GetAsync(ct).ConfigureAwait(false);
                        var modelId = create.ModelId ?? config.DefaultModelId ?? throw new LauncherOperationException("AI_DEFAULT_MODEL_MISSING", "Select a model for this conversation.");
                        if (!config.Services.Any(service => service.Enabled && service.Models.Any(model => model.Id == modelId)))
                            throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "No enabled configured model has this identifier.");
                        if (create.Title?.Length > 200) throw new ArgumentException("A conversation title can contain at most 200 characters.");
                        if (create.InstanceId is not null && create.SkinScopeId is not null)
                            throw new ArgumentException("Choose an instance or skin scope for the conversation.");
                        if (create.SkinScopeId is not null)
                            new Skins.SkinService(paths).Proposals.CheckBinding(create.SkinScopeId);
                        if (create.InstanceId is not null) _ = new LauncherClient(paths).Instances.Get(create.InstanceId);
                        var game = create.GameSessionId is null ? null : new LauncherClient(paths).Sessions.Get(create.GameSessionId);
                        if (game is not null && (create.InstanceId is null || game.Status != "running" || game.ProcessId is null || game.ProcessStartUtc is null
                            || game.InstanceId != create.InstanceId || create.SkinScopeId is not null || create.RepairId is not null))
                        {
                            throw new ArgumentException("A temporary game conversation must belong to its running game session and instance.");
                        }

                        var conversation = new JsonObject
                        {
                            ["schemaVersion"] = 1,
                            ["id"] = Guid.NewGuid().ToString("N"),
                            ["title"] = create.Title,
                            ["titleSource"] = create.Title is null && create.GenerateTitle ? "pending" : "manual",
                            ["modelId"] = modelId,
                            ["instanceId"] = create.InstanceId,
                            ["gameSessionId"] = create.GameSessionId,
                            ["gameProcessId"] = game?.ProcessId,
                            ["gameProcessStartUtc"] = game?.ProcessStartUtc,
                            ["skinScopeId"] = create.SkinScopeId,
                            ["repairId"] = create.RepairId,
                            ["fileAccessEnabled"] = repair is not null ? true : null,
                            ["networkAccessEnabled"] = repair is not null ? true : null,
                            ["revision"] = 1L,
                            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                            ["mode"] = "read_only"
                        };
                        await _store.SaveConversationAsync(conversation, ct).ConfigureAwait(false);
                        if (repair is not null)
                        {
                            repair["conversationId"] = conversation["id"]!.DeepClone();
                            await repairs.SaveAsync(repair, ct).ConfigureAwait(false);
                        }

                        return AiChatRecord.Read(conversation);
                    }
                case AiListConversationsOperation listing:
                    return _store.List(listing.Offset, listing.Limit);
                case AiShowConversationOperation:
                    return _store.Get(Required(conversationId)) with { Turns = _store.Turns(Required(conversationId)).Items };
                case AiUpdateConversationOperation update:
                    return await new AiConversationHistory(paths).UpdateAsync(update.Request, ct).ConfigureAwait(false);
                case AiReviewRemovalOperation:
                    return new AiConversationHistory(paths).ReviewRemoval(Required(conversationId));
                case AiExportConversationOperation export:
                    return await new AiConversationHistory(paths).ExportAsync(export.Request, ct).ConfigureAwait(false);
                case AiRemoveConversationOperation remove:
                    var removal = remove.Request;
                    var review = new AiConversationHistory(paths).ReviewRemoval(removal.ConversationId);
                    if (!removal.Confirm || review.Fingerprint != removal.Fingerprint
                        || review.Revision.GetValue<long>() != removal.ExpectedRevision)
                    {
                        throw new LauncherOperationException("AI_HISTORY_REVIEW_CHANGED", "Confirm the exact current deletion review.",
                            JsonSerializer.SerializeToNode(review, AiJsonContext.Default.AiConversationRemovalReview));
                    }

                    AiChatStore.Writable(_store.GetDocument(removal.ConversationId));
                    foreach (var pending in _pending.Where(item => item.Request.ConversationId == removal.ConversationId).ToArray())
                        await CancelPendingAsync(pending).ConfigureAwait(false);
                    if (IsActive && _activeConversation == removal.ConversationId)
                    {
                        await _activeCancellation!.CancelAsync().ConfigureAwait(false);
                        await _activeTask!.WaitAsync(ct).ConfigureAwait(false);
                    }

                    var foreign = _store.TurnDocuments(removal.ConversationId)
                        .Where(item => !AiChatStore.IsTerminal(item?["status"]?.ToString())).Select(item => item!["id"]!.ToString()).ToArray();
                    foreach (var active in foreign)
                        await _store.SignalCancellationAsync(removal.ConversationId, active, ct).ConfigureAwait(false);
                    while (foreign.Any(active => !AiChatStore.IsTerminal(_store.GetTurn(removal.ConversationId, active).Status)))
                    {
                        if (foreign.Any(active => !AiGenerationLease.OwnerAlive(_store.GetTurnDocument(removal.ConversationId, active))))
                        {
                            using var recoveryLease = AiGenerationLease.Acquire(paths);
                            await _store.RecoverInterruptedAsync(ct, removal.ConversationId).ConfigureAwait(false);
                        }

                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }

                    if (_storageFailure is not null)
                        throw new LauncherOperationException("AI_CHAT_STORAGE_FAILURE", "Repair the failed record store before removing this conversation's recovery evidence.");
                    return await new AiConversationHistory(paths).RemoveAsync(removal, ct).ConfigureAwait(false);
                case AiShowTurnOperation:
                    return _store.GetTurn(Required(conversationId), Required(turnId));
                case AiReadEventsOperation events:
                    return await _store.EventsAsync(Required(conversationId), Required(turnId), events.After, events.Limit, ct).ConfigureAwait(false);
                case AiReadCompletedTurnEventsOperation:
                    return await _store.CompletedEventsAsync(Required(conversationId), Required(turnId), ct).ConfigureAwait(false);
                case AiReadArchiveOperation archive:
                    return new AiContextArchives(paths).Read(archive.Request);
                case AiCheckEffectsOperation:
                    return await new AiToolJournal(paths).CheckAsync(Required(conversationId), Required(turnId), ct).ConfigureAwait(false);
                case AiAcknowledgeEffectsOperation acknowledge:
                    if (IsActive && _activeTurn == turnId)
                        throw new LauncherOperationException("AI_EFFECT_CHECK_BUSY", "The owning turn is still closing its tools. Wait for its final state before acknowledging uncertainty.");
                    return await new AiToolJournal(paths).AcknowledgeAsync(acknowledge.Request, ct).ConfigureAwait(false);
                case AiRespondOperation respond:
                    return await new AiUserPrompts(paths).RespondAsync(respond.Request, ct).ConfigureAwait(false);
                case AiConfirmOperation confirm:
                    return await new AiToolApprovals(paths).DecideAsync(confirm.Request, ct).ConfigureAwait(false);
                case AiReadConfirmationOperation confirmation:
                    return new AiToolApprovals(paths).Read(confirmation.Request);
                case AiSendOperation send:
                    if (_storageFailure is not null)
                        throw new LauncherOperationException("AI_CHAT_STORAGE_FAILURE", "The previous turn could not persist its final state. Repair the record store and restart the host; no turn was replayed.");
                    return await SendAsync(send.Request, ct).ConfigureAwait(false);
                case AiCancelOperation cancel:
                    var cancellationRequest = cancel.Request;
                    var record = _store.GetTurnDocument(Required(conversationId), Required(turnId));
                    if (cancellationRequest.CancelQueued)
                    {
                        foreach (var queued in _pending.Where(item => item.Request.ConversationId == conversationId).ToArray())
                            await CancelPendingAsync(queued).ConfigureAwait(false);
                        // The client may still display a turn that just finished while its successor started.
                        if (_activeConversation == conversationId && _activeTurn != turnId && _activeTurn is { } current && _activeTask is { IsCompleted: false })
                        {
                            await _store.SignalCancellationAsync(Required(conversationId), current, ct).ConfigureAwait(false);
                            _activeCancellation!.Cancel();
                        }
                    }

                    record = _store.GetTurnDocument(Required(conversationId), Required(turnId));
                    if (!AiChatStore.IsTerminal(record["status"]?.ToString()))
                    {
                        var pending = _pending.FirstOrDefault(item => item.Turn["id"]!.ToString() == turnId);
                        if (pending is not null)
                        {
                            await CancelPendingAsync(pending).ConfigureAwait(false);
                            return AiChatRecord.Read(pending.Turn.DeepClone().AsObject());
                        }

                        await _store.SignalCancellationAsync(Required(conversationId), Required(turnId), ct).ConfigureAwait(false);
                        if (_activeTurn == turnId) _activeCancellation!.Cancel();
                        return new AiChatRecord { TurnId = turnId, Status = "canceling" };
                    }

                    return AiChatRecord.Read(record);
                case AiRunQueuedTurnOperation:
                    var selected = _store.GetTurnDocument(Required(conversationId), Required(turnId));
                    if (selected["status"]?.ToString() != "queued") return AiChatRecord.Read(selected);
                    if (_storageFailure is not null || _stopping)
                        throw new LauncherOperationException("AI_QUEUE_UNAVAILABLE", "The host cannot start queued work while stopping or recovering a storage failure.");
                    var promoted = _pending.FirstOrDefault(item => item.Request.ConversationId == conversationId && item.Turn["id"]!.ToString() == turnId)
                        ?? throw new LauncherOperationException("AI_QUEUE_OWNER_UNAVAILABLE", "The queued message is not owned by this host. Read its current state before continuing.");
                    await _store.AppendEventAsync(promoted.Turn, "turn_priority_requested", null, [], ct).ConfigureAwait(false);
                    if (_activeConversation == conversationId && _activeTurn is { } executingTurn && _activeTask is { IsCompleted: false })
                        await _store.SignalCancellationAsync(Required(conversationId), executingTurn, ct).ConfigureAwait(false);
                    _pending.Remove(promoted);
                    _pending.Insert(0, promoted);
                    if (_activeConversation == conversationId && _activeTask is { IsCompleted: false }) _activeCancellation!.Cancel();
                    return AiChatRecord.Read(promoted.Turn.DeepClone().AsObject());
                default:
                    throw new LauncherOperationException("AI_CHAT_METHOD_UNIMPLEMENTED", "This conversation operation is not implemented.");
            }
        }
        finally { _operations.Release(); }
    }

    /// <summary>Cancels pending submissions and waits for the owned runner to close without replaying queued work.</summary>
    public async Task StopAsync()
    {
        try
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try
            {
                _stopping = true;
                foreach (var pending in _pending.ToArray()) await CancelPendingAsync(pending).ConfigureAwait(false);
            }
            finally { _activeCancellation?.Cancel(); _operations.Release(); }
        }
        finally
        {
            try { if (_worker is not null) await _worker.ConfigureAwait(false); }
            finally
            {
                await _titles.StopAsync().ConfigureAwait(false);
                _activeCancellation?.Dispose();
            }
        }
    }

    private async Task<AiChatRecord> SendAsync(AiChatSend request, CancellationToken ct)
    {
        AssistantService.ValidateRequest(new AiTextRequest { Prompt = request.Prompt, TimeoutSeconds = request.TimeoutSeconds }, request.ImageIds is { Length: > 0 });
        request = request with { ImageIds = request.ImageIds?.ToArray() };
        if (request.ImageIds?.Length > AiImageStore.MaximumImages)
            throw new ArgumentException("Attach at most three reference images to one turn.", nameof(request));
        if (!Guid.TryParseExact(request.ClientKey, "N", out _) || request.MaximumModelCalls is < 1 or > 64
            || request.MaximumOutputTokens is <= 0)
        {
            throw new ArgumentException("Use a GUID client key, 1..64 model calls, and a positive output-token limit only when explicitly specifying one.");
        }

        if (request.OwnerPid is { } pid)
        {
            if (pid <= 0 || !DateTimeOffset.TryParse(request.OwnerStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ownerStart))
                throw new ArgumentException("Client-owned turns require the owner's PID and UTC start time.");
            using var owner = Process.GetProcessById(pid);
            if (Math.Abs((owner.StartTime.ToUniversalTime() - ownerStart.UtcDateTime).TotalMilliseconds) > 1)
                throw new LauncherOperationException("AI_OWNER_IDENTITY_MISMATCH", "The client process start time does not match.");
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(request with { ExpectedRevision = 0 }, AiChatJsonContext.Default.AiChatSend))));
        var conversation = _store.GetDocument(request.ConversationId);
        AiChatStore.Writable(conversation);
        if (conversation["gameSessionId"]?.GetValue<string>() is { } gameSession && request.GameContext?.SessionId != gameSession)
            throw new LauncherOperationException("AI_GAME_CHAT_SCOPE_MISMATCH", "This temporary conversation belongs to another game session.");
        if (request.GameContext is { } game)
        {
            var client = new LauncherClient(paths);
            if (client.Sessions.Get(game.SessionId).InstanceId != conversation["instanceId"]?.ToString())
                throw new LauncherOperationException("AI_INSTANCE_SCOPE", "The game does not belong to this conversation's instance.");
            var live = await client.GameBridge.SnapshotAsync(game.SessionId, ct).ConfigureAwait(false);
            if (!live.Ready || AiGameContext.FromSnapshot(live) != game)
                throw new LauncherOperationException("AI_GAME_WORLD_CHANGED", "The observed game world changed before this message was submitted.");
        }

        string? retryGoal = null;
        if (request.RetryTurnId is { } retryId)
        {
            var prior = await new AiToolJournal(paths).CheckDocumentAsync(request.ConversationId, retryId, ct).ConfigureAwait(false);
            if (!AiChatStore.IsTerminal(prior["status"]?.ToString()) || prior["status"]?.ToString() == "completed")
                throw new LauncherOperationException("AI_RETRY_NOT_AVAILABLE", "Retry applies to a terminal unsuccessful turn, not a running or completed operation.");
            if (prior["unknownEffects"] is JsonArray { Count: > 0 })
                throw new LauncherOperationException("AI_EFFECT_REVIEW_REQUIRED", "Resolve the previous turn's unknown effects before retrying. Nothing was replayed.");
            if (request.Prompt != prior["prompt"]?.GetValue<string>())
                throw new ArgumentException("A retry must retain the original prompt. Use a new turn for an edited request.");
            var previousImages = (prior["images"] as JsonArray ?? []).Select(image => image!["id"]!.ToString());
            if (!previousImages.SequenceEqual(request.ImageIds ?? []))
                throw new ArgumentException("A retry must retain the original reference images. Use a new turn for different images.");
            retryGoal = prior["goalId"]?.ToString() ?? prior["id"]!.ToString();
        }

        var old = _store.TurnDocuments(request.ConversationId).FirstOrDefault(item => item?["clientKey"]?.ToString() == request.ClientKey);
        if (old is not null)
        {
            if (old["requestFingerprint"]?.ToString() != fingerprint)
                throw new LauncherOperationException("AI_IDEMPOTENCY_CONFLICT", "This client key already belongs to different turn content or options.");
            return AiChatRecord.Read(old.DeepClone().AsObject());
        }

        if (conversation["revision"]!.GetValue<long>() != request.ExpectedRevision)
            throw new LauncherOperationException("AI_CONFIGURATION_CONFLICT", "The conversation changed. Read its current revision before sending.");
        if (_stopping) throw new LauncherOperationException("AI_HOST_STOPPING", "The AI host is stopping; this turn was not queued.");
        var acquired = !IsActive;
        if (acquired && !await generation.WaitAsync(0, ct).ConfigureAwait(false))
            throw new LauncherOperationException("AI_BUSY", "This data root already has an active model request; this turn was not queued.");
        AiServiceSnapshot? snapshot = null;
        JsonObject? turn = null;
        FileStream? ownership = null;
        var dispatched = false;
        try
        {
            if (acquired) ownership = AiGenerationLease.Acquire(paths);
            conversation = _store.GetDocument(request.ConversationId);
            old = _store.TurnDocuments(request.ConversationId).FirstOrDefault(item => item?["clientKey"]?.ToString() == request.ClientKey);
            if (old is not null)
            {
                if (old["requestFingerprint"]?.ToString() != fingerprint)
                    throw new LauncherOperationException("AI_IDEMPOTENCY_CONFLICT", "This client key already belongs to different turn content.");
                return AiChatRecord.Read(old.DeepClone().AsObject());
            }

            if (conversation["revision"]!.GetValue<long>() != request.ExpectedRevision)
                throw new LauncherOperationException("AI_CONFIGURATION_CONFLICT", "The conversation changed before this request acquired execution ownership.");
            snapshot = await new AiServiceStore(paths).AcquireAsync(null, request.ModelId ?? conversation["modelId"]!.ToString(), true, ct).ConfigureAwait(false);
            request = request with { Prompt = SecretScrubber.Scrub(request.Prompt, snapshot.Credential.Value) };
            var images = (request.ImageIds ?? []).Select(new AiImageStore(paths).Describe).ToArray();
            using var runtimeProcess = Process.GetCurrentProcess();
            turn = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["id"] = Guid.NewGuid().ToString("N"),
                ["conversationId"] = request.ConversationId,
                ["clientKey"] = request.ClientKey,
                ["requestFingerprint"] = fingerprint,
                ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["prompt"] = request.Prompt,
                ["images"] = JsonSerializer.SerializeToNode(images, AiChatJsonContext.Default.AiChatImageArray),
                ["modelId"] = snapshot.Model!.Id,
                ["service"] = new JsonObject { ["id"] = snapshot.Service.Id, ["displayName"] = snapshot.Service.DisplayName, ["protocol"] = snapshot.Service.Protocol, ["revision"] = snapshot.Service.Revision },
                ["status"] = "queued",
                ["ownership"] = request.OwnerPid is null ? "host_owned" : "client_owned",
                ["runtimeProcessId"] = Environment.ProcessId,
                ["runtimeStartedUtc"] = runtimeProcess.StartTime.ToUniversalTime().ToString("O"),
                ["instanceId"] = conversation["instanceId"]?.DeepClone(),
                ["gameSessionId"] = request.GameContext?.SessionId,
                ["skinScopeId"] = conversation["skinScopeId"]?.DeepClone(),
                ["repairId"] = conversation["repairId"]?.DeepClone(),
                ["mode"] = request.AllowOperations ? JsonValue.Create("allow_operations") : conversation["mode"]?.DeepClone() ?? JsonValue.Create("read_only"),
                ["operationRevision"] = conversation["operationRevision"]?.DeepClone() ?? JsonValue.Create(0L),
                ["turnAuthorization"] = request.AllowOperations ? "explicit_turn" : "conversation",
                ["fileAccessEnabled"] = conversation["fileAccessEnabled"]?.DeepClone() ?? JsonValue.Create(snapshot.FileAccessEnabled),
                ["networkAccessEnabled"] = conversation["networkAccessEnabled"]?.DeepClone() ?? JsonValue.Create(snapshot.NetworkAccessEnabled),
                ["toolsEnabled"] = request.UseTools,
                ["userPromptsEnabled"] = request.AllowUserPrompts,
                ["retryOf"] = request.RetryTurnId,
                ["budgets"] = new JsonObject { ["maximumModelCalls"] = request.MaximumModelCalls, ["maximumOutputTokens"] = request.MaximumOutputTokens, ["contextPolicy"] = "provider_tokens" }
            };
            if (request.GameContext is { } binding)
            {
                turn["gameWorldBindings"] = new JsonObject
                {
                    [binding.SessionId] = new JsonObject
                    {
                        ["gameIncarnation"] = binding.GameIncarnation,
                        ["worldEpoch"] = binding.WorldEpoch,
                        ["localWorldEpoch"] = binding.LocalWorldEpoch,
                        ["dimension"] = binding.Dimension
                    }
                };
            }

            turn["goalId"] = retryGoal is null ? turn["id"]!.DeepClone() : JsonValue.Create(retryGoal);
            await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
            await _store.AppendUserMessageAsync(turn, ct).ConfigureAwait(false);
            conversation["revision"] = request.ExpectedRevision + 1;
            conversation["modelId"] = snapshot.Model.Id;
            if (conversation["titleSource"]?.ToString() != "pending")
                conversation["title"] ??= JsonValue.Create(request.Prompt[..Math.Min(request.Prompt.Length, 60)]);
            await _store.SaveConversationAsync(conversation, ct).ConfigureAwait(false);
            var receipt = AiChatRecord.Read(turn.DeepClone().AsObject());
            _pending.Add(new(conversation.DeepClone().AsObject(), turn, snapshot, request));
            if (acquired)
            {
                _ownership = ownership;
                _worker = Task.Run(DrainAsync, CancellationToken.None);
            }

            dispatched = true;
            return receipt;
        }
        catch
        {
            if (turn is not null)
            {
                turn["status"] = "failed";
                turn["error"] = new JsonObject { ["code"] = "AI_SUBMISSION_NOT_COMMITTED", ["message"] = "The turn was not dispatched. Its submission record could not be committed." };
                await _store.SaveTurnAsync(turn, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (!dispatched)
            {
                snapshot?.Dispose();
                ownership?.Dispose();
                if (acquired) generation.Release();
            }
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try
            {
                _activeTurn = _activeConversation = null;
                if (_pending.Count == 0)
                {
                    _ownership!.Dispose(); _ownership = null; generation.Release();
                    return;
                }

                var next = _pending[0];
                _pending.RemoveAt(0);
                _activeCancellation?.Dispose();
                _activeCancellation = new();
                _activeTurn = next.Turn["id"]!.ToString();
                _activeConversation = next.Request.ConversationId;
                _activeTask = RunOwnedAsync(next, _activeCancellation);
            }
            finally { _operations.Release(); }

            await _activeTask.ConfigureAwait(false);
        }
    }

    private async Task CancelPendingAsync(PendingTurn pending)
    {
        pending.Turn["status"] = "canceled";
        pending.Turn["notDispatched"] = true;
        try
        {
            await _store.AppendEventAsync(pending.Turn, "turn_finished", null, new JsonObject { ["status"] = "canceled" }).ConfigureAwait(false);
            await _store.SaveTurnAsync(pending.Turn).ConfigureAwait(false);
        }
        catch (Exception error) { _storageFailure = error; throw; }

        _pending.Remove(pending);
        pending.Snapshot.Dispose();
    }

    private async Task<string?> ContinueQueuedAsync(string conversationId, CancellationToken ct)
    {
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var next = _pending.FirstOrDefault(item => item.Request.ConversationId == conversationId);
            if (next is null || _stopping || _storageFailure is not null) return null;
            // Finish the current tool batch before handing its complete history to this frozen submission.
            // Other conversations neither interrupt this one nor enter its model context.
            _pending.Remove(next);
            _pending.Insert(0, next);
            return next.Turn["id"]!.ToString();
        }
        finally { _operations.Release(); }
    }

    private async Task RunOwnedAsync(PendingTurn pending, CancellationTokenSource cancellation)
    {
        var (conversation, turn, snapshot, request) = pending;
        using var stopMonitor = new CancellationTokenSource();
        var monitor = WatchOwnerAsync(request, cancellation, stopMonitor.Token);
        var cancellationSignal = WatchCancellationAsync(request.ConversationId, turn["id"]!.ToString(), cancellation, stopMonitor.Token);
        try
        {
            // A queue reads its predecessor's committed history, including a handoff at a model-call boundary.
            // It never restarts an old tool call.
            if (_storageFailure is not null)
            {
                turn["status"] = "interrupted";
                turn["notDispatched"] = true;
                turn["error"] = new JsonObject { ["code"] = "AI_CHAT_STORAGE_FAILURE", ["message"] = "A preceding turn could not persist its final state. This queued turn was not dispatched." };
                await _store.SaveTurnAsync(turn).ConfigureAwait(false);
                return;
            }

            if (_store.HasCancellationSignal(request.ConversationId, turn["id"]!.ToString())) cancellation.Cancel();
            // Context/permission fields stay frozen; only title metadata may have been generated by an earlier turn.
            var latest = _store.GetDocument(request.ConversationId);
            conversation["titleSource"] = latest["titleSource"]?.DeepClone();
            var predecessors = _store.TurnDocuments(request.ConversationId)
                .Where(item => item["id"]?.ToString() != turn["id"]!.ToString()
                    && item["notDispatched"]?.GetValue<bool>() != true && AiChatStore.IsTerminal(item["status"]?.ToString()))
                .OrderBy(item => item["executionOrder"]?.GetValue<long>() ?? 0L)
                .ThenBy(item => item["createdUtc"]?.ToString(), StringComparer.Ordinal).ToArray();
            turn["executionOrder"] = (predecessors.LastOrDefault()?["executionOrder"]?.GetValue<long>() ?? 0L) + 1L;
            turn["previousTurnId"] = predecessors.LastOrDefault()?["id"]?.DeepClone();
            _titles.Start(conversation, snapshot, request.Prompt, cancellation.Token);
            await new AiChatRunner(paths, localInference).RunAsync(conversation, turn, snapshot, request,
                token => ContinueQueuedAsync(request.ConversationId, token), cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // A store failure cannot be turned into success or followed by unrecorded tool dispatch.
            _storageFailure = error;
        }
        finally
        {
            await stopMonitor.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(monitor, cancellationSignal).ConfigureAwait(false);
            snapshot.Dispose();
        }
    }

    private sealed record PendingTurn(JsonObject Conversation, JsonObject Turn, AiServiceSnapshot Snapshot, AiChatSend Request);

    private async Task WatchCancellationAsync(string conversation, string turn, CancellationTokenSource cancellation, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (_store.HasCancellationSignal(conversation, turn))
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return;
                }

                await Task.Delay(100, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static async Task WatchOwnerAsync(AiChatSend request, CancellationTokenSource cancellation, CancellationToken stop)
    {
        if (request.OwnerPid is null) return;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var process = Process.GetProcessById(request.OwnerPid.Value);
                    if (process.HasExited || !DateTimeOffset.TryParse(request.OwnerStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
                        || Math.Abs((process.StartTime.ToUniversalTime() - start.UtcDateTime).TotalMilliseconds) > 1)
                    { await cancellation.CancelAsync().ConfigureAwait(false); return; }
                }
                catch (ArgumentException) { await cancellation.CancelAsync().ConfigureAwait(false); return; }

                await Task.Delay(250, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private static string Required(string? value) => value ?? throw new ArgumentException("Missing conversation or turn identifier.");
}