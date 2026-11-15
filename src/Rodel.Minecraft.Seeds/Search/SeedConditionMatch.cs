// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Records the chosen shared anchor and original positive positions satisfying one condition.</summary>
public sealed record SeedConditionMatch
{
    /// <summary>Gets the caller's condition key.</summary>
    public string ConditionId { get; set; } = string.Empty;
    /// <summary>Gets the object used by every dependent condition in this result.</summary>
    public SeedPosition Anchor { get; set; } = new();
    /// <summary>Gets positive native witnesses retained for this condition.</summary>
    public SeedPosition[] Positions { get; set; } = [];
}