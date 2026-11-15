// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Reports actual search work and a restart cursor, preserving positive results on cooperative cancellation.</summary>
public sealed record SeedSearchResult
{
    /// <summary>Gets running, completed, budget_exhausted, time_limit or cancelled.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Gets candidates fully examined in this attempt.</summary>
    public long Checked { get; set; }
    /// <summary>Gets the first candidate offset not committed as examined.</summary>
    public long NextOffset { get; set; }
    /// <summary>Gets immutable result snapshots reported by the native worker.</summary>
    public SeedSearchHit[] Hits { get; set; } = [];
    /// <summary>Gets native attempt time excluding process initialization.</summary>
    public double ElapsedMilliseconds { get; set; }
}