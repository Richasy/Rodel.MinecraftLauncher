// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Reports provider evidence of reasoning without inventing hidden reasoning content.</summary>
/// <param name="Evidence">The observed item, block, thought-part or usage evidence category.</param>
/// <param name="Phase">The provider observation's lifecycle phase.</param>
internal sealed record AiReasoningObservation(string Evidence, string Phase)
{
    /// <summary>Gets the provider reasoning item's identity, if supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderItemId { get; init; }
    /// <summary>Gets whether the provider reported encrypted continuation state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OpaqueStatePresent { get; init; }
    /// <summary>Gets whether the provider hid the reasoning block.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Redacted { get; init; }
    /// <summary>Gets reasoning usage when only token accounting provides evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ReasoningTokens { get; init; }
}