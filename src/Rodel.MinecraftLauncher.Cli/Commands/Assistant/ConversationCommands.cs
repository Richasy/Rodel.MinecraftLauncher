// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.Assistant.AssistantCommandFactory;
using C = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;
using O = Rodel.MinecraftLauncher.Cli.Assistant.AssistantOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli.Assistant;

internal static class ConversationCommands
{
    /// <summary>Registers typed conversation commands and observation-only waits over the shared Core API.</summary>
    public static void Register(Command root)
    {
        var chat = Group(root, "chat", S.Chat);
        Add(chat, "create", S.Create, async (app, p, ct) => await app.Client.CreateConversationAsync(new AiChatCreate
        {
            Title = p.GetValue(O.Title),
            ModelId = AssistantInput.Model(p),
            InstanceId = p.GetValue(O.Instance),
            GenerateTitle = p.GetValue(O.GenerateTitle),
            SkinScopeId = p.GetValue(LauncherCommands.Hosting.SkinScope),
            RepairId = p.GetValue(O.Repair)
        }, app.Host, ct).ConfigureAwait(false), options: [O.Title, O.Models, O.Instance, O.GenerateTitle, O.Repair, LauncherCommands.Hosting.SkinScope]);
        Add(chat, "list", S.List, async (app, p, ct) => await app.Client.ListConversationsAsync(p.GetValue(O.Offset), p.GetValue(O.Limit) ?? 20, app.Host, ct).ConfigureAwait(false), options: [O.Offset, O.Limit]);
        Add(chat, "show", S.Show, async (app, p, ct) => await app.Client.GetConversationAsync(p.GetRequiredValue(O.Conversation), app.Host, ct).ConfigureAwait(false), [O.Conversation]);
        var turns = Group(chat, "turn", S.Turns);
        var confirmations = Group(chat, "confirmation", S.Confirmations);
        var effects = Group(chat, "effects", S.Effects);
        var archives = Group(chat, "archive", S.Archive);
        Add(turns, "show", S.Turn, async (app, p, ct) => await app.Client.GetTurnAsync(p.GetRequiredValue(O.Conversation), p.GetRequiredValue(O.Turn), app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn]);
        Add(turns, "wait", S.WaitForTurn, async (app, p, ct) => await WaitAsync(app, p,
            p.GetRequiredValue(O.Conversation), p.GetRequiredValue(O.Turn), ct).ConfigureAwait(false),
            [O.Conversation, O.Turn], [O.After, O.Timeout], execution: true);
        Add(turns, "events", S.Events, async (app, p, ct) => await app.Client.GetEventsAsync(p.GetRequiredValue(O.Conversation), p.GetRequiredValue(O.Turn), p.GetValue(O.After), app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn], [O.After]);
        Add(archives, "read", S.Archive, async (app, p, ct) => await app.Client.ReadContextArchiveAsync(new AiContextRead
        {
            ConversationId = p.GetRequiredValue(O.Conversation),
            ArchiveId = p.GetRequiredValue(O.Archive),
            Offset = p.GetValue(O.Offset),
            Characters = p.GetValue(O.Characters) ?? 12000,
            MessageIndex = p.GetValue(O.MessageIndex)
        }, app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Archive], [O.Offset, O.Characters, O.MessageIndex]);
        var cancelQueued = new Option<bool>("--cancel-queued") { Description = C.AiCancelQueued };
        Add(turns, "cancel", S.Cancel, async (app, p, ct) => await app.Client.CancelTurnAsync(new AiChatCancellation
        {
            ConversationId = p.GetRequiredValue(O.Conversation),
            TurnId = p.GetRequiredValue(O.Turn),
            CancelQueued = p.GetValue(cancelQueued)
        }, app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn], [cancelQueued]);
        Add(turns, "run-now", C.AiRunQueuedTurn, async (app, p, ct) => await app.Client.RunQueuedTurnAsync(
            p.GetRequiredValue(O.Conversation), p.GetRequiredValue(O.Turn), app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn]);
        Add(effects, "check", S.EffectsCheck, async (app, p, ct) => await app.Client.CheckTurnEffectsAsync(
            p.GetRequiredValue(O.Conversation), p.GetRequiredValue(O.Turn), app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn]);
        Add(effects, "acknowledge", S.EffectsAcknowledge, async (app, p, ct) => await app.Client.AcknowledgeUnknownEffectAsync(new AiEffectAcknowledgement
        {
            ConversationId = p.GetRequiredValue(O.Conversation),
            TurnId = p.GetRequiredValue(O.Turn),
            InvocationId = p.GetRequiredValue(O.Invocation),
            Fingerprint = AssistantInput.Required(p, O.Fingerprint),
            AcceptUnknown = p.GetValue(O.Approve) ?? throw new ArgumentException(S.ApprovalRequired)
        }, app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn, O.Invocation], [O.Fingerprint, O.Approve]);
        Add(confirmations, "decide", S.ToolConfirm, async (app, p, ct) => await app.Client.ConfirmToolAsync(new AiToolConfirmation
        {
            ConversationId = p.GetRequiredValue(O.Conversation),
            TurnId = p.GetRequiredValue(O.Turn),
            InvocationId = p.GetRequiredValue(O.Invocation),
            Fingerprint = AssistantInput.Required(p, O.Fingerprint),
            Scope = p.GetValue(O.ApprovalScope) ?? "once",
            Approve = p.GetValue(O.Approve) ?? throw new ArgumentException(S.ApprovalRequired)
        }, app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn, O.Invocation], [O.Fingerprint, O.Approve, O.ApprovalScope]);
        Add(confirmations, "show", S.ToolConfirmationRead, async (app, p, ct) => await app.Client.ReadToolConfirmationAsync(new AiToolConfirmationRead
        {
            ConversationId = p.GetRequiredValue(O.Conversation),
            TurnId = p.GetRequiredValue(O.Turn),
            InvocationId = p.GetRequiredValue(O.Invocation),
            Offset = p.GetValue(O.Offset),
            Characters = p.GetValue(O.Characters) ?? 12000
        }, app.Host, ct).ConfigureAwait(false), [O.Conversation, O.Turn, O.Invocation], [O.Offset, O.Characters]);
        Add(chat, "respond", C.AiUserPromptRespond, async (app, p, ct) => await app.Client.RespondToUserPromptAsync(
            await AssistantInput.JsonAsync(AssistantInput.Required(p, O.File), AiUserPromptJsonContext.Default.AiUserResponse, ct).ConfigureAwait(false), app.Host, ct).ConfigureAwait(false), options: [O.File]);
        Add(chat, "update", S.UpdateConversation, async (app, p, ct) =>
        {
            if (p.GetValue(O.Title) is null && p.GetValue(O.Mode) is null && p.GetValue(O.Files) is null && p.GetValue(O.Network) is null
                && p.GetValue(O.Instance) is null && !p.GetValue(O.ClearInstance) && AssistantInput.Model(p) is null)
            {
                throw new ArgumentException(S.ConversationChangesRequired);
            }

            return await app.Client.UpdateConversationAsync(new AiConversationUpdate
            {
                ConversationId = p.GetRequiredValue(O.Conversation),
                ExpectedRevision = AssistantInput.Revision(p),
                Title = p.GetValue(O.Title),
                InstanceId = p.GetValue(O.Instance),
                ClearInstance = p.GetValue(O.ClearInstance),
                ModelId = AssistantInput.Model(p),
                Mode = p.GetValue(O.Mode),
                FileAccessEnabled = p.GetValue(O.Files),
                NetworkAccessEnabled = p.GetValue(O.Network)
            }, app.Host, ct).ConfigureAwait(false);
        }, [O.Conversation], [O.Title, O.Instance, O.ClearInstance, O.Models, O.Mode, O.Files, O.Network, O.Revision]);
        Add(chat, "export", S.Export, async (app, p, ct) => await app.Client.ExportConversationAsync(new AiConversationExport
        {
            ConversationId = p.GetRequiredValue(O.Conversation),
            Destination = AssistantInput.Required(p, O.Output),
            Format = p.GetValue(O.Format) ?? "json",
            Overwrite = p.GetValue(O.Overwrite)
        }, app.Host, ct).ConfigureAwait(false), [O.Conversation], [O.Output, O.Format, O.Overwrite]);
        Add(chat, "remove", S.Remove, RemoveAsync, [O.Conversation], [O.Confirm, O.Revision, O.Fingerprint]);
        Option[] sending = [O.Models, O.ClientKey, O.Revision, O.ModelCalls, O.OutputTokens, O.Timeout, O.CancelOnExit, O.NoTools, O.Wait, O.AllowOperations];
        Add(chat, "send", S.Send, async (app, p, ct) => await SendAsync(app, p,
            await AssistantInput.PromptAsync(p, app.AllowsSecretInput, null, ct).ConfigureAwait(false), null, ct).ConfigureAwait(false),
            [O.Conversation], [.. sending, O.Prompt, O.PromptFile, O.PromptStdin, O.Images], execution: true);
        Add(turns, "retry", S.Retry, async (app, p, ct) =>
        {
            var prior = await app.Client.GetTurnAsync(p.GetRequiredValue(O.Conversation), p.GetRequiredValue(O.Turn), app.Host, ct).ConfigureAwait(false);
            return await SendAsync(app, p, prior.Prompt ?? throw new InvalidDataException("The saved turn has no prompt."), p.GetRequiredValue(O.Turn), ct,
                prior.Images?.Select(image => image.Id).ToArray()).ConfigureAwait(false);
        }, [O.Conversation, O.Turn], sending, execution: true);
    }

    private static async Task<object> RemoveAsync(AssistantInvocation app, ParseResult parsed, CancellationToken ct)
    {
        var id = parsed.GetRequiredValue(O.Conversation);
        if (!parsed.GetValue(O.Confirm)) return await app.Client.ReviewConversationRemovalAsync(id, app.Host, ct).ConfigureAwait(false);
        return await app.Client.RemoveConversationAsync(new AiConversationRemoval
        {
            ConversationId = id,
            ExpectedRevision = AssistantInput.Revision(parsed),
            Fingerprint = AssistantInput.Required(parsed, O.Fingerprint),
            Confirm = true
        }, app.Host, ct).ConfigureAwait(false);
    }
    private static async Task<AiChatRecord> SendAsync(AssistantInvocation app, ParseResult parsed, string prompt, string? retry, CancellationToken ct, string[]? previousImages = null)
    {
        var sources = parsed.GetValue(O.Images) ?? [];
        if (sources.Length > AiImageStore.MaximumImages) throw new ArgumentException(C.AiReferenceImagesTooMany);
        var clientKey = parsed.GetValue(O.ClientKey) ?? Guid.NewGuid().ToString("N");
        var images = previousImages is null
            ? await app.Client.Images.PrepareSubmissionAsync(sources, parsed.GetRequiredValue(O.Conversation), clientKey, ct).ConfigureAwait(false) : [];
        using var owner = parsed.GetValue(O.CancelOnExit) ? Process.GetCurrentProcess() : null;
        var request = new AiChatSend
        {
            ConversationId = parsed.GetRequiredValue(O.Conversation),
            Prompt = prompt,
            ExpectedRevision = AssistantInput.Revision(parsed),
            ClientKey = clientKey,
            ModelId = AssistantInput.Model(parsed),
            UseTools = !parsed.GetValue(O.NoTools),
            AllowOperations = parsed.GetValue(O.AllowOperations),
            MaximumModelCalls = parsed.GetValue(O.ModelCalls) ?? 64,
            MaximumOutputTokens = parsed.GetValue(O.OutputTokens),
            TimeoutSeconds = parsed.GetValue(O.Timeout) ?? 600,
            OwnerPid = owner?.Id,
            OwnerStartedUtc = owner?.StartTime.ToUniversalTime().ToString("O"),
            RetryTurnId = retry,
            ImageIds = previousImages ?? [.. images.Select(image => image.Id)],
        };
        var receipt = await app.Client.SendAsync(request, app.Host, ct).ConfigureAwait(false);
        if (!parsed.GetValue(O.Wait)) return receipt;
        var id = receipt.Id ?? throw new InvalidDataException("The submitted turn has no identifier.");
        try
        {
            return await WaitAsync(app, parsed, request.ConversationId, id, ct, observeOnly: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (owner is not null)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.Client.CancelTurnAsync(request.ConversationId, id, app.Host, cleanup.Token).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<AiChatRecord> WaitAsync(AssistantInvocation app, ParseResult parsed,
        string conversation, string id, CancellationToken ct, bool observeOnly = true)
    {
        var seconds = parsed.GetValue(O.Timeout) ?? 600;
        var cursor = observeOnly ? parsed.GetValue(O.After) : 0;
        if (seconds is < 1 or > 3600 || cursor < 0) throw new ArgumentException(S.WaitOptionsInvalid);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (observeOnly) deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var token = deadline.Token;
        async Task DrainAsync()
        {
            do
            {
                var page = await app.Client.GetEventsAsync(conversation, id, cursor, app.Host, token).ConfigureAwait(false);
                foreach (var item in page.Items)
                {
                    cursor = item.Sequence ?? throw new InvalidDataException("The saved event has no sequence.");
                    if (parsed.GetValue(O.Events)) Console.Error.WriteLine(JsonSerializer.SerializeToNode(item, AiJsonContext.Default.AiChatEvent)!.ToJsonString());
                }

                if (!page.HasMore) break;
            } while (true);
        }

        try
        {
            while (true)
            {
                await DrainAsync().ConfigureAwait(false);
                var turn = await app.Client.GetTurnAsync(conversation, id, app.Host, token).ConfigureAwait(false);
                if (AiChatStates.IsTerminal(turn.Status))
                {
                    await DrainAsync().ConfigureAwait(false);
                    return turn with { EventCursor = cursor };
                }

                await Task.Delay(200, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LauncherOperationException("AI_WAIT_TIMEOUT", S.WaitTimeout, new JsonObject
            {
                ["conversationId"] = conversation,
                ["turnId"] = id,
                ["eventCursor"] = cursor
            });
        }
    }
}