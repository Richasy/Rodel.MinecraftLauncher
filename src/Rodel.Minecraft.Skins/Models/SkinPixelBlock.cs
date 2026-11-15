// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines editable RGBA clipboard pixels and an optional nonrectangular footprint.</summary>
/// <param name="Width">The number of pixel columns.</param>
/// <param name="Height">The number of pixel rows.</param>
/// <param name="Pixels">Row-major #RRGGBBAA packed pixels.</param>
/// <param name="Mask">Included pixels; null includes the entire rectangle.</param>
public sealed record SkinPixelBlock(int Width, int Height, uint[] Pixels, bool[]? Mask = null);