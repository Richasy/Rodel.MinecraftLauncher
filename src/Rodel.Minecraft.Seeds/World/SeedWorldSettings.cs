// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Defines original generation settings shared by tiles, searches and bookmarks.</summary>
public sealed record SeedWorldSettings
{
    /// <summary>Gets overworld, the_nether, the_end, or an additional namespaced identifier exposed by the native dimension provider.</summary>
    public string Dimension { get; set; } = "overworld";
    /// <summary>Gets normal, large_biomes, amplified or single_biome generation.</summary>
    public string Preset { get; set; } = "normal";
    /// <summary>Gets the registered biome for single_biome; ignored in the other presets.</summary>
    public string? Biome { get; set; }
}