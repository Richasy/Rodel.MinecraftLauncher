// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines one sampled pen position, optionally on a different model face.</summary>
/// <param name="X">Horizontal pixel-center coordinate.</param>
/// <param name="Y">Vertical pixel-center coordinate.</param>
/// <param name="Surface">The sampled face, or null to use the drawing action's face.</param>
/// <param name="BreakBefore">Starts a new path segment after a missed model hit without creating another transaction.</param>
public sealed record SkinStrokePoint(double X, double Y, SkinSurface? Surface = null, bool BreakBefore = false);