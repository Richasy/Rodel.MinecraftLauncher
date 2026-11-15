// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines a non-destructive paint layer above the required background texture.</summary>
/// <param name="Id">The unique, stable layer identifier, excluding the reserved background identifier.</param>
/// <param name="Name">The user-facing layer label.</param>
/// <param name="Pixels">Document-sized row-major RGBA pixels, independent of Minecraft's base/outer surfaces.</param>
/// <param name="Visible">Whether the layer contributes to the exported composite.</param>
/// <param name="Opacity">The source-over layer opacity, from zero through one.</param>
/// <param name="Locked">Whether pixel writes, deletion and merging are rejected.</param>
public sealed record SkinPaintLayer(string Id, string Name, uint[] Pixels, bool Visible = true, double Opacity = 1, bool Locked = false)
{
    internal SkinPaintLayer CopyPixels() => this with { Pixels = (uint[])Pixels.Clone() };
}