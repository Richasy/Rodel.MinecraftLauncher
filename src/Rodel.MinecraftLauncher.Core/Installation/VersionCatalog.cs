// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Installation;

/// <summary>Combines an open Mojang release manifest with local version discovery and cache provenance.</summary>
public sealed record VersionCatalog
{
    /// <summary>Gets whether the release manifest was read from cache.</summary>
    public bool Cached { get; init; }

    /// <summary>Gets whether an offline catalog lookup was requested.</summary>
    public bool Offline { get; init; }

    /// <summary>Gets locally discovered version metadata locations.</summary>
    public LocalVersionSummary[] LocalVersions { get; init; } = [];

    /// <summary>Gets provider extensions and unrecognized manifest values without narrowing their JSON types.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalMetadata { get; set; } = [];

    /// <summary>Gets the provider's release array without narrowing third-party version metadata.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Versions { get; init; }

    /// <summary>Gets the provider's latest-release aliases.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Latest { get; init; }
}