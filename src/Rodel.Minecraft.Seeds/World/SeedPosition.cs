// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Identifies a positive original-generation witness; structures include assembled bounds rather than candidate chunks.</summary>
public sealed record SeedPosition
{
    /// <summary>Gets the original biome/structure ID, coordinate origin or shared world spawn.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets structure, biome, origin or spawn.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Gets the X coordinate, using the assembled bounds center for a structure.</summary>
    public int X { get; set; }
    /// <summary>Gets the witness Y, using the assembled bounds center for a structure; this is not a safe-player-spawn promise.</summary>
    public int Y { get; set; }
    /// <summary>Gets the Z coordinate, using the assembled bounds center for a structure.</summary>
    public int Z { get; set; }
    /// <summary>Gets the registered biome for a biome witness.</summary>
    public string? Biome { get; set; }
    /// <summary>Gets the original nonempty piece count for an assembled structure.</summary>
    public int Pieces { get; set; }
    /// <summary>Gets minX/minY/minZ/maxX/maxY/maxZ of the assembled structure, or null for biome witnesses.</summary>
    public int[]? Bounds { get; set; }
}