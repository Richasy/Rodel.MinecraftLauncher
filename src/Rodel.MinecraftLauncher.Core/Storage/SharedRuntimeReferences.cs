// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>References the shared dependency sets published by one completed directory migration.</summary>
internal sealed record SharedRuntimeReferences
{
    /// <summary>Gets the on-disk dependency-reference format.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the published assets directory's content identity.</summary>
    public string Assets { get; set; } = string.Empty;
    /// <summary>Gets the published libraries directory's content identity.</summary>
    public string Libraries { get; set; } = string.Empty;
    /// <summary>Gets each version's published client archive identity.</summary>
    public Dictionary<string, string> Clients { get; init; } = [];
}

/// <summary>Source-generated metadata for storage-owned reference documents.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SharedRuntimeReferences))]
internal partial class StorageJsonContext : JsonSerializerContext;