// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Seeds;

/// <summary>Evaluates an explicitly recorded container table and seed in an isolated original no-player/default-luck context.</summary>
public sealed record SeedLootQuery
{
    /// <summary>Gets the installed original runtime; no player world is opened by it.</summary>
    [JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets the lossless saved world seed used by the isolated context.</summary>
    [JsonRequired]
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the original dimension and reproducible world settings.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the namespaced recorded container block ID.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets the saved container X coordinate used as the loot origin.</summary>
    public int X { get; set; }
    /// <summary>Gets the saved container Y coordinate used as the loot origin.</summary>
    public int Y { get; set; }
    /// <summary>Gets the saved container Z coordinate used as the loot origin.</summary>
    public int Z { get; set; }
    /// <summary>Gets the exact recorded namespaced loot table.</summary>
    public string LootTable { get; set; } = string.Empty;
    /// <summary>Gets the lossless saved container seed; zero retains original nondeterministic behavior.</summary>
    public string LootSeed { get; set; } = string.Empty;
}