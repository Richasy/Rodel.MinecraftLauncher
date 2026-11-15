// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Seeds;

/// <summary>Shares the actual row-vector GPU matrix and logical viewport dimensions with accessible container markers.</summary>
/// <param name="Matrix">Original local block coordinates to D3D clip coordinates, with depth in zero to one.</param>
/// <param name="Width">Logical viewport width.</param>
/// <param name="Height">Logical viewport height.</param>
public sealed record SeedSceneProjection(Matrix4x4 Matrix, float Width, float Height)
{
    /// <summary>Projects one original local block point into the same logical pixels used by the native pin overlay.</summary>
    public Vector2 Point(Vector3 local)
    {
        var clip = Vector4.Transform(new Vector4(local, 1), Matrix);
        return new(((clip.X / clip.W) + 1) * Width / 2, (1 - (clip.Y / clip.W)) * Height / 2);
    }
}