// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains the original world's shared spawn, distinct from a player's randomized initial landing position.</summary>
public sealed record SeedSpawnResult
{
    /// <summary>Gets the normalized signed seed.</summary>
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the exact Overworld settings used to initialize the new world.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the XYZ saved by the original new-world spawn initialization.</summary>
    public SeedPosition Position { get; set; } = new();
    /// <summary>Gets original spawn calculation time including its temporary child startup, excluding main-session startup and cache transport.</summary>
    public double ElapsedMilliseconds { get; set; }
    /// <summary>Gets the exact adapter, original game and dependency fingerprint.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Gets whether a completed original result was reused from disk.</summary>
    public bool Cached { get; set; }
}