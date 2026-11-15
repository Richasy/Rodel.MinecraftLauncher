// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains original tint-index colors resolved for an actual biome and block state in a frozen volume.</summary>
public sealed record SeedRegionTint
{
    /// <summary>Gets the namespaced actual biome used for color resolution; missing biome data is never assigned a guessed climate.</summary>
    public string BiomeId { get; set; } = string.Empty;
    /// <summary>Gets original 24-bit RGB values by model tint index; -1 means the original provider applies no tint.</summary>
    public int[] Colors { get; set; } = [];
}