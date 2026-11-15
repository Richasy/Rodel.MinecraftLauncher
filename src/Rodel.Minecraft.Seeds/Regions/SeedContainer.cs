// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Describes a generated container or a dynamic reward block at its actual world position.</summary>
public sealed record SeedContainer
{
    /// <summary>Gets the original namespaced block ID, including chest, barrel, dispenser or vault.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets the absolute X coordinate.</summary>
    public int X { get; set; }
    /// <summary>Gets the absolute Y coordinate.</summary>
    public int Y { get; set; }
    /// <summary>Gets the absolute Z coordinate.</summary>
    public int Z { get; set; }
    /// <summary>Gets generated, unevaluated or dynamic_reward; dynamic rewards never claim a seed-fixed inventory.</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>Gets the native generated slot count or inspected saved-slot extent; saved capacity is not inferred from absent tail slots, and dynamic rewards use zero.</summary>
    public int Slots { get; set; }
    /// <summary>Gets the original loot table when one is attached to this container.</summary>
    public string? LootTable { get; set; }
    /// <summary>Gets the lossless original container loot seed, independently of the world seed.</summary>
    public string? LootSeed { get; set; }
    /// <summary>Gets nonempty original stacks after optional native loot evaluation.</summary>
    public SeedLootItem[] Items { get; set; } = [];
}