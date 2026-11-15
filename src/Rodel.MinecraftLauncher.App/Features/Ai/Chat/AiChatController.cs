// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Submits idempotent Core turns and observes durable events without replaying effects.</summary>
internal sealed class AiChatController(AiUiState state) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, Task> _observers = [];
    private readonly HashSet<string> _titleObservers = [];
    private readonly HashSet<string> _failedEffectChecks = [];
    private readonly Dictionary<string, (int Version, Task Task, CancellationTokenSource Lifetime)> _historyLoads = [];

    internal bool EffectCheckFailed(string turnId) => _failedEffectChecks.Contains(turnId);

    internal async Task OpenRepairAsync(AiRepairCreate request, string title)
    {
        if (!state.Loaded)
            await state.LoadAsync();
        await state.RunAsync(async () =>
            {
                var repair = await state.Service.StartRepairAsync(request, state.Configuration.Defaults.ModelId, title, ct: _lifetime.Token);
                var conversation = state.Conversations.FirstOrDefault(c => c.RepairId == repair.Id) ?? new AiUiConversation();
                if (!state.Conversations.Contains(conversation))
                    state.Conversations.Insert(0, conversation);
                conversation.RepairId = repair.Id;
                conversation.RepairRequest = request;
                conversation.Repair = repair;
                conversation.Name = title;
                conversation.InstanceId = request.InstanceId;
                conversation.ModelId ??= state.Configuration.Defaults.ModelId;
                if (repair.ConversationId is { } id)
                    await ApplyAsync(conversation, await ReadConversationAsync(id));
                state.Select(conversation);
            });
    }

    internal async Task ContinueRepairAsync(AiUiConversation conversation)
    {
        if (conversation.RepairRequest is not { } request || conversation.Turns.Count > 0 || conversation.ModelId is null || state.Busy)
            return;
        await state.RunAsync(async () =>
            {
                var repair = await state.Service.StartRepairAsync(request, conversation.ModelId, conversation.Name, ct: _lifetime.Token);
                if (repair.ConversationId is { } id)
                    await ApplyAsync(conversation, await ReadConversationAsync(id));
            });
    }

    internal Task RestoreRepairAsync(AiUiConversation conversation) => state.RunAsync(async () =>
        {
            var cancellation = _lifetime.Token;
            var plan = conversation.Repair?.Plans?.LastOrDefault(p => p.AfterSettings is not null && p.State != "restored");
            if (plan is null || conversation.RepairId is null)
                return;
            await state.Repairs.RestoreAsync(new()
            {
                RepairId = conversation.RepairId,
                PlanId = plan.Id!
            }, cancellation);
            var repair = await Task.Run(() => state.Repairs.Read(conversation.RepairId), cancellation);
            cancellation.ThrowIfCancellationRequested();
            conversation.Repair = repair;
        });

    internal async Task LoadAsync()
    {
        var cancellation = _lifetime.Token;
        var offset = 0;
        do
        {
            var page = await Task.Run(() => state.Service.ListConversationsAsync(offset, 100, ct: cancellation), cancellation);
            cancellation.ThrowIfCancellationRequested();
            foreach (var record in page.Items)
            {
                if (record.Id is null || record.SkinScopeId is not null || record.GameSessionId is not null)
                    continue;
                try
                {
                    var snapshot = await ReadConversationAsync(record.Id);
                    cancellation.ThrowIfCancellationRequested();
                    var conversation = state.Conversations.FirstOrDefault(item => item.Id == record.Id);
                    if (conversation is null)
                    {
                        conversation = new();
                        state.Conversations.Add(conversation);
                    }

                    await ApplyAsync(conversation, snapshot);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    state.Report(error);
                }
            }

            offset += page.Items.Length;
            if (!page.HasMore || page.Items.Length == 0)
                break;
        }
        while (true);
        state.Notify();
    }

    internal Task SelectAsync(AiUiConversation conversation)
    {
        if (!conversation.Persisted || conversation.Sending || conversation.Removing || conversation.Removed) return Task.CompletedTask;
        var version = conversation.TranscriptVersion;
        if (_historyLoads.TryGetValue(conversation.UiKey, out var pending) && pending.Version == version) return pending.Task;
        pending.Lifetime?.Cancel();
        var timing = new HistoryLoadTiming();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var completion = new TaskCompletionSource();
        _historyLoads[conversation.UiKey] = (version, completion.Task, lifetime);
        conversation.HistoryLoading = true;
        conversation.HistoryFailed = false;
        state.ClearError();
        state.Notify();
        timing.Mark("selectionMs");
        _ = LoadSelectedAsync(conversation, version, lifetime, completion, timing);
        return completion.Task;
    }

    private async Task LoadSelectedAsync(AiUiConversation conversation, int version, CancellationTokenSource lifetime, TaskCompletionSource completion, HistoryLoadTiming timing)
    {
        var loaded = false;
        try
        {
            var record = await ReadConversationAsync(conversation.Id, lifetime.Token, timing);
            if (conversation.TranscriptVersion != version || !IsTranscriptVisible(conversation)) return;
            await ApplyAsync(conversation, record, restoringHistory: true);
            timing.Mark("applyMs");
            await RestoreCompletedTurnsAsync(conversation, timing, lifetime.Token);
            timing.Mark("eventsMs");
            loaded = conversation.Turns.All(turn => !AiChatStates.IsTerminal(turn.Record.Status) || turn.ObservedTerminal);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!conversation.Removing && !conversation.Removed && conversation.TranscriptVersion == version && IsTranscriptVisible(conversation)) state.Report(error);
        }
        finally
        {
            timing.Mark("unfinishedMs");
            var canceled = lifetime.IsCancellationRequested || conversation.TranscriptVersion != version || !IsTranscriptVisible(conversation);
            if (_historyLoads.TryGetValue(conversation.UiKey, out var pending) && pending.Version == version) _historyLoads.Remove(conversation.UiKey);
            if (conversation.TranscriptVersion == version)
            {
                conversation.HistoryLoading = false;
                conversation.HistoryFailed = !loaded && IsTranscriptVisible(conversation);
                if (loaded) conversation.TranscriptOffset = null;
            }

            state.Notify();
            timing.Mark("publishMs");
            timing.Metrics["totalMs"] = timing.ElapsedMilliseconds;
            timing.Metrics["eventReadMs"] = (long)timing.EventRead.TotalMilliseconds;
            if (loaded && !canceled)
                timing.Metrics["events"] = conversation.Turns.Sum(turn => (long)turn.Events.Count);
            var count = conversation.Turns.Count;
            var stage = conversation.SkinScopeId is null ? "chat" : "skin";
            var code = canceled ? "CANCELED" : loaded ? "LOADED" : "FAILED";
            lifetime.Dispose();
            completion.TrySetResult();
            _ = Task.Run(() => state.Activity.WriteAsync("ai.history.load", stage: stage, code: code, count: count, metrics: timing.Metrics));
        }
    }

    internal async Task<AiUiConversation> LoadConversationAsync(string id)
    {
        var conversation = new AiUiConversation();
        await ApplyAsync(conversation, await ReadConversationAsync(id));
        state.Conversations.Add(conversation);
        state.Select(conversation);
        await SelectAsync(conversation);
        return conversation;
    }

    internal Task SendAsync(AiUiConversation conversation, AiUiTurn? retry = null) => state.RunAsync(async () =>
        {
            // Commit the player's message before any Core initialization or storage waits. This snapshot also
            // retains rejected/ambiguous submissions independently of a new draft typed while sending.
            var pending = conversation.PendingTurn;
            if (pending is null)
            {
                pending = new(new()
                {
                    Id = conversation.Submission?.ClientKey ?? Guid.NewGuid().ToString("N"),
                    Prompt = conversation.Submission?.Prompt ?? retry?.Record.Prompt ?? conversation.Draft,
                    Images = retry is not null ? retry.Record.Images : [.. conversation.Images],
                    RetryOf = retry?.Id,
                    Status = "connecting"
                });
                conversation.PendingTurn = pending;
                if (retry is null && conversation.Draft == pending.Record.Prompt)
                    conversation.Draft = string.Empty;
                conversation.Images.RemoveAll(image => pending.Record.Images?.Any(sent => sent.Id == image.Id) == true);
            }

            pending.Record = pending.Record with { Status = "connecting", Error = null };
            conversation.Sending = true;
            conversation.StopRequested = false;
            if (!state.Conversations.Contains(conversation))
                state.Conversations.Insert(0, conversation);
            state.Notify();
            try
            {
                if (!conversation.Persisted)
                {
                    var request = new AiChatCreate
                    {
                        GenerateTitle = conversation.SkinScopeId is null,
                        ModelId = conversation.ModelId,
                        InstanceId = conversation.InstanceId,
                        SkinScopeId = conversation.SkinScopeId,
                        RepairId = conversation.RepairId,
                        Title = string.IsNullOrWhiteSpace(conversation.Name) ? null : conversation.Name
                    };
                    var created = await Task.Run(() => state.Service.CreateConversationAsync(request, ct: _lifetime.Token), _lifetime.Token);
                    conversation.Id = created.Id ?? throw new InvalidDataException(Resources.AiInvalidRecord);
                    conversation.Persisted = true;
                    conversation.Revision = created.Revision ?? throw new InvalidDataException(Resources.AiInvalidRecord);
                    await UpdateAsync(conversation);
                }

                if (conversation.StoredMode == "read_only" && conversation.Submission is null)
                    await UpdateAsync(conversation);
                using var process = Process.GetCurrentProcess();
                // Retain the same exact submission after an ambiguous receipt failure. An explicit retry cannot duplicate a turn.
                conversation.Submission ??= new()
                {
                    ConversationId = conversation.Id,
                    ClientKey = pending.Id,
                    Prompt = pending.Record.Prompt!,
                    ExpectedRevision = conversation.Revision,
                    ModelId = conversation.ModelId,
                    AllowOperations = conversation.Operate,
                    OwnerPid = process.Id,
                    OwnerStartedUtc = process.StartTime.ToUniversalTime().ToString("O"),
                    RetryTurnId = pending.Record.RetryOf,
                    ImageIds = pending.Record.Images?.Select(image => image.Id).ToArray(),
                };
                AiChatRecord receipt;
                try
                {
                    receipt = await Task.Run(() => state.Service.SendAsync(conversation.Submission, ct: _lifetime.Token), _lifetime.Token);
                }
                catch (Rodel.MinecraftLauncher.Core.Application.LauncherOperationException)
                {
                    conversation.Submission = null;
                    await ApplyAsync(conversation, await ReadConversationAsync(conversation.Id));
                    throw;
                }

                if (receipt.Id is null)
                    throw new InvalidDataException(Resources.AiInvalidRecord);
                var turn = conversation.Turns.FirstOrDefault(item => item.Id == receipt.Id) ?? pending;
                turn.Record = receipt;
                if (!conversation.Turns.Contains(turn))
                    conversation.Turns.Add(turn);
                conversation.PendingTurn = null;
                conversation.Submission = null;
                conversation.Sending = false;
                _ = Observe(conversation, turn);
                state.Notify();
                if (conversation.StopRequested)
                    await state.Service.CancelTurnAsync(new AiChatCancellation { ConversationId = conversation.Id, TurnId = turn.Id, CancelQueued = true }, ct: _lifetime.Token);
                await ApplyAsync(conversation, await ReadConversationAsync(conversation.Id));
            }
            catch (Exception error)
            {
                if (conversation.PendingTurn is not null)
                    pending.Record = pending.Record with { Status = "failed", Error = new System.Text.Json.Nodes.JsonObject { ["message"] = error.Message } };
                else
                    throw;
            }
            finally
            {
                conversation.Sending = false;
                conversation.StopRequested = false;
            }
        });

    internal async Task UpdateAsync(AiUiConversation conversation)
    {
        if (conversation.RepairId is not null)
            return;
        if (!conversation.Persisted)
        {
            state.Notify();
            return;
        }

        var request = new AiConversationUpdate
        {
            ConversationId = conversation.Id,
            ExpectedRevision = conversation.Revision,
            Mode = conversation.Operate ? "allow_operations" : "safe",
            FileAccessEnabled = conversation.Files,
            NetworkAccessEnabled = conversation.Network,
        };
        var result = await Task.Run(() => state.Service.UpdateConversationAsync(request, ct: _lifetime.Token), _lifetime.Token);
        conversation.Revision = result.Revision ?? throw new InvalidDataException(Resources.AiInvalidRecord);
        conversation.StoredMode = conversation.Operate ? "allow_operations" : "safe";
        state.Notify();
    }

    internal Task ChangeAccessAsync(
        AiUiConversation conversation,
        bool? operate = null,
        bool? files = null,
        bool? network = null) => state.RunAsync(async () =>
        {
            var old = (conversation.Operate, conversation.Files, conversation.Network);
            conversation.Operate = operate ?? conversation.Operate;
            conversation.Files = files ?? conversation.Files;
            conversation.Network = network ?? conversation.Network;
            try
            {
                await UpdateAsync(conversation);
            }
            catch
            {
                (conversation.Operate, conversation.Files, conversation.Network) = old;
                throw;
            }
        });

    internal Task ChangeContextAsync(
        AiUiConversation conversation,
        string? instanceId = null,
        bool instance = false,
        string? modelId = null) => state.RunAsync(async () =>
        {
            if (conversation.Submission is not null)
                return;
            if (conversation.Persisted)
            {
                await state.Service.UpdateConversationAsync(new()
                {
                    ConversationId = conversation.Id,
                    ExpectedRevision = conversation.Revision,
                    InstanceId = instance ? instanceId : null,
                    ClearInstance = instance && instanceId is null,
                    ModelId = modelId,
                });
                await ApplyAsync(conversation, await ReadConversationAsync(conversation.Id));
            }
            else
            {
                if (instance)
                    conversation.InstanceId = instanceId;
                if (modelId is not null)
                    conversation.ModelId = modelId;
                state.Notify();
            }
        });

    internal Task StopAsync(AiUiConversation conversation)
    {
        if (conversation.Sending)
        {
            // Core owns the durable turn identity. Remember Stop during preparation and cancel that exact receipt.
            conversation.StopRequested = true;
            if (conversation.PendingTurn is { } pending)
                pending.Record = pending.Record with { Status = "canceling" };
            state.Notify();
            return Task.CompletedTask;
        }

        return state.RunAsync(async () =>
        {
            var turn = conversation.Turns.FirstOrDefault(turn => !AiChatStates.IsTerminal(turn.Record.Status));
            if (turn is not null)
            {
                await state.Service.CancelTurnAsync(new AiChatCancellation
                {
                    ConversationId = conversation.Id,
                    TurnId = turn.Id,
                    CancelQueued = true
                });
            }
        });
    }

    internal async Task RenameAsync(AiUiConversation conversation, string title)
    {
        if (conversation.Persisted)
        {
            var result = await state.Service.UpdateConversationAsync(new()
            {
                ConversationId = conversation.Id,
                ExpectedRevision = conversation.Revision,
                Title = title
            });
            conversation.Revision = result.Revision ?? throw new InvalidDataException(Resources.AiInvalidRecord);
        }

        conversation.Name = title;
        state.Notify();
    }

    internal async Task RemoveAsync(AiUiConversation conversation, AiConversationRemovalReview? review)
    {
        if (conversation.Persisted)
            ArgumentNullException.ThrowIfNull(review);
        conversation.Removing = true;
        try
        {
            // Stop this UI's readers before Core can remove their files. Core still owns cancellation
            // of active/queued generation; a failed exact review resumes observation of the original chat.
            await Task.WhenAll(conversation.Turns.Select(turn => _observers.GetValueOrDefault(turn.Id)).OfType<Task>());
            if (conversation.Persisted)
            {
                await state.Service.RemoveConversationAsync(new()
                {
                    ConversationId = conversation.Id,
                    ExpectedRevision = review!.Revision.GetValue<long>(),
                    Fingerprint = review.Fingerprint,
                    Confirm = true
                });
            }

            conversation.Removed = true;
            state.Discard(conversation);
        }
        finally
        {
            conversation.Removing = false;
            if (!conversation.Removed)
            {
                foreach (var turn in conversation.Turns)
                    _ = Observe(conversation, turn);
            }
        }
    }

    internal Task RefreshAsync(AiUiConversation conversation) => state.RunAsync(async () => await ApplyAsync(conversation, await ReadConversationAsync(conversation.Id)));

    private async Task<AiChatRecord> ReadConversationAsync(string id, CancellationToken? selectedCancellation = null, HistoryLoadTiming? timing = null)
    {
        var cancellation = selectedCancellation ?? _lifetime.Token;
        var record = await Task.Run(() => state.Service.GetConversationAsync(id, ct: cancellation), cancellation);
        timing?.Mark("recordReadMs");
        if (record.Turns is { } turns)
        {
            for (var i = 0; i < turns.Length; i++)
                turns[i] = await RefreshEffectsAsync(id, turns[i], cancellation);
        }

        cancellation.ThrowIfCancellationRequested();
        timing?.Mark("effectCheckMs");
        return record;
    }

    private async Task<AiChatRecord> RefreshEffectsAsync(string conversationId, AiChatRecord turn, CancellationToken cancellation)
    {
        if (!AiChatStates.IsTerminal(turn.Status) || !AiTurnObservation.HasPendingEffects(turn))
        {
            _failedEffectChecks.Remove(turn.Id!);
            return turn;
        }

        try
        {
            var observed = await Task.Run(() => state.Service.CheckTurnEffectsAsync(conversationId, turn.Id!, ct: cancellation), cancellation);
            _failedEffectChecks.Remove(turn.Id!);
            return observed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _failedEffectChecks.Add(turn.Id!);
            return turn;
        }
    }

    private async Task ApplyAsync(AiUiConversation conversation, AiChatRecord record, bool restoringHistory = false)
    {
        var cancellation = _lifetime.Token;
        var repair = record.RepairId is { } id ? await Task.Run(() => state.Repairs.Read(id), cancellation) : null;
        cancellation.ThrowIfCancellationRequested();
        // A background observation must not undo a newer permission/title save made while this read was pending.
        if (conversation.Persisted && (record.Revision ?? 0) < conversation.Revision)
            return;
        conversation.Id = record.Id!;
        conversation.Persisted = true;
        conversation.Revision = record.Revision ?? 0;
        conversation.Name = record.Title ?? string.Empty;
        conversation.InstanceId = record.InstanceId;
        conversation.ModelId = record.ModelId;
        conversation.SkinScopeId = record.SkinScopeId;
        conversation.RepairId = record.RepairId;
        if (repair is not null)
        {
            conversation.Repair = repair;
            conversation.RepairRequest = new()
            {
                InstanceId = record.InstanceId!,
                SessionId = conversation.Repair.OriginSessionId,
                JobId = conversation.Repair.OriginJobId
            };
        }

        conversation.Operate = record.Mode == "allow_operations";
        conversation.StoredMode = record.Mode ?? "read_only";
        conversation.Files = record.FileAccessEnabled ?? state.Configuration.Defaults.Files;
        conversation.Network = record.NetworkAccessEnabled ?? state.Configuration.Defaults.Network;
        conversation.ContextChanges.Clear();
        if (record.AdditionalData?.TryGetValue("contextChanges", out var changes) == true)
        {
            foreach (var change in changes.EnumerateArray())
            {
                conversation.ContextChanges.Add(new(change.GetProperty("id").GetString()!, change.GetProperty("afterTurnId").GetString()!, change.GetProperty("kind").GetString()!, change.GetProperty("value").GetString()));
            }
        }

        foreach (var snapshot in record.Turns ?? [])
        {
            if (snapshot.Id is null)
                continue;
            var turn = conversation.Turns.FirstOrDefault(item => item.Id == snapshot.Id);
            if (turn is null)
            {
                turn = new(snapshot);
                conversation.Turns.Add(turn);
            }

            UpdateTurn(conversation, turn, snapshot);
            if (!restoringHistory || !AiChatStates.IsTerminal(turn.Record.Status))
                _ = Observe(conversation, turn);
        }

        state.RestoreReadReceipt(conversation);
        if (!restoringHistory)
            state.Notify();
        if (record.TitleSource == "pending" && state.Configuration.Providers.Any(provider => provider.Protocol != AiProtocols.RodelInference && provider.Models.Any(model => model.Id == record.ModelId)) && _titleObservers.Add(conversation.Id))
        {
            _ = ObserveTitleAsync(conversation);
        }
    }

    private async Task ObserveTitleAsync(AiUiConversation conversation)
    {
        var cancellation = _lifetime.Token;
        var timer = Stopwatch.StartNew();
        try
        {
            while (!cancellation.IsCancellationRequested && timer.Elapsed < TimeSpan.FromSeconds(20) && state.Conversations.Contains(conversation))
            {
                await Task.Delay(250, cancellation);
                var record = await Task.Run(() => state.Service.GetConversationAsync(conversation.Id, ct: cancellation), cancellation);
                cancellation.ThrowIfCancellationRequested();
                if ((record.Revision ?? 0) >= conversation.Revision)
                {
                    conversation.Revision = record.Revision ?? conversation.Revision;
                    conversation.Name = record.Title ?? string.Empty;
                    state.Notify();
                }

                if (record.TitleSource != "pending")
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }
        finally
        {
            _titleObservers.Remove(conversation.Id);
        }
    }

    private void UpdateTurn(AiUiConversation conversation, AiUiTurn turn, AiChatRecord record)
    {
        // Full-history reads and event observations may complete in either order.
        if (DateTimeOffset.TryParse(turn.Record.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var current) && DateTimeOffset.TryParse(record.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var incoming) && incoming < current)
        {
            return;
        }

        turn.Record = AiChatStates.IsTerminal(record.Status) && !IsTranscriptVisible(conversation) ? HistoryRecord(record) : record;
        turn.Approval = AiApproval.Capture(state, conversation, turn);
        turn.UserAnswer = AiUserAnswer.Capture(state, conversation, turn);
    }

    private async Task RestoreCompletedTurnsAsync(AiUiConversation conversation, HistoryLoadTiming timing, CancellationToken cancellation)
    {
        var conversationId = conversation.Id;
        foreach (var turn in conversation.Turns.Where(turn => AiChatStates.IsTerminal(turn.Record.Status)).ToArray())
        {
            cancellation.ThrowIfCancellationRequested();
            if (_observers.TryGetValue(turn.Id, out var pending))
                await pending.WaitAsync(cancellation);
            if (turn.ObservedTerminal)
                continue;

            var turnId = turn.Id;
            var started = Stopwatch.GetTimestamp();
            var history = await Task.Run(() => state.Service.GetCompletedTurnEventsAsync(conversationId, turnId, cancellation), cancellation);
            timing.EventRead += Stopwatch.GetElapsedTime(started);
            cancellation.ThrowIfCancellationRequested();
            AppendEvents(turn, history.Items, history.NextSequence);
            turn.ObservedTerminal = true;
        }
    }

    private bool IsTranscriptVisible(AiUiConversation conversation) => state.TranscriptActive && ReferenceEquals(state.Current, conversation);

    private static AiChatRecord HistoryRecord(AiChatRecord record) => new()
    {
        Id = record.Id,
        Status = record.Status,
        CreatedUtc = record.CreatedUtc,
        UpdatedUtc = record.UpdatedUtc
    };

    internal void ReleaseCompletedTranscript(AiUiConversation conversation)
    {
        conversation.TranscriptVersion++;
        if (_historyLoads.TryGetValue(conversation.UiKey, out var pending)) pending.Lifetime.Cancel();
        conversation.HistoryLoading = false;
        conversation.HistoryFailed = false;
        foreach (var turn in conversation.Turns)
        {
            if (AiChatStates.IsTerminal(turn.Record.Status) && !_observers.ContainsKey(turn.Id))
            {
                turn.Record = HistoryRecord(turn.Record);
                turn.ReleaseTranscript();
            }
        }
    }

    private Task Observe(AiUiConversation conversation, AiUiTurn turn)
    {
        if (conversation.Removing || conversation.Removed)
            return Task.CompletedTask;
        if (_observers.TryGetValue(turn.Id, out var pending))
            return pending;
        if (turn.ObservedTerminal || (AiChatStates.IsTerminal(turn.Record.Status) && !IsTranscriptVisible(conversation)))
            return Task.CompletedTask;
        var completion = new TaskCompletionSource();
        _observers.Add(turn.Id, completion.Task);
        _ = ObserveAsync(conversation, turn, completion);
        return completion.Task;
    }

    private async Task ObserveAsync(AiUiConversation conversation, AiUiTurn turn, TaskCompletionSource completion)
    {
        var cancellation = _lifetime.Token;
        var restoringHistory = AiChatStates.IsTerminal(turn.Record.Status);
        try
        {
            do
            {
                var (Turn, Events, NextSequence) = await AiTurnObservation.ReadAsync(turn.Cursor, after => Task.Run(() => state.Service.GetEventsAsync(conversation.Id, turn.Id, after, ct: cancellation), cancellation), () => Task.Run(() => state.Service.GetTurnAsync(conversation.Id, turn.Id, ct: cancellation), cancellation));
                if (conversation.Removing || conversation.Removed)
                    break;
                Turn = await RefreshEffectsAsync(conversation.Id, Turn, cancellation);
                var terminal = AiChatStates.IsTerminal(Turn.Status);
                var repair = !terminal && conversation.RepairId is { } repairId ? await Task.Run(() => state.Repairs.Read(repairId), cancellation) : null;
                cancellation.ThrowIfCancellationRequested();
                if (conversation.Removing || conversation.Removed)
                    break;
                AppendEvents(turn, Events, NextSequence);
                UpdateTurn(conversation, turn, Turn);
                if (repair is not null)
                    conversation.Repair = repair;
                state.Notify();
                if (terminal)
                {
                    turn.ObservedTerminal = true;
                    // The selected history already contains every turn snapshot. Only a newly finished live
                    // turn needs another whole-conversation read to discover its final queue/metadata changes.
                    if (!restoringHistory)
                        await ApplyAsync(conversation, await ReadConversationAsync(conversation.Id));
                    break;
                }

                await Task.Delay(200, cancellation);
            }
            while (!cancellation.IsCancellationRequested && !conversation.Removing && !conversation.Removed);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!conversation.Removing && !conversation.Removed)
                state.Report(error);
        }
        finally
        {
            _observers.Remove(turn.Id);
            if (!IsTranscriptVisible(conversation))
                ReleaseCompletedTranscript(conversation);
            completion.TrySetResult();
        }
    }

    private static void AppendEvents(AiUiTurn turn, AiChatEvent[] events, long nextSequence)
    {
        StringBuilder? text = null;
        foreach (var item in events)
        {
            if (item.Sequence <= turn.Cursor)
                continue;
            turn.Events.Add(item);
            if (item.Kind == "message_started" && (text?.Length ?? turn.Text.Length) > 0)
                (text ??= new(turn.Text)).Append("\n\n");
            if (item.Kind == "text_delta")
                (text ??= new(turn.Text)).Append(item.Data?["text"]?.ToString());
        }

        if (text is not null)
            turn.Text = text.ToString();
        turn.Cursor = nextSequence;
    }

    private sealed class HistoryLoadTiming
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _previous;

        internal long ElapsedMilliseconds => _clock.ElapsedMilliseconds;
        internal TimeSpan EventRead { get; set; }
        internal Dictionary<string, long> Metrics { get; } = [];

        internal void Mark(string stage)
        {
            var elapsed = ElapsedMilliseconds;
            Metrics[stage] = elapsed - _previous;
            _previous = elapsed;
        }
    }

    /// <summary>Stops UI observation; Core retains durable history and monitors this application's process lifetime.</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}