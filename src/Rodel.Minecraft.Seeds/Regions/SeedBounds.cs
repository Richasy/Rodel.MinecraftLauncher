// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Seeds;

/// <summary>Defines an inclusive world-block volume; all six coordinates belong to the selected dimension.</summary>
public sealed record SeedBounds
{
    /// <summary>Gets the western inclusive block coordinate.</summary>
    public int MinX { get; set; }
    /// <summary>Gets the lowest inclusive block coordinate.</summary>
    public int MinY { get; set; }
    /// <summary>Gets the northern inclusive block coordinate.</summary>
    public int MinZ { get; set; }
    /// <summary>Gets the eastern inclusive block coordinate.</summary>
    public int MaxX { get; set; }
    /// <summary>Gets the highest inclusive block coordinate.</summary>
    public int MaxY { get; set; }
    /// <summary>Gets the southern inclusive block coordinate.</summary>
    public int MaxZ { get; set; }
    /// <summary>Gets the volume as a 64-bit count, including air and fluid positions.</summary>
    [JsonIgnore]
    public long Volume => ((long)MaxX - MinX + 1) * ((long)MaxY - MinY + 1) * ((long)MaxZ - MinZ + 1);
    /// <summary>Checks whether an absolute block position belongs to this inclusive volume.</summary>
    public bool Contains(int x, int y, int z) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;
}