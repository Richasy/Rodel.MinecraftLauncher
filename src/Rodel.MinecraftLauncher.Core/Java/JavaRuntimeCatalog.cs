// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Reports Java catalog provenance while retaining Mojang's extensible platform and component metadata.</summary>
public sealed record JavaRuntimeCatalog
{
    /// <summary>Gets whether the catalog came from the network, an explicit cache lookup, or a cached fallback.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>Gets the explicit offline marker, omitted for an online lookup.</summary>
    [JsonPropertyName("offline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Offline { get; init; }

    /// <summary>Gets a recoverable catalog or cache warning, when present.</summary>
    [JsonPropertyName("warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; init; }

    /// <summary>Gets the original platform/component release data and unknown provider fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Platforms { get; set; } = [];

    /// <summary>Reads one platform's open component catalog, returning null when that platform has no object metadata.</summary>
    /// <param name="platform">The provider platform key, such as windows-x64.</param>
    /// <returns>A detached component object, or null when no object is available for the platform.</returns>
    public JsonObject? GetPlatform(string platform) =>
        Platforms.TryGetValue(platform, out var value) && value.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(value.GetRawText())!.AsObject() : null;
}