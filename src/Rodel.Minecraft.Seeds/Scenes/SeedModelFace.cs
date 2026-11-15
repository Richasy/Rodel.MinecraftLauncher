// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Seeds;

internal sealed record SeedModelFace(Vector3[] Positions, Vector2[] TextureCoordinates, Vector3 Cull, float Shade, string Texture, int TintIndex = -1);