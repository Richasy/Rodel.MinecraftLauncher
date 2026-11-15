// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Requests one game-originated AI action, scoped to a fresh world observation and a stable save slot.</summary>
public sealed record GameAiRequest
{
    /// <summary>Gets open, send, quick, read, new, cancel, confirm or answer.</summary>
    public required string Action { get; init; }
    /// <summary>Gets the stable local save directory or remote endpoint identity; it is hashed, never opened as a path.</summary>
    public required string WorldKey { get; init; }
    /// <summary>Gets the player-visible world name, used only as the conversation title.</summary>
    public string? WorldName { get; init; }
    /// <summary>Gets the live observation attached by the authenticated Java agent.</summary>
    public required GameSnapshot Snapshot { get; init; }
    /// <summary>Gets a unique submission/reset identity; repeating an uncertain submission keeps the same identity.</summary>
    public string? RequestId { get; init; }
    /// <summary>Gets the console submission to observe within the save's shared conversation, or null for the panel's chronological page.</summary>
    public string? QuickId { get; init; }
    /// <summary>Gets the user's text without a generated instruction prefix.</summary>
    public string? Message { get; init; }
    /// <summary>Gets the exact turn for event observation, cancellation or a human decision.</summary>
    public string? TurnId { get; init; }
    /// <summary>Gets the durable event cursor already consumed by the game.</summary>
    public long After { get; init; }
    /// <summary>Gets how many older turns to skip, counting backward from the latest turn.</summary>
    public int Offset { get; init; }
    /// <summary>Gets the exact pending question or operation invocation.</summary>
    public string? InvocationId { get; init; }
    /// <summary>Gets the original immutable confirmation fingerprint.</summary>
    public string? Fingerprint { get; init; }
    /// <summary>Gets the user's explicit approval or refusal; omitted values cannot approve.</summary>
    public bool? Approve { get; init; }
    /// <summary>Gets explicitly selected question option identifiers.</summary>
    public string[] OptionIds { get; init; } = [];
    /// <summary>Gets whether the user explicitly skips the pending question.</summary>
    public bool Skip { get; init; }
}

/// <summary>Returns one save's current conversation page and a bounded stream-event page.</summary>
public sealed record GameAiState
{
    /// <summary>Gets the conversation identifier, or null before the first message.</summary>
    public string? ConversationId { get; init; }
    /// <summary>Gets the console submission identifier being observed in the shared conversation.</summary>
    public string? QuickId { get; init; }
    /// <summary>Gets the launcher's currently selected default model display name.</summary>
    public string? ModelName { get; init; }
    /// <summary>Gets whether a configured default model is available for a new message.</summary>
    public bool ModelAvailable { get; init; }
    /// <summary>Gets the game ingress's per-turn operation mode; launcher conversation defaults are unchanged.</summary>
    public required string Mode { get; init; }
    /// <summary>Gets up to twenty chronological turns, excluding provider protocol and tool-result internals.</summary>
    public GameAiTurn[] Turns { get; init; } = [];
    /// <summary>Gets whether older turns remain in this save's current conversation.</summary>
    public bool HasOlder { get; init; }
    /// <summary>Gets the selected turn whose events are included.</summary>
    public string? EventTurnId { get; init; }
    /// <summary>Gets committed events after the supplied cursor; quick-question clients display only the terminal answer.</summary>
    public AiEventPage? Events { get; init; }
}

/// <summary>Projects one Core turn for the game without duplicating the model message or tool-call model.</summary>
public sealed record GameAiTurn
{
    /// <summary>Gets the committed submission identity, allowing a lost response to be observed without resending.</summary>
    public string? SubmissionId { get; init; }
    /// <summary>Gets the actual Core turn identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the unmodified submitted user text.</summary>
    public required string Prompt { get; init; }
    /// <summary>Gets the actual execution state.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the final answer, leaving active partial text to the event stream.</summary>
    public string? Answer { get; init; }
    /// <summary>Gets diagnostic data when execution did not complete normally.</summary>
    public JsonElement? Error { get; init; }
    /// <summary>Gets an exact pending operation, if explicit approval is required.</summary>
    public GameAiConfirmation? Confirmation { get; init; }
    /// <summary>Gets the original Core question and choices awaiting the player's answer.</summary>
    public AiUserPrompt? Question { get; init; }
}

/// <summary>Identifies the exact existing Core approval and its readable review.</summary>
public sealed record GameAiConfirmation
{
    /// <summary>Gets the pending tool invocation identity.</summary>
    public required string InvocationId { get; init; }
    /// <summary>Gets the actual tool name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the immutable binding fingerprint.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Gets a bounded page of the original review document; a clipped page never changes the authorized operation.</summary>
    public AiConfirmationPage? Details { get; init; }
}

internal sealed record GameAiSlot
{
    /// <summary>Gets transport submission receipts for text messages consumed as question replies in this conversation.</summary>
    public GameAiReplySubmission[] Replies { get; init; } = [];
    /// <summary>Gets old history awaiting completion of an explicitly requested, crash-recoverable reset.</summary>
    public string? ClearingConversationId { get; init; }
    /// <summary>Gets the conversation currently occupying this slot.</summary>
    public string? ConversationId { get; init; }
    /// <summary>Gets the most recent explicit reset identity for idempotent reconnection.</summary>
    public string? ResetId { get; init; }
}

/// <summary>Binds an uncertain game-message retry to its original question without creating another model turn.</summary>
/// <param name="RequestId">The game's stable message submission identity.</param>
/// <param name="TurnId">The existing turn waiting for the answer.</param>
/// <param name="PromptId">The exact frozen question invocation.</param>
/// <param name="MessageSha256">The submitted text fingerprint used to reject identity reuse with different text.</param>
internal sealed record GameAiReplySubmission(string RequestId, string TurnId, string PromptId, string MessageSha256);

/// <summary>Provides NativeAOT metadata for the game AI protocol and durable slot references.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GameAiRequest))]
[JsonSerializable(typeof(GameAiState))]
public partial class GameAiJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GameAiSlot))]
internal partial class GameAiSlotJsonContext : JsonSerializerContext;