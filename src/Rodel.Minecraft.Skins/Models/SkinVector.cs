// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Stores a source-generation-friendly XYZ coordinate in model pixel units.</summary>
/// <param name="X">The horizontal world coordinate.</param>
/// <param name="Y">The vertical world coordinate.</param>
/// <param name="Z">The front/back world coordinate.</param>
public sealed record SkinVector(float X, float Y, float Z);