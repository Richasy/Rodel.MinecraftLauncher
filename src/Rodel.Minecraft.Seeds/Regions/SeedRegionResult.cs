// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains one frozen block inspection; GenerationPolicy distinguishes original generation from an actual saved snapshot.</summary>
public sealed record SeedRegionResult
{
    /// <summary>Gets the normalized lossless world seed.</summary>
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the original generation settings owning this snapshot.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the exact inclusive scanned and optionally rendered volume.</summary>
    public SeedBounds Bounds { get; set; } = new();
    /// <summary>Gets fixed_region_v1: fresh isolated world, terrain barrier, ordered features and full chunks for this request's range.</summary>
    public string GenerationPolicy { get; set; } = string.Empty;
    /// <summary>Gets positions whose chunks were not saved or could not be read; they are excluded from materials and never treated as air.</summary>
    public long UnknownBlocks { get; set; }
    /// <summary>Gets individual saved-chunk failures without substituting generated blocks or hiding other readable data.</summary>
    public string[] Warnings { get; set; } = [];
    /// <summary>Gets exact block states indexed by the optional volume buffer.</summary>
    public SeedBlockState[] Palette { get; set; } = [];
    /// <summary>Gets little-endian unsigned 16-bit palette indices in Y/Z/X order; empty means blocks were not requested.</summary>
    public byte[] Blocks { get; set; } = [];
    /// <summary>Gets original tint-index colors for biome/state combinations observed in this volume; empty retains untinted original textures.</summary>
    public SeedRegionTint[] TintPalette { get; set; } = [];
    /// <summary>Gets little-endian unsigned 16-bit tint-palette indices in the same Y/Z/X order as Blocks; empty means no original tint metadata is available.</summary>
    public byte[] TintIndices { get; set; } = [];
    /// <summary>Gets namespaced biome identifiers read from the saved volume, with rodel:unknown for absent historical biome data.</summary>
    public string[] BiomePalette { get; set; } = [];
    /// <summary>Gets little-endian unsigned 16-bit saved biome indices in Y/Z/X order, independently of block state identity; empty means biome storage was not requested.</summary>
    public byte[] Biomes { get; set; } = [];
    /// <summary>Gets block-ID counts for every position, including air and fluids.</summary>
    public SeedBlockCount[] Materials { get; set; } = [];
    /// <summary>Gets ordinary containers and dynamic reward blocks within the inclusive query volume.</summary>
    public SeedContainer[] Containers { get; set; } = [];
    /// <summary>Gets the original calculation duration including bounded world initialization and cleanup.</summary>
    public double ElapsedMilliseconds { get; set; }
    /// <summary>Gets the exact original runtime and adapter fingerprint.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Gets whether a previously completed, validated snapshot was reused.</summary>
    public bool Cached { get; set; }
}