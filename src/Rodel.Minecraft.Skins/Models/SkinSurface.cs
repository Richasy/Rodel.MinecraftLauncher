// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Addresses one model face using face-local, top-left-origin pixel coordinates.</summary>
public sealed record SkinSurface
{
    /// <summary>Gets the character body part to edit.</summary>
    public required SkinPart Part { get; init; }
    /// <summary>Gets the base or outer texture layer.</summary>
    public required SkinLayer Layer { get; init; }
    /// <summary>Gets the face whose local coordinates are used.</summary>
    public required SkinFace Face { get; init; }
}