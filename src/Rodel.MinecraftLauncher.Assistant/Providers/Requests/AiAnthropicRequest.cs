// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiAnthropicRequest : AiProviderRequest
{
    /// <summary>Gets the requested provider model.</summary>
    public required string Model { get; init; }
    /// <summary>Gets native message blocks, including signed or redacted continuation state.</summary>
    public required JsonArray Messages { get; init; }
    /// <summary>Gets whether the response uses SSE.</summary>
    public bool Stream { get; init; }
    /// <summary>Gets the explicit limit or provider-advertised maximum required by native Anthropic; never a launcher-invented default.</summary>
    [JsonPropertyName("max_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }
    /// <summary>Gets joined system instructions, absent for isolated text probes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; init; }
    /// <summary>Gets the requested sampling temperature, if configured.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; init; }
    /// <summary>Gets offered functions, absent when none are offered.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Tool[]? Tools { get; init; }

    /// <summary>Declares an Anthropic function while preserving its open input schema.</summary>
    /// <param name="Name">The frozen model-visible tool name.</param>
    /// <param name="Description">The offered tool description.</param>
    /// <param name="InputSchema">The external JSON input schema.</param>
    internal sealed record Tool(string Name, string Description, [property: JsonPropertyName("input_schema")] JsonObject InputSchema);
}