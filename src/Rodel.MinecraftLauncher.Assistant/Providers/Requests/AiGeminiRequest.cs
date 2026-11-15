// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiGeminiRequest : AiProviderRequest
{
    /// <summary>Gets native message parts, retaining thought signatures and provider extensions.</summary>
    public required JsonArray Contents { get; init; }
    /// <summary>Gets system instructions, absent for isolated text probes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Instruction? SystemInstruction { get; init; }
    /// <summary>Gets explicitly requested generation options, absent when all are unspecified.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Configuration? GenerationConfig { get; init; }
    /// <summary>Gets offered functions, absent when none are offered.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolGroup[]? Tools { get; init; }

    /// <summary>Contains the selected system instruction text parts.</summary>
    /// <param name="Parts">The complete system instruction parts.</param>
    internal sealed record Instruction(TextPart[] Parts);
    /// <summary>Contains one system text part.</summary>
    /// <param name="Text">The system instruction text.</param>
    internal sealed record TextPart(string Text);
    /// <summary>Groups function declarations using the provider's native shape.</summary>
    /// <param name="FunctionDeclarations">The complete offered function declarations.</param>
    internal sealed record ToolGroup(FunctionDefinition[] FunctionDeclarations);
    /// <summary>Declares a Gemini function while preserving its open input schema.</summary>
    /// <param name="Name">The frozen model-visible tool name.</param>
    /// <param name="Description">The offered tool description.</param>
    /// <param name="ParametersJsonSchema">The external JSON input schema.</param>
    internal sealed record FunctionDefinition(string Name, string Description, JsonObject ParametersJsonSchema);
    internal sealed record Configuration
    {
        /// <summary>Gets the configured output-token budget.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxOutputTokens { get; init; }
        /// <summary>Gets the number of candidates requested for a conversation.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? CandidateCount { get; init; }
        /// <summary>Gets the requested sampling temperature, if configured.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature { get; init; }
    }
}