// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

public sealed record AiToolConfirmation
{
    public required string ConversationId { get; init; }
    public required string TurnId { get; init; }
    public required string InvocationId { get; init; }
    public required string Fingerprint { get; init; }
    public required bool Approve { get; init; }
    /// <summary>Gets the host approval scope: once, tool or conversation for operations, or parent_folder/all_paths for this conversation’s path access.</summary>
    public string Scope { get; init; } = "once";
}

public sealed record AiToolConfirmationRead
{
    public required string ConversationId { get; init; }
    public required string TurnId { get; init; }
    public required string InvocationId { get; init; }
    public int Offset { get; init; }
    public int Characters { get; init; } = 12000;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiToolConfirmation))]
[JsonSerializable(typeof(AiToolConfirmationRead))]
[JsonSerializable(typeof(AiExecuteReview))]
[JsonSerializable(typeof(AiProcessStopReview))]
[JsonSerializable(typeof(AiOperationPermissionReview))]
[JsonSerializable(typeof(AiPathAccessReview))]
[JsonSerializable(typeof(AiPathAccessRequest))]
[JsonSerializable(typeof(AiWebAccessReview))]
internal sealed partial class AiApprovalJsonContext : JsonSerializerContext;

internal sealed class AiToolApprovals(LauncherPaths paths)
{
    private readonly AiChatStore _store = new(paths);
    private string FileFor(string conversation, string turn, string invocation) => Path.Combine(paths.Root, "ai", "conversations",
        AiChatStore.CheckId(conversation), "confirmations", AiChatStore.CheckId(turn), AiChatStore.CheckId(invocation) + ".json");
    private static string Hash(JsonNode node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));

    /// <summary>Persists the exact typed review binding and waits for an authorized decision without dispatching the tool.</summary>
    public async Task WaitAsync(JsonObject turn, string invocation, string name, JsonObject arguments, AiToolReview review,
        Func<CancellationToken, Task<AiToolReview>> recheck, Func<CancellationToken, Task> authorize,
        Func<string, string?, JsonObject, Task> emit, CancellationToken ct, bool operationPermission = false, AiPathAccessRequest? pathAccess = null)
    {
        var reviewedDocument = AiToolReviews.Write(review);
        var cid = turn["conversationId"]!.ToString();
        var tid = turn["id"]!.ToString();
        var file = FileFor(cid, tid, invocation);
        var binding = new JsonObject
        {
            ["conversationId"] = cid,
            ["turnId"] = tid,
            ["invocationId"] = invocation,
            ["name"] = name,
            ["arguments"] = arguments.DeepClone(),
            ["review"] = reviewedDocument.DeepClone(),
            ["operationRevision"] = turn["operationRevision"]!.DeepClone(),
            ["modelId"] = turn["modelId"]!.DeepClone()
        };
        if (pathAccess is not null) binding["pathAccess"] = System.Text.Json.JsonSerializer.SerializeToNode(pathAccess, AiApprovalJsonContext.Default.AiPathAccessRequest);
        var fingerprint = Hash(binding);
        if (operationPermission)
        {
            binding["operationPermission"] = true;
            fingerprint = Hash(binding);
        }

        var approval = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fingerprint"] = fingerprint,
            ["state"] = "pending",
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["binding"] = binding
        };
        await JsonFiles.WriteAsync(file, approval, ct).ConfigureAwait(false);
        var pending = new JsonObject
        {
            ["id"] = invocation,
            ["name"] = name,
            ["fingerprint"] = fingerprint,
            ["instanceId"] = arguments["instanceId"]?.DeepClone(),
            ["state"] = "pending",
            ["reviewId"] = arguments["reviewId"]?.DeepClone(),
            ["detailsAvailable"] = true
        };
        if (operationPermission) pending["operationPermission"] = true;
        if (pathAccess is not null) pending["pathAccess"] = System.Text.Json.JsonSerializer.SerializeToNode(pathAccess, AiApprovalJsonContext.Default.AiPathAccessRequest);
        var previousStatus = turn["status"]!.DeepClone();
        turn["pendingConfirmation"] = pending;
        turn["status"] = "awaiting_confirmation";
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        await emit("confirmation_required", invocation, pending).ConfigureAwait(false);
        try
        {
            while (true)
            {
                await authorize(ct).ConfigureAwait(false);
                using (await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false))
                    approval = JsonFiles.ReadObject(file);
                if (approval["fingerprint"]?.ToString() != fingerprint || !JsonNode.DeepEquals(approval["binding"], binding))
                    throw new LauncherOperationException("AI_CONFIRMATION_CHANGED", "The confirmation binding changed. No call was dispatched.");
                if (approval["state"]?.ToString() == "rejected")
                    throw new LauncherOperationException("AI_CONFIRMATION_DECLINED", "The user declined the exact proposed operation. It was not dispatched.");
                if (approval["state"]?.ToString() == "approved")
                {
                    var scope = approval["scope"]?.ToString();
                    var conversation = _store.GetDocument(cid);
                    if (operationPermission && scope is "conversation" or "tool"
                        && (scope == "tool" ? conversation["operationToolGrants"]?[name]?.GetValue<long>() : conversation["operationGrantRevision"]?.GetValue<long>())
                            != turn["operationRevision"]?.GetValue<long>())
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                        continue;
                    }

                    var current = await recheck(ct).ConfigureAwait(false);
                    if (Hash(AiToolReviews.Write(current)) != Hash(reviewedDocument))
                        throw new LauncherOperationException("AI_CONFIRMATION_REVIEW_CHANGED", "The operation's review or target changed. Prepare a fresh review and confirmation; no stale operation was dispatched.");
                    await authorize(ct).ConfigureAwait(false);
                    await emit("confirmation_finished", invocation, new JsonObject
                    {
                        ["status"] = "approved",
                        ["fingerprint"] = fingerprint,
                        ["name"] = name
                    }).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            await emit("confirmation_finished", invocation, new JsonObject
            {
                ["status"] = ct.IsCancellationRequested ? "canceled" : "not_executed",
                ["name"] = name,
                ["fingerprint"] = fingerprint,
                ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
            }).ConfigureAwait(false);
            throw;
        }
        finally
        {
            turn.Remove("pendingConfirmation");
            turn["status"] = previousStatus;
            await _store.SaveTurnAsync(turn, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Commits an exact binding decision without dispatching the underlying operation.</summary>
    public async Task<AiToolConfirmationReceipt> DecideAsync(AiToolConfirmation request, CancellationToken ct)
    {
        if (request.Scope is not ("once" or "tool" or "conversation" or "parent_folder" or "all_paths")) throw new ArgumentException("Use once, tool, conversation, parent_folder or all_paths approval scope.");
        var turn = _store.GetTurnDocument(request.ConversationId, request.TurnId);
        var file = FileFor(request.ConversationId, request.TurnId, request.InvocationId);
        using var lease = await ResourceLease.AcquireAsync(file, ct).ConfigureAwait(false);
        var approval = JsonFiles.ReadObject(file);
        if (approval["schemaVersion"]?.GetValue<int>() != 1 || approval["fingerprint"]?.ToString() != request.Fingerprint)
            throw new LauncherOperationException("AI_CONFIRMATION_CHANGED", "Confirm the exact current fingerprint, not an earlier or edited operation.");
        var binding = approval["binding"]!.AsObject();
        if (binding["conversationId"]?.ToString() != request.ConversationId || binding["turnId"]?.ToString() != request.TurnId
            || binding["invocationId"]?.ToString() != request.InvocationId || Hash(binding) != request.Fingerprint)
        {
            throw new LauncherOperationException("AI_CONFIRMATION_CHANGED", "The stored confirmation does not match its conversation, turn, operation and review.");
        }

        if (request.Scope is "parent_folder" or "all_paths" && (!request.Approve || binding["pathAccess"] is not JsonObject))
            throw new ArgumentException("Path scopes are available only for an explicit host path-access request.");
        var desired = request.Approve ? "approved" : "rejected";
        if (approval["state"]?.ToString() == desired)
        {
            if ((approval["scope"]?.ToString() ?? "once") != request.Scope)
                throw new LauncherOperationException("AI_CONFIRMATION_CHANGED", "An existing decision cannot be expanded to another scope.");
            var existing = Receipt(request.InvocationId, request.Fingerprint, desired);
            if (request.Scope is "conversation" or "tool") existing = existing with { ConversationRevision = await GrantOperationAsync(binding, request.Scope, ct).ConfigureAwait(false) };
            if (request.Scope is "parent_folder" or "all_paths") await new AiPathAccess(paths).GrantAsync(binding, request.Scope, ct).ConfigureAwait(false);
            return existing;
        }

        if (approval["state"]?.ToString() != "pending"
            || turn["status"]?.ToString() != "awaiting_confirmation"
            || turn["pendingConfirmation"]?["id"]?.ToString() != request.InvocationId)
        {
            throw new LauncherOperationException("AI_CONFIRMATION_INACTIVE", "This confirmation no longer belongs to an active waiting turn.");
        }

        JsonObject? conversation = null;
        if (request.Scope is "conversation" or "tool")
        {
            if (!request.Approve || binding["operationPermission"]?.GetValue<bool>() != true)
                throw new ArgumentException("Reusable approval is available only for a safe-mode operation permission request.");
            conversation = _store.GetDocument(request.ConversationId);
            if (conversation["mode"]?.ToString() != "safe"
                || (conversation["operationRevision"]?.GetValue<long>() ?? 0) != binding["operationRevision"]?.GetValue<long>())
            {
                throw new LauncherOperationException("AI_OPERATION_AUTHORIZATION_REVOKED", "The permission request no longer matches this conversation.");
            }
        }

        if (request.Scope is "parent_folder" or "all_paths")
            await new AiPathAccess(paths).GrantAsync(binding, request.Scope, ct).ConfigureAwait(false);
        approval["state"] = desired;
        approval["scope"] = request.Scope;
        approval["decidedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        await JsonFiles.WriteAsync(file, approval, ct).ConfigureAwait(false);
        if (conversation is not null)
        {
            await GrantOperationAsync(binding, request.Scope, ct).ConfigureAwait(false);
        }

        var receipt = Receipt(request.InvocationId, request.Fingerprint, desired);
        if (conversation is not null) receipt = receipt with { ConversationRevision = _store.GetDocument(request.ConversationId)["revision"]!.GetValue<long>() };
        return receipt;
    }

    private async Task<long> GrantOperationAsync(JsonObject binding, string scope, CancellationToken ct)
    {
        var conversation = _store.GetDocument(binding["conversationId"]!.ToString());
        var revision = binding["operationRevision"]!.GetValue<long>();
        if (binding["operationPermission"]?.GetValue<bool>() != true || conversation["mode"]?.ToString() != "safe"
            || (conversation["operationRevision"]?.GetValue<long>() ?? 0) != revision)
        {
            throw new LauncherOperationException("AI_OPERATION_AUTHORIZATION_REVOKED", "The approved conversation grant is no longer current.");
        }

        var name = binding["name"]?.GetValue<string>() ?? throw new InvalidDataException("The approved operation has no tool name.");
        var grants = conversation["operationToolGrants"] as JsonObject;
        if ((scope == "tool" ? grants?[name]?.GetValue<long>() : conversation["operationGrantRevision"]?.GetValue<long>()) != revision)
        {
            if (scope == "tool")
            {
                grants ??= [];
                if (conversation["operationToolGrants"] is null) conversation["operationToolGrants"] = grants;
                grants[name] = revision;
            }
            else
            {
                conversation["operationGrantRevision"] = revision;
            }

            conversation["revision"] = conversation["revision"]!.GetValue<long>() + 1;
            await _store.SaveConversationAsync(conversation, ct).ConfigureAwait(false);
        }

        return conversation["revision"]!.GetValue<long>();
    }

    /// <summary>Reads a bounded page of the exact persisted confirmation without deciding or dispatching it.</summary>
    public AiConfirmationPage Read(AiToolConfirmationRead request)
    {
        if (request.Offset < 0 || request.Characters is < 256 or > 16000)
            throw new ArgumentException("Use a nonnegative offset and 256 through 16000 characters.");
        _ = _store.GetTurn(request.ConversationId, request.TurnId);
        var record = JsonFiles.ReadObject(FileFor(request.ConversationId, request.TurnId, request.InvocationId));
        if (record["schemaVersion"]?.GetValue<int>() != 1 || record["binding"] is not JsonObject binding
            || Hash(binding) != record["fingerprint"]?.ToString()
            || binding["conversationId"]?.ToString() != request.ConversationId
            || binding["turnId"]?.ToString() != request.TurnId
            || binding["invocationId"]?.ToString() != request.InvocationId)
        {
            throw new InvalidDataException("The stored confirmation binding is invalid.");
        }

        var text = record.ToJsonString();
        if (request.Offset > text.Length) throw new ArgumentException("The offset exceeds the confirmation text.");
        var end = request.Offset + Math.Min(request.Characters, text.Length - request.Offset);
        return new()
        {
            Id = request.InvocationId,
            Fingerprint = record["fingerprint"]!.DeepClone(),
            State = record["state"]!.DeepClone(),
            Text = text[request.Offset..end],
            Offset = request.Offset,
            NextOffset = end < text.Length ? end : null,
            HasMore = end < text.Length,
            TotalCharacters = text.Length
        };
    }

    private static AiToolConfirmationReceipt Receipt(string id, string fingerprint, string state)
        => new() { Id = id, Fingerprint = fingerprint, State = state, DispatchPerformedByThisRequest = false };
}