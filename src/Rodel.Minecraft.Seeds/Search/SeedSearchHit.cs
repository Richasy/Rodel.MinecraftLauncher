// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains a seed with independently inspectable original-generation witnesses.</summary>
public sealed record SeedSearchHit
{
    /// <summary>Gets the exact signed seed string.</summary>
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets positive condition matches for this seed.</summary>
    public SeedConditionMatch[] Matches { get; set; } = [];
}