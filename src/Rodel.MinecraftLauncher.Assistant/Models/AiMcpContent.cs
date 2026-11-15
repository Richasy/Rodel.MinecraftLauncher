// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>One consumed MCP text/image block or an explicit unsupported-content placeholder.</summary>
/// <param name="Type">The sanitized native MCP block discriminator.</param>
internal sealed record AiMcpContent(string Type)
{
    /// <summary>Gets a textual MCP result block, absent for unsupported content types.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }
    /// <summary>Gets the actual inline image bytes returned by MCP, never bytes read from a supplied file path.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? Data { get; init; }
    /// <summary>Gets the server-declared image media type for the preserved binary content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; init; }
    /// <summary>Gets the original byte count, retained when diagnostics omit the image body.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ByteLength { get; init; }
    /// <summary>Gets the explicit consumption limitation for a non-text result block.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unavailable { get; init; }
}