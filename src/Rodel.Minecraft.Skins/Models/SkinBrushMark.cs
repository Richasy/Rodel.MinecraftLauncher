// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes one brush-covered texel before blending, shared by UV and projected cursors.</summary>
/// <param name="Surface">The addressed Minecraft surface.</param>
/// <param name="X">The face-local horizontal texel coordinate.</param>
/// <param name="Y">The face-local vertical texel coordinate.</param>
/// <param name="Coverage">The combined shape and opacity coverage, from zero to one.</param>
public sealed record SkinBrushMark(SkinSurface Surface, int X, int Y, double Coverage);