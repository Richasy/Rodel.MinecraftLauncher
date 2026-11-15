// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;
namespace Rodel.Minecraft.Seeds;

/// <summary>Requests a bounded original-generation tile; null Y selects the base surface.</summary>
public sealed record SeedMapQuery
{
    /// <summary>Gets the original version and Java selection.</summary>
    [JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets the lossless signed 64-bit seed or original text-seed input.</summary>
    [JsonRequired]
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the dimension and original world preset.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the western sample origin in world blocks.</summary>
    public int MinX { get; set; }
    /// <summary>Gets the northern sample origin in world blocks.</summary>
    public int MinZ { get; set; }
    /// <summary>Gets the world-block interval between adjacent samples.</summary>
    public int Step { get; set; } = 16;
    /// <summary>Gets the number of samples on each side, bounded to 256.</summary>
    public int Side { get; set; } = 64;
    /// <summary>Gets the explicit biome slice; null requests the original base surface.</summary>
    public int? Y { get; set; }
    /// <summary>Gets whether base columns also identify water/lava and the solid terrain height.</summary>
    public bool Terrain { get; set; } = true;
    /// <summary>Gets whether to generate actual feature-stage surface blocks; requires adapter support, no Y slice and a span at most 128 blocks.</summary>
    public bool Decorated { get; set; }
    /// <summary>Gets original structure IDs to query; null includes all, while an empty array disables markers.</summary>
    public string[]? Structures { get; set; }
}