// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

public sealed record AiContextRead
{
    public required string ConversationId { get; init; }
    public required string ArchiveId { get; init; }
    public int Offset { get; init; }
    public int Characters { get; init; } = 12000;
    public int? MessageIndex { get; init; }
}

internal sealed record AiContextArchive(int SchemaVersion, string Id, string ConversationId, string TurnId,
    string CreatedUtc, string ContentHash, JsonArray Messages, string[] PreviousArchives);

/// <summary>A Unicode-safe character page of a validated archive, without provider-private protocol state.</summary>
/// <param name="SchemaVersion">The page contract version.</param>
/// <param name="ArchiveId">The validated archive identifier.</param>
/// <param name="ContentHash">The original archive content hash.</param>
/// <param name="Text">The selected portable text page.</param>
/// <param name="Offset">The requested character offset.</param>
/// <param name="NextOffset">The continuation offset, or null at the end.</param>
/// <param name="HasMore">Whether more text remains.</param>
/// <param name="TotalCharacters">The complete portable text length.</param>
/// <param name="MessageCount">The number of archived messages.</param>
/// <param name="MessageIndex">The selected message, or null for the full portable archive.</param>
public sealed record AiContextPage(int SchemaVersion, string ArchiveId, string ContentHash, string Text,
    int Offset, int? NextOffset, bool HasMore, int TotalCharacters, int MessageCount, int? MessageIndex)
{
    /// <summary>Gets additional transport diagnostics without changing the validated archive page.</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
}

internal sealed record AiContextReceipt(int MessageIndex, string? Name, string? CallId, string? Status);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class AiContextToolRead
{
    public required string ArchiveId { get; init; }
    public int Offset { get; set; }
    public int Characters { get; set; } = 12000;
    public int? MessageIndex { get; set; }
}

internal sealed record AiContextCheckpoint(int SchemaVersion, string ArchiveId, string ContentHash, string Summary,
    string SummarySource, string CreatedUtc, int BeforeCharacters, int AfterCharacters, string Measurement,
    string ModelId, long ServiceRevision, string[] PreviousArchives, AiContextReceipt[] RecentToolResults,
    AiContextExcerpt[] Excerpts, int SourceUnits, int OmittedUnits);

internal sealed record AiContextExcerpt(string Id, string ArchiveId, int MessageIndex, int Offset, int Length,
    string Role, string Text, bool Protected);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AiContextSelection(int[] Keep, int[] Omit);

/// <summary>The selector's fixed instruction data, not executable model instructions from source excerpts.</summary>
/// <param name="Goal">The original conversation goal.</param>
/// <param name="Question">The current user question.</param>
/// <param name="SelectionSchema">The generated structured selection schema.</param>
/// <param name="MaximumSelectedCharacters">The remaining source-selection character budget.</param>
internal sealed record AiContextSelectionInput(string Goal, string Question, System.Text.Json.JsonElement SelectionSchema, int MaximumSelectedCharacters);

/// <summary>A packet-local source heading with its actual rendered cost.</summary>
/// <param name="Id">The source index within this selector packet.</param>
/// <param name="Role">The original message role.</param>
/// <param name="CharactersCost">The rendered checkpoint cost if retained.</param>
internal sealed record AiContextSelectionHeader(int Id, string Role, int CharactersCost);

/// <summary>The source-bound heading retained in a checkpoint.</summary>
/// <param name="Id">The immutable archive/message/offset source identity.</param>
/// <param name="Length">The exact retained source length.</param>
/// <param name="Role">The original message role.</param>
/// <param name="CharactersCost">The complete rendered heading and body cost.</param>
internal sealed record AiContextSourceHeader(string Id, int Length, string Role, int CharactersCost);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AiContextRead))]
[JsonSerializable(typeof(AiContextArchive))]
[JsonSerializable(typeof(AiContextPage))]
[JsonSerializable(typeof(AiContextCheckpoint))]
[JsonSerializable(typeof(AiContextToolRead))]
[JsonSerializable(typeof(AiContextExcerpt[]))]
[JsonSerializable(typeof(AiContextSelection))]
[JsonSerializable(typeof(AiContextSelectionInput))]
[JsonSerializable(typeof(AiContextSelectionHeader))]
[JsonSerializable(typeof(AiContextSourceHeader))]
internal sealed partial class AiContextJsonContext : JsonSerializerContext;