// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains registries read from the exact original runtime, not a manually curated subset.</summary>
/// <param name="Version">Exact original game version.</param>
/// <param name="Dimensions">Original dimension sources and legal heights.</param>
/// <param name="Biomes">All registered original biomes.</param>
/// <param name="Structures">All registered original structures.</param>
public sealed record SeedCatalog(string Version, SeedDimension[] Dimensions, SeedBiome[] Biomes, SeedStructure[] Structures)
{
    /// <summary>Gets localized names read from that installed game's language assets when available.</summary>
    public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    /// <summary>Gets the exact runtime fingerprint to bind requests and persistent results.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Gets default state properties read from the original block registry, keyed by namespaced block ID.</summary>
    public Dictionary<string, Dictionary<string, string>> BlockDefaults { get; set; } = [];
    /// <summary>Gets whether the original shared-spawn selection is determined by the seed and world settings alone.</summary>
    public bool SeedBoundSpawn { get; set; } = true;
    /// <summary>Gets whether this adapter can sample actual feature-stage top blocks in bounded local map tiles.</summary>
    public bool DecoratedMaps { get; set; }
}