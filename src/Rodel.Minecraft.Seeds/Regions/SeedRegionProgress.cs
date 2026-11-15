// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Reports actual work in the current native stage, without inventing an overall generation percentage.</summary>
public sealed record SeedRegionProgress
{
    /// <summary>Gets loading, terrain, features, full or scan.</summary>
    public string Phase { get; set; } = string.Empty;
    /// <summary>Gets completed work units within this stage.</summary>
    public int Completed { get; set; }
    /// <summary>Gets stage work units; zero denotes initialization without a known total.</summary>
    public int Total { get; set; }
}