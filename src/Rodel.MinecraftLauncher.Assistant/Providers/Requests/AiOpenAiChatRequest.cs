// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

internal sealed record AiOpenAiChatRequest : AiProviderRequest
{
    /// <summary>Gets the requested provider model.</summary>
    public required string Model { get; init; }
    /// <summary>Gets native messages, including opaque continuation fields that must not be projected away.</summary>
    public required JsonArray Messages { get; init; }
    /// <summary>Gets whether the response uses SSE.</summary>
    public bool Stream { get; init; }
    /// <summary>Gets the number of choices for a single-text request; omitted for streaming.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? N { get; init; }
    /// <summary>Gets the temporary output budget.</summary>
    [JsonPropertyName("max_completion_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCompletionTokens { get; init; }
    /// <summary>Gets the requested sampling temperature, if configured.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; init; }
    /// <summary>Gets streaming usage options; absent for non-streaming requests.</summary>
    [JsonPropertyName("stream_options"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamConfiguration? StreamOptions { get; init; }
    /// <summary>Gets offered tools, absent when none are offered.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Tool[]? Tools { get; init; }

    /// <summary>Requests actual provider usage in the stream.</summary>
    /// <param name="IncludeUsage">Whether the provider should send token accounting.</param>
    internal sealed record StreamConfiguration([property: JsonPropertyName("include_usage")] bool IncludeUsage);

    /// <summary>Wraps one function declaration in the Chat Completions tool shape.</summary>
    /// <param name="Function">The offered function identity and external schema.</param>
    internal sealed record Tool(FunctionDefinition Function)
    {
        /// <summary>Gets the provider's function-tool discriminator.</summary>
        public string Type { get; } = "function";
    }

    /// <summary>Declares a function while leaving its external input schema open.</summary>
    /// <param name="Name">The frozen model-visible tool name.</param>
    /// <param name="Description">The offered tool description.</param>
    /// <param name="Parameters">The external JSON input schema.</param>
    internal sealed record FunctionDefinition(string Name, string Description, JsonObject Parameters);
}