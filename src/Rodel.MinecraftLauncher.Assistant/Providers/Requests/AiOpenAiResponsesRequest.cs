// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiOpenAiResponsesRequest : AiProviderRequest
{
    /// <summary>Gets the requested provider model.</summary>
    public required string Model { get; init; }
    /// <summary>Gets a text prompt or native input items, retaining opaque reasoning state.</summary>
    public required JsonNode Input { get; init; }
    /// <summary>Gets whether the response uses SSE.</summary>
    public bool Stream { get; init; }
    /// <summary>Gets whether provider-side storage is requested; always false.</summary>
    public bool Store { get; } = false;
    /// <summary>Gets additional provider continuation fields requested for a stream.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Include { get; init; }
    /// <summary>Gets the temporary output budget.</summary>
    [JsonPropertyName("max_output_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; init; }
    /// <summary>Gets the requested sampling temperature, if configured.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; init; }
    /// <summary>Gets offered functions, absent when none are offered.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Tool[]? Tools { get; init; }

    /// <summary>Declares a Responses function while preserving its open input schema.</summary>
    /// <param name="Name">The frozen model-visible tool name.</param>
    /// <param name="Description">The offered tool description.</param>
    /// <param name="Parameters">The external JSON input schema.</param>
    internal sealed record Tool(string Name, string Description, JsonObject Parameters)
    {
        /// <summary>Gets the provider's function-tool discriminator.</summary>
        public string Type { get; } = "function";
    }
}