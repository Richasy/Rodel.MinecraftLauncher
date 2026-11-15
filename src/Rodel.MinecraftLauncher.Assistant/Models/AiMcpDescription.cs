// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>A bounded MCP inspection result; external schemas and diagnostic payloads remain open.</summary>
public sealed record AiMcpDescription
{
    /// <summary>Gets the original registration identity, never replaced by secret scrubbing.</summary>
    public required string Id { get; init; }
    /// <summary>Gets ready, disabled or failed.</summary>
    public required string Status { get; init; }
    /// <summary>Gets the scrubbed configured display name when a session is ready.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
    /// <summary>Gets the owned child process identifier when connected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; init; }
    /// <summary>Gets the external server's scrubbed implementation name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServerName { get; init; }
    /// <summary>Gets the external server's scrubbed implementation version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServerVersion { get; init; }
    /// <summary>Gets supported tool descriptions only for ready sessions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiMcpToolDescription[]? Tools { get; init; }
    /// <summary>Gets diagnostics for external tool entries rejected during catalog discovery.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiMcpCatalogWarning[]? Warnings { get; init; }
    /// <summary>Gets inspection failure details, absent for ready or disabled registrations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
}