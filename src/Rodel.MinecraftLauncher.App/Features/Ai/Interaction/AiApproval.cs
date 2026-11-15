// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shares one immutable Core approval binding between the composer banner and its optional detail view.</summary>
internal sealed class AiApproval(
    AiUiState state,
    AiUiConversation conversation,
    AiUiTurn turn,
    string invocationId,
    string fingerprint,
    string name,
    bool operationPermission,
    JsonElement? pathAccess)
{
    private int _offset;
    private bool _resolved;
    private string _readError = string.Empty;
    private string _decisionError = string.Empty;
    internal string InvocationId => invocationId;
    internal string Fingerprint => fingerprint;
    internal string Name => name;
    internal bool OperationPermission => operationPermission;
    internal JsonElement? PathAccess => pathAccess;
    internal int PathScope { get; private set; }

    internal void SelectPathScope(int index)
    {
        PathScope = Math.Clamp(index, 0, 2);
        state.Notify();
    }

    internal bool IsActive => !_resolved && ReferenceEquals(turn.Approval, this) && turn.Record.Status == "awaiting_confirmation";
    internal bool Reading { get; private set; }
    internal bool Deciding { get; private set; }
    internal bool Working => Reading || Deciding;
    internal bool DetailsComplete { get; private set; }
    internal bool CanApprove => IsActive && !Deciding;
    internal string Text { get; private set; } = string.Empty;
    internal string Error => _decisionError.Length > 0 ? _decisionError : _readError;

    internal static AiApproval? Capture(AiUiState state, AiUiConversation conversation, AiUiTurn turn)
    {
        if (turn.Record.Status != "awaiting_confirmation")
            return null;
        if (turn.Record.AdditionalData?.TryGetValue("pendingConfirmation", out var pending) != true)
            throw new InvalidDataException(Resources.AiInvalidRecord);
        var id = Required(pending, "id");
        var fingerprint = Required(pending, "fingerprint");
        if (turn.Approval is { } current && current.InvocationId == id && current.Fingerprint == fingerprint)
            return current;
        return new(
            state,
            conversation,
            turn,
            id,
            fingerprint,
            Required(pending, "name"),
            pending.TryGetProperty("operationPermission", out var permission) && permission.GetBoolean(),
            pending.TryGetProperty("pathAccess", out var access) ? access.Clone() : null);
    }

    internal async Task ReadAsync()
    {
        if (Working || DetailsComplete)
            return;
        Reading = true;
        _readError = string.Empty;
        state.Notify();
        try
        {
            var request = new AiToolConfirmationRead
            {
                ConversationId = conversation.Id,
                TurnId = turn.Id,
                InvocationId = invocationId,
                Offset = _offset
            };
            var page = await Task.Run(() => state.Service.ReadToolConfirmationAsync(request));
            if (page.Id != invocationId || page.Fingerprint.ToString() != fingerprint)
                throw new InvalidDataException(Resources.AiReviewChanged);
            Text += page.Text;
            _offset = page.NextOffset ?? page.TotalCharacters;
            DetailsComplete = !page.HasMore;
            _resolved |= page.State.ToString() != "pending";
        }
        catch (Exception error)
        {
            _readError = error.Message;
        }
        finally
        {
            Reading = false;
            state.Notify();
        }
    }

    internal async Task DecideAsync(bool approve, string scope = "once")
    {
        if (Deciding)
            return;
        if (!IsActive || (approve && !CanApprove))
        {
            _decisionError = Resources.AiApprovalInactive;
            state.Notify();
            return;
        }

        Deciding = true;
        _decisionError = string.Empty;
        state.Notify();
        try
        {
            var result = await state.Service.ConfirmToolAsync(new()
            {
                ConversationId = conversation.Id,
                TurnId = turn.Id,
                InvocationId = invocationId,
                Fingerprint = fingerprint,
                Approve = approve,
                Scope = approve && PathAccess is not null
                        ? PathScope switch
                        {
                            1 => "parent_folder",
                            2 => "all_paths",
                            _ => "once"
                        }
                        : approve && OperationPermission && scope == "tool" ? "tool" : "once"
            });
            if (result.ConversationRevision is { } revision)
                conversation.Revision = revision;
            _resolved = true;
        }
        catch (Exception error)
        {
            _decisionError = error.Message;
        }
        finally
        {
            Deciding = false;
            state.Notify();
        }
    }

    private static string Required(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException(Resources.AiInvalidRecord);
}