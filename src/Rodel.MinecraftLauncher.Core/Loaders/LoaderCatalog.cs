// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Loaders;

/// <summary>Reports loader release candidates and the provenance of the catalog used to obtain them.</summary>
public sealed record LoaderCatalog
{
    /// <summary>Gets the normalized loader identifier.</summary>
    public required string Loader { get; init; }

    /// <summary>Gets the game version for which the candidates were requested.</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>Gets the release entries, retaining provider-specific Maven and profile metadata.</summary>
    public required JsonObject[] Versions { get; init; }

    /// <summary>Gets whether the catalog came from the network, explicit cache use, or a cached fallback.</summary>
    public required string Source { get; init; }

    /// <summary>Gets whether only cached metadata was requested.</summary>
    public bool Offline { get; init; }

    /// <summary>Gets the local catalog cache path.</summary>
    public required string CachePath { get; init; }

    /// <summary>Gets the provider URL associated with this catalog.</summary>
    public required string CatalogUrl { get; init; }

    /// <summary>Gets a cache or network warning without discarding usable candidates.</summary>
    public string? Warning { get; init; }

    /// <summary>Gets additional catalog fields retained from cached metadata.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalMetadata { get; set; } = [];
}