// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Defines the atlas rectangle and texture resolution of a model face.</summary>
/// <param name="Surface">The part, layer and face addressed by this rectangle.</param>
/// <param name="X">The left atlas pixel of the face.</param>
/// <param name="Y">The top atlas pixel of the face.</param>
/// <param name="Width">The horizontal texture resolution of the face.</param>
/// <param name="Height">The vertical texture resolution of the face.</param>
public sealed record SkinFaceInfo(SkinSurface Surface, int X, int Y, int Width, int Height);