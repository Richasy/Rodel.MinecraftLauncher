// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;
namespace Rodel.Minecraft.Seeds;

/// <summary>Requests a bounded reproducible scan of signed seeds, without claiming to exhaust the entire seed space.</summary>
public sealed record SeedSearchQuery
{
    /// <summary>Gets installed original-version metadata and optional Java selection.</summary>
    [JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets dimension/preset generation settings.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the initial signed seed; subsequent candidates increment with original 64-bit wrapping.</summary>
    public string StartSeed { get; set; } = "0";
    /// <summary>Gets the candidate offset for a new or resumed bounded scan.</summary>
    public long Offset { get; set; }
    /// <summary>Gets the maximum candidates to examine in this attempt.</summary>
    public int MaxSeeds { get; set; } = 10_000;
    /// <summary>Gets the maximum positive results returned in this attempt.</summary>
    public int MaxResults { get; set; } = 5;
    /// <summary>Gets the attempt time budget, checked between native calculations.</summary>
    public int TimeLimitSeconds { get; set; } = 60;
    /// <summary>Gets the biome witness sampling interval; a miss is not proof of absence.</summary>
    public int BiomeStep { get; set; } = 32;
    /// <summary>Gets conditions in dependency order; all must have positive original witnesses.</summary>
    [JsonRequired]
    public SeedCondition[] Conditions { get; set; } = [];
}