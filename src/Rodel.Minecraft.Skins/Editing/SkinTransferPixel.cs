// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes a validated model-face pixel write.</summary>
/// <param name="Surface">The addressed face.</param>
/// <param name="X">The local horizontal pixel index.</param>
/// <param name="Y">The local vertical pixel index.</param>
/// <param name="Color">The packed RGBA value.</param>
internal readonly record struct SkinTransferPixel(SkinSurface Surface, int X, int Y, uint Color);