// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;

namespace Rodel.Minecraft.Seeds;

/// <summary>Preserves one original item stack and its actual inventory slot.</summary>
public sealed record SeedLootItem
{
    /// <summary>Gets the zero-based native container slot.</summary>
    public int Slot { get; set; }
    /// <summary>Gets the namespaced original item ID.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets the original stack amount.</summary>
    public int Count { get; set; }
    /// <summary>Gets original item components such as enchantments; unknown components remain inspectable JSON.</summary>
    public Dictionary<string, JsonElement> Components { get; set; } = [];
}