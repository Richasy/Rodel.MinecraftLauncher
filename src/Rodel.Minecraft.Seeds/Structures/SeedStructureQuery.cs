// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Seeds;

/// <summary>Finds positive assembled-structure witnesses around a block coordinate in one seed, without generating decorated chunks.</summary>
public sealed record SeedStructureQuery
{
    /// <summary>Gets the installed original runtime used by the query.</summary>
    [JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets the lossless numeric seed or original text-seed input.</summary>
    [JsonRequired]
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the original dimension and world preset.</summary>
    public SeedWorldSettings World { get; set; } = new();
    /// <summary>Gets the reference X coordinate for the horizontal radius and distance ordering.</summary>
    public int CenterX { get; set; }
    /// <summary>Gets the reference Z coordinate for the horizontal radius and distance ordering.</summary>
    public int CenterZ { get; set; }
    /// <summary>Gets the inclusive horizontal radius in blocks, bounded to 8192.</summary>
    public int Radius { get; set; } = 2048;
    /// <summary>Gets the original structure IDs to include; at least one registered ID is required.</summary>
    [JsonRequired]
    public string[] Targets { get; set; } = [];
    /// <summary>Gets the maximum returned distance-ordered witnesses, bounded to 256.</summary>
    public int Limit { get; set; } = 100;
}