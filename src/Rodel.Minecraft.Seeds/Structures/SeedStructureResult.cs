// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains bounded original assembly witnesses and the exact generation context that owns them; an empty result is not an absence proof.</summary>
public sealed record SeedStructureResult
{
    /// <summary>Gets the normalized signed 64-bit seed as a string.</summary>
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the original dimension and preset used for the result.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the frozen reference X coordinate.</summary>
    public int CenterX { get; set; }
    /// <summary>Gets the frozen reference Z coordinate.</summary>
    public int CenterZ { get; set; }
    /// <summary>Gets the inclusive horizontal query radius.</summary>
    public int Radius { get; set; }
    /// <summary>Gets the registered structure IDs actually requested.</summary>
    public string[] Targets { get; set; } = [];
    /// <summary>Gets the number of positive witnesses found before the return limit was applied.</summary>
    public int Found { get; set; }
    /// <summary>Gets native assembly centers and bounds, ordered by horizontal distance, ID and coordinates.</summary>
    public SeedPosition[] Positions { get; set; } = [];
    /// <summary>Gets original query calculation time, excluding JVM startup.</summary>
    public double ElapsedMilliseconds { get; set; }
    /// <summary>Gets the exact original-runtime and adapter fingerprint owning these witnesses.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Gets generated or saved; saved records never claim to cover ungenerated or unreadable chunks.</summary>
    public string Source { get; set; } = "generated";
    /// <summary>Gets individual saved-chunk read failures while retaining other valid records.</summary>
    public string[] Warnings { get; set; } = [];
}