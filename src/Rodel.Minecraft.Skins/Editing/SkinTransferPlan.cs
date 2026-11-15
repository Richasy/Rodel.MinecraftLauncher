// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Separates captured destination pixels from source clearing, before any mutation is performed.</summary>
/// <param name="Writes">Captured and transformed destination writes.</param>
/// <param name="Clears">Source pixels to clear before destination writes.</param>
internal sealed record SkinTransferPlan(SkinTransferPixel[] Writes, SkinTransferPixel[] Clears);