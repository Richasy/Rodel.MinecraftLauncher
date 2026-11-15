// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes a textured model face with a shared top-left origin and local right/down axes.</summary>
/// <param name="Texture">The atlas rectangle mapped onto this face.</param>
/// <param name="Origin">The face's top-left XYZ position in model pixel units.</param>
/// <param name="Right">The XYZ vector spanning the complete texture width.</param>
/// <param name="Down">The XYZ vector spanning the complete texture height.</param>
public sealed record SkinQuad(SkinFaceInfo Texture, float[] Origin, float[] Right, float[] Down);