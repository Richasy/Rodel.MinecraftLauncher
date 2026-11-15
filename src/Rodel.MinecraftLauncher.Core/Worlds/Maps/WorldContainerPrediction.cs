// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Selects one actual saved container for an explicit original loot prediction.</summary>
public sealed record WorldContainerQuery
{
    /// <summary>Gets the registered instance and save owning the container.</summary>
    public required WorldMapSelection Selection { get; init; }
    /// <summary>Gets the saved dimension containing the container.</summary>
    public string Dimension { get; init; } = "overworld";
    /// <summary>Gets the actual container X coordinate.</summary>
    public int X { get; init; }
    /// <summary>Gets the actual container Y coordinate.</summary>
    public int Y { get; init; }
    /// <summary>Gets the actual container Z coordinate.</summary>
    public int Z { get; init; }
}

/// <summary>Contains an explicitly requested original prediction, separately from the actual saved inventory.</summary>
public sealed record WorldContainerPrediction
{
    /// <summary>Gets the predicted native slots and stacks; they never replace the saved container record.</summary>
    public required SeedContainer Container { get; init; }
    /// <summary>Gets the frozen saved revision whose table and seed were checked.</summary>
    public required string SourceRevision { get; init; }
    /// <summary>Gets the exact original runtime used for the prediction.</summary>
    public required string RuntimeFingerprint { get; init; }
    /// <summary>Gets whether a nonzero saved loot seed was supplied; predictions still use no player and default luck.</summary>
    public bool Deterministic { get; init; }
    /// <summary>Gets context limitations while retaining a usable original prediction.</summary>
    public string[] Warnings { get; init; } = [];
}