// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains bounded map samples with a saved revision or original runtime and separate completion provenance.</summary>
public sealed record SeedMapTile
{
    /// <summary>Gets generated, saved, or mixed when a saved snapshot is completed by original preview samples.</summary>
    public string Source { get; set; } = "generated";
    /// <summary>Gets the saved display name for exported provenance, when inspecting a save.</summary>
    public string? SourceName { get; set; }
    /// <summary>Gets the normalized signed 64-bit seed as a string.</summary>
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the generation configuration used for this tile.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the western sample origin.</summary>
    public int MinX { get; set; }
    /// <summary>Gets the northern sample origin.</summary>
    public int MinZ { get; set; }
    /// <summary>Gets the interval between samples in world blocks.</summary>
    public int Step { get; set; }
    /// <summary>Gets the number of samples per row/column.</summary>
    public int Side { get; set; }
    /// <summary>Gets the explicit slice Y, or null for a base surface.</summary>
    public int? Y { get; set; }
    /// <summary>Gets the selected native dimension's minimum build height, when reported by its original runtime.</summary>
    public int? DimensionMinY { get; set; }
    /// <summary>Gets original biome IDs indexed by the sample array; empty means the native format has no biome API.</summary>
    public string[] Palette { get; set; } = [];
    /// <summary>Gets the row-major palette index for each sample, or -1 when no native biome exists.</summary>
    public int[] Biomes { get; set; } = [];
    /// <summary>Gets sampled solid terrain heights, or the slice Y; values below dimension minimum represent void.</summary>
    public int[] Heights { get; set; } = [];
    /// <summary>Gets row-major fluid categories: zero none, one water, two lava, three other.</summary>
    public int[] Fluids { get; set; } = [];
    /// <summary>Gets native assembled structure starts whose bounds centers lie within this tile.</summary>
    public SeedPosition[] Structures { get; set; } = [];
    /// <summary>Gets original calculation time, excluding process startup and cache transport.</summary>
    public double ElapsedMilliseconds { get; set; }
    /// <summary>Gets the saved revision or exact original-runtime fingerprint owning this tile.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Gets whether this read reused a previously completed tile.</summary>
    public bool Cached { get; set; }
    /// <summary>Gets available saved or preview samples; an empty array means every original generated sample is known.</summary>
    public bool[] Known { get; set; } = [];
    /// <summary>Gets the row-major actual saved-data mask; empty uses Source for a pure generated or saved tile.</summary>
    public bool[] Saved { get; set; } = [];
    /// <summary>Gets samples in absent chunks or native pre-surface generation stages; damaged, unreadable and completed air chunks are never eligible for preview.</summary>
    public bool[] PreviewEligible { get; set; } = [];
    /// <summary>Gets the independent original-runtime fingerprint supplying preview pixels; Fingerprint remains the saved revision.</summary>
    public string PreviewFingerprint { get; set; } = string.Empty;
    /// <summary>Gets separately identified original predictions whose anchor chunks have no saved data; Structures retains actual saved witnesses.</summary>
    public SeedPosition[] PreviewStructures { get; set; } = [];
    /// <summary>Gets material RGB colors from saved or generated top blocks; empty uses the biome display palette for a coarse preview.</summary>
    public uint[] SurfaceColors { get; set; } = [];
    /// <summary>Gets the actual top surface heights, including tree canopies and fluids; empty uses Heights.</summary>
    public int[] SurfaceHeights { get; set; } = [];
    /// <summary>Gets actual sampled fluid depths; empty means depth was not available.</summary>
    public int[] WaterDepths { get; set; } = [];
    /// <summary>Gets top heights with one sample of neighboring padding on all sides; int.MinValue marks unknown neighbors.</summary>
    public int[] ReliefHeights { get; set; } = [];
    /// <summary>Gets frozen per-sample shading when combining independent saved and predicted sources; empty derives shading from this tile's own heights.</summary>
    public double[] SurfaceShades { get; set; } = [];
    /// <summary>Gets whether this generated tile includes original feature-stage blocks, rather than base terrain only.</summary>
    public bool Decorated { get; set; }
    /// <summary>Gets individual saved-chunk read warnings without inventing terrain for unavailable chunks.</summary>
    public string[] Warnings { get; set; } = [];
}