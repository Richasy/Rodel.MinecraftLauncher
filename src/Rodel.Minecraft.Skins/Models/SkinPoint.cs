// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines a continuous point using pixel-center coordinates.</summary>
/// <param name="X">Horizontal position; integer values address pixel centers.</param>
/// <param name="Y">Vertical position; integer values address pixel centers.</param>
public sealed record SkinPoint(double X, double Y);