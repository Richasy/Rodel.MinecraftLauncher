// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Seeds;

/// <summary>Generates one isolated original world volume for block preview, container lookup and exact material counts.</summary>
public sealed record SeedRegionQuery
{
    /// <summary>Gets the installed original runtime; player saves, mods and identities are not opened.</summary>
    [JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets the lossless original numeric or text seed.</summary>
    [JsonRequired]
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the original dimension and generation preset.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the inclusive volume, bounded to 256 blocks on each horizontal axis.</summary>
    [JsonRequired]
    public SeedBounds Bounds { get; set; } = null!;
    /// <summary>Gets whether to return a complete paletted block volume; preview transport is bounded to four million positions.</summary>
    public bool IncludeBlocks { get; set; }
    /// <summary>Gets whether ordinary containers are filled with their native loot seed and no-player/default-luck context.</summary>
    public bool IncludeLoot { get; set; } = true;
}