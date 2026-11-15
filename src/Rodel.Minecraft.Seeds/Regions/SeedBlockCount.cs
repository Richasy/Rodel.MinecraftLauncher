// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Reports the number of blocks of one original ID, before any tool, enchantment or drop calculation.</summary>
public sealed record SeedBlockCount
{
    /// <summary>Gets the namespaced original block ID.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets the exact count in the frozen inclusive query volume.</summary>
    public long Count { get; set; }
}