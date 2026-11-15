// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Requests a reproducible geometric or color-based selection without modifying the document.</summary>
public sealed record SkinSelectionRequest
{
    /// <summary>Gets the face on which selection coordinates are interpreted.</summary>
    public required SkinSurface Surface { get; init; }
    /// <summary>Gets the geometric or color-selection kind.</summary>
    public required SkinSelectionKind Kind { get; init; }
    /// <summary>Gets the bounding-box horizontal origin, or the color-selection seed.</summary>
    public int X { get; init; }
    /// <summary>Gets the bounding-box vertical origin, or the color-selection seed.</summary>
    public int Y { get; init; }
    /// <summary>Gets an optional geometric width; omission extends to the face edge.</summary>
    public int? Width { get; init; }
    /// <summary>Gets an optional geometric height; omission extends to the face edge.</summary>
    public int? Height { get; init; }
    /// <summary>Gets polygon vertices relative to the requested bounding box.</summary>
    public SkinPoint[]? Points { get; init; }
    /// <summary>Gets the maximum RGBA channel distance for color selection.</summary>
    public int Tolerance { get; init; }
    /// <summary>Gets a particular paint-layer ID, or null to sample the visible composite.</summary>
    public string? PaintLayerId { get; init; }
    /// <summary>Gets whether the resulting selection is inverted within the face.</summary>
    public bool Inverted { get; init; }
}