// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines a face-local selection; an inverted selection also includes pixels outside its bounding box.</summary>
/// <param name="X">The left pixel of the selection's bounding box.</param>
/// <param name="Y">The top pixel of the bounding box.</param>
/// <param name="Width">The number of bounding-box columns.</param>
/// <param name="Height">The number of bounding-box rows.</param>
public sealed record SkinSelection(int X, int Y, int Width, int Height)
{
    /// <summary>Gets the selection footprint inside the bounding box.</summary>
    public SkinSelectionShape Shape { get; init; }
    /// <summary>Gets polygon vertices relative to the bounding box's top-left pixel corner.</summary>
    public SkinPoint[]? Points { get; init; }
    /// <summary>Gets row-major selected pixels for Shape=Mask.</summary>
    public bool[]? Mask { get; init; }
    /// <summary>Gets whether to invert the footprint within the addressed model face.</summary>
    public bool Inverted { get; init; }
}