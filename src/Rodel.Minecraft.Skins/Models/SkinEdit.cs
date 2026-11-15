// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes one undoable drawing action; layer operations need no model surface.</summary>
public sealed record SkinEdit
{
    /// <summary>Gets the deterministic operation to execute.</summary>
    public required SkinTool Tool { get; init; }
    /// <summary>Gets the addressed model face; required except for paint-layer operations and absolute-atlas transfers.</summary>
    public SkinSurface? Surface { get; init; }
    /// <summary>Gets the destination paint layer; null addresses the protected background texture.</summary>
    public string? PaintLayerId { get; init; }
    /// <summary>Targets the document transparency mask with a hard Pencil or Stroke to hide pixels, or Eraser to reveal them.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool TransparencyMask { get; init; }
    /// <summary>Gets the first horizontal pixel coordinate or clipboard destination.</summary>
    public int X { get; init; }
    /// <summary>Gets the first vertical pixel coordinate or clipboard destination.</summary>
    public int Y { get; init; }
    /// <summary>Gets the inclusive second horizontal coordinate for lines, shapes and gradients.</summary>
    public int? X2 { get; init; }
    /// <summary>Gets the inclusive second vertical coordinate for lines, shapes and gradients.</summary>
    public int? Y2 { get; init; }
    /// <summary>Gets a #RRGGBB or #RRGGBBAA foreground color.</summary>
    public string? Color { get; init; }
    /// <summary>Gets the ending color for a gradient.</summary>
    public string? SecondaryColor { get; init; }
    /// <summary>Gets the brush diameter in pixels; omission means one pixel.</summary>
    public int? BrushSize { get; init; }
    /// <summary>Gets the square or circular brush footprint.</summary>
    public SkinBrushShape BrushShape { get; init; }
    /// <summary>Gets painting opacity from zero to one; omission means one. Cut, Move and flips require full opacity.</summary>
    public double? Opacity { get; init; }
    /// <summary>Gets edge falloff from zero for a hard brush to one for a fully soft brush.</summary>
    public double Softness { get; init; }
    /// <summary>Gets the compositing mode; copying defaults to replacement, painting to source-over.</summary>
    public SkinBlendMode? BlendMode { get; init; }
    /// <summary>Gets whether painting preserves alpha and skips empty pixels; destructive pixel transfers require this disabled.</summary>
    public bool LockAlpha { get; init; }
    /// <summary>Gets whether brush footprints and connected fills continue over edges of the same cuboid.</summary>
    public bool WrapFaces { get; init; }
    /// <summary>Gets character-wide or local-part bilateral symmetry.</summary>
    public SkinMirrorMode Mirror { get; init; }
    /// <summary>Gets the visible parts eligible for Body symmetry. An absent opposite limb reflects through its cuboid's
    /// opposite face (front/back, left/right, top/bottom), not across the same face. Null retains whole-body pairing;
    /// heads, torsos and explicit Part symmetry retain bilateral reflection. Source targeting and selections are unchanged.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SkinPart[]? MirrorParts { get; init; }
    /// <summary>Gets whether rectangles or ellipses include their interior.</summary>
    public bool Filled { get; init; }
    /// <summary>Gets an optional destination selection; mirrored marks use the reflected selection.</summary>
    public SkinSelection? Selection { get; init; }
    /// <summary>Gets an optional atlas-row-major painting write mask of Size*Size entries; null is unrestricted and all-false forbids writes.
    /// It intersects Selection and clips final mirrored writes without expanding the tool's original scope.
    /// Pixel transfers do not support this mask; layer operations ignore it.</summary>
    public bool[]? PixelMask { get; init; }
    /// <summary>Gets the sampled positions for a continuous stroke.</summary>
    public SkinStrokePoint[]? Points { get; init; }
    /// <summary>Gets an absolute-atlas Move/Cut/Paste request, mutually exclusive with face-local drawing parameters.</summary>
    public SkinAtlasTransfer? AtlasTransfer { get; init; }
    /// <summary>Gets the source face for copying; omission uses the destination face.</summary>
    public SkinSurface? Source { get; init; }
    /// <summary>Gets the captured source selection, independent of the destination clipping selection.</summary>
    public SkinSelection? SourceSelection { get; init; }
    /// <summary>Gets caller-owned clipboard pixels for paste.</summary>
    public SkinPixelBlock? Clipboard { get; init; }
    /// <summary>Gets clockwise quarter turns applied to copied pixels before scaling and placement.</summary>
    public int QuarterTurns { get; init; }
    /// <summary>Gets whether clipboard pixels are reflected horizontally after rotation.</summary>
    public bool FlipX { get; init; }
    /// <summary>Gets whether clipboard pixels are reflected vertically after rotation.</summary>
    public bool FlipY { get; init; }
    /// <summary>Gets the nearest-neighbor destination width; omission preserves the rotated width.</summary>
    public int? DestinationWidth { get; init; }
    /// <summary>Gets the nearest-neighbor destination height; omission preserves the rotated height.</summary>
    public int? DestinationHeight { get; init; }
    /// <summary>Gets the opaque replacement color required when clearing the background's base surface.</summary>
    public string? ClearColor { get; init; }
    /// <summary>Gets the reference color for replacement.</summary>
    public string? MatchColor { get; init; }
    /// <summary>Gets the maximum per-channel RGBA distance for bucket and color replacement matching.</summary>
    public int Tolerance { get; init; }
    /// <summary>Gets the bucket extent; connected fill is the default.</summary>
    public SkinFillMode FillMode { get; init; }
    /// <summary>Gets whether copying or bucket/replacement matching samples the visible composite instead of the target layer.</summary>
    public bool SampleMerged { get; init; }
    /// <summary>Gets the linear or radial gradient shape.</summary>
    public SkinGradientKind GradientKind { get; init; }
    /// <summary>Gets an RGB channel offset for shade, from -255 to 255.</summary>
    public int Amount { get; init; }
    /// <summary>Gets a hue rotation in degrees, from -360 to 360.</summary>
    public double Hue { get; init; }
    /// <summary>Gets an additive HSV saturation change from -1 to 1.</summary>
    public double Saturation { get; init; }
    /// <summary>Gets an additive HSV value change from -1 to 1.</summary>
    public double Brightness { get; init; }
    /// <summary>Gets RGB contrast around mid-gray, from -1 to 1.</summary>
    public double Contrast { get; init; }
    /// <summary>Gets the paint-layer operation when Tool is Layer.</summary>
    public SkinLayerChange? LayerChange { get; init; }
}