// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Requests a model-space ray pick, optionally targeting blank outer surfaces for painting.</summary>
/// <param name="Origin">The ray origin in model pixel units.</param>
/// <param name="Direction">A finite, nonzero direction; normalized before intersection.</param>
/// <param name="Layer">The editable Minecraft layer, or null for both layers.</param>
/// <param name="Parts">Visible/editable body parts, or null for all.</param>
/// <param name="SkipTransparent">Whether transparent texels are skipped; false allows drawing on an empty outer surface.</param>
/// <param name="CullBackfaces">Whether faces directed away from the ray origin are excluded.</param>
public sealed record SkinPickRequest(SkinVector Origin, SkinVector Direction, SkinLayer? Layer = null,
    SkinPart[]? Parts = null, bool SkipTransparent = true, bool CullBackfaces = true);