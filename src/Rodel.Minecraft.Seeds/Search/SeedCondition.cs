// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;
namespace Rodel.Minecraft.Seeds;

/// <summary>Defines a positive count/presence constraint with an optional previously matched anchor.</summary>
public sealed record SeedCondition
{
    /// <summary>Gets the stable condition key referenced by dependent conditions.</summary>
    [JsonRequired]
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets structure or biome; absence constraints are not inferred from sampled misses.</summary>
    [JsonRequired]
    public string Kind { get; set; } = string.Empty;
    /// <summary>Gets original IDs accepted by this condition, with any-of semantics.</summary>
    [JsonRequired]
    public string[] Targets { get; set; } = [];
    /// <summary>Gets the minimum assembled structure count; biome presence uses one.</summary>
    public int Minimum { get; set; } = 1;
    /// <summary>Gets the inclusive horizontal radius in blocks.</summary>
    public int Radius { get; set; } = 512;
    /// <summary>Gets origin, the original Overworld spawn, or the ID of a preceding condition; dependent conditions share the chosen anchor.</summary>
    public string Reference { get; set; } = "origin";
    /// <summary>Gets an explicit biome query Y, or null to confirm positive samples at the original base surface.</summary>
    public int? Y { get; set; }
}