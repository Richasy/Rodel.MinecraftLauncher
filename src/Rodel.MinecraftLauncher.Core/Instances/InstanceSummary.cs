// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Instances;

/// <summary>Reads an instance registration or an independently unreadable registration without hiding unknown metadata.</summary>
public sealed class InstanceSummary : IInstanceImportResult
{
    /// <summary>Gets the registration identifier, including the directory identifier for unreadable records.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the registered display name when readable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
    /// <summary>Gets the resolved game directory when readable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameDirectory { get; init; }
    /// <summary>Gets the resolved version and dependency directory when readable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MinecraftDirectory { get; init; }
    /// <summary>Gets the explicitly registered version, if present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionId { get; init; }
    /// <summary>Gets the explicitly registered Java path, if present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaPath { get; init; }
    /// <summary>Gets the owning favorite for a dedicated server client, absent for an ordinary local instance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServerId { get; init; }
    /// <summary>Gets the registered ownership marker without adding an absent marker.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Managed { get; init; }
    /// <summary>Gets unreadable for a failed registration read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the failing metadata path.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MetadataPath { get; init; }
    /// <summary>Gets structured failure details for an unreadable registration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
    /// <summary>Gets the stored icon declaration, which may contain future image metadata.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Icon { get; init; }
    /// <summary>Gets unknown persisted metadata and explicit null values preserved verbatim.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}

/// <summary>Source-generated metadata for instance registration summaries.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstanceSummary))]
[JsonSerializable(typeof(IReadOnlyList<InstanceSummary>))]
[JsonSerializable(typeof(InstanceImportPreview))]
[JsonSerializable(typeof(IInstanceImportResult))]
public partial class InstanceJsonContext : JsonSerializerContext;