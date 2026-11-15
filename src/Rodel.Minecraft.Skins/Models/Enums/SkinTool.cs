// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.Minecraft.Skins;

/// <summary>Names deterministic drawing, pixel transformation and paint-layer operations.</summary>
[JsonConverter(typeof(SkinEnumConverter<SkinTool>))]
public enum SkinTool
{
    Pencil, Eraser, Line, Rectangle, Ellipse, Fill, Replace, Shade, Copy,
    FlipHorizontal, FlipVertical, Stroke, Gradient, AdjustColor, Move, Cut, Paste, Layer
}