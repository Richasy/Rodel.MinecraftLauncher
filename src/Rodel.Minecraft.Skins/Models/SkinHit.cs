// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes an actual ray/quad intersection suitable for an eyedropper or stroke sample.</summary>
/// <param name="Surface">The intersected part, Minecraft layer and face.</param>
/// <param name="X">The face-local horizontal pixel index.</param>
/// <param name="Y">The face-local vertical pixel index.</param>
/// <param name="LocalX">The continuous horizontal pixel-center coordinate for strokes.</param>
/// <param name="LocalY">The continuous vertical pixel-center coordinate for strokes.</param>
/// <param name="Distance">Distance from the ray origin in model pixel units.</param>
/// <param name="Position">The world-space intersection.</param>
/// <param name="Normal">The outward unit surface normal.</param>
public sealed record SkinHit(SkinSurface Surface, int X, int Y, double LocalX, double LocalY,
    float Distance, SkinVector Position, SkinVector Normal);