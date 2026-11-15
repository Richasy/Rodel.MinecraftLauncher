// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>The committed decision for one exact tool binding; it never dispatches the tool itself.</summary>
public sealed record AiToolConfirmationReceipt
{
    /// <summary>Gets the invocation whose decision was committed.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the fingerprint binding this decision to the reviewed operation.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Gets approved or rejected.</summary>
    public required string State { get; init; }
    /// <summary>Gets whether the decision request dispatched the operation; always false.</summary>
    public bool DispatchPerformedByThisRequest { get; init; }
    /// <summary>Gets the committed revision when the decision also grants reusable conversation permission.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ConversationRevision { get; init; }
    /// <summary>Gets unfamiliar host receipt fields without changing the approved scope.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The result of removing reviewed conversation history, not its independent background jobs.</summary>
public sealed record AiConversationRemovalReceipt
{
    /// <summary>Gets the removed conversation identifier.</summary>
    public required string ConversationId { get; init; }
    /// <summary>Gets whether the conversation was removed from visible history.</summary>
    public bool Removed { get; init; }
    /// <summary>Gets independent jobs left untouched by history removal.</summary>
    public required string[] IndependentJobIds { get; init; }
    /// <summary>Gets the recovery location if cleanup could not remove every private file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetainedRecoveryPath { get; init; }
    /// <summary>Gets heterogeneous cleanup diagnostics without discarding their original error details.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Warnings { get; init; }
    /// <summary>Gets unfamiliar host receipt fields while retaining original cleanup evidence.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The integrity receipt for an explicitly written portable conversation export.</summary>
public sealed record AiConversationExportReceipt
{
    /// <summary>Gets the absolute destination actually written.</summary>
    public required string Path { get; init; }
    /// <summary>Gets the UTF-8 file length.</summary>
    public int Bytes { get; init; }
    /// <summary>Gets the SHA-256 of the exported bytes.</summary>
    public required string Sha256 { get; init; }
    /// <summary>Gets json or text.</summary>
    public required string Format { get; init; }
    /// <summary>Gets whether private provider protocol state was exported; always false.</summary>
    public bool ProviderStateIncluded { get; init; }
    /// <summary>Gets unfamiliar host receipt fields without changing the published integrity information.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A selected answer resolved against the frozen question, rather than caller-provided labels.</summary>
/// <param name="Id">The selected option identifier.</param>
/// <param name="Label">The corresponding label from the frozen question.</param>
public sealed record AiUserAnswerChoice(string Id, string Label);

/// <summary>The exact answer durably accepted for a waiting question; it grants no operation permission.</summary>
public sealed record AiUserAnswer
{
    /// <summary>Gets the answered question invocation.</summary>
    public required string PromptId { get; init; }
    /// <summary>Gets the original frozen question text.</summary>
    public required string Question { get; init; }
    /// <summary>Gets answered or skipped.</summary>
    public required string Status { get; init; }
    /// <summary>Gets selected choices in the original question order.</summary>
    public required AiUserAnswerChoice[] Choices { get; init; }
    /// <summary>Gets the user's unmodified free-text answer.</summary>
    public required string Text { get; init; }
    /// <summary>Gets unfamiliar host answer fields without interpreting them as operation permission.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The model-visible result of a completed question, independent of operation authorization.</summary>
/// <param name="Data">The durably accepted answer including the frozen question and selected labels.</param>
internal sealed record AiUserAnswerResult(AiUserAnswer Data)
{
    /// <summary>Gets the completion of the question tool, not the answer's answered/skipped status.</summary>
    public string Status { get; } = "succeeded";
}