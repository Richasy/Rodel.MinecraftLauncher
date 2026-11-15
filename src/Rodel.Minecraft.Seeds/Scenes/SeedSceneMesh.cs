// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Seeds;

/// <summary>Owns one stable layer mesh; vertices are local to the snapshot origin to preserve precision at distant world coordinates.</summary>
public sealed class SeedSceneMesh
{
    internal SeedSceneMesh(SeedBlockScene scene, float[] vertices, uint[] colors, Vector3 minimum, Vector3 maximum, int minY, int maxY, int blocks)
    { Scene = scene; Vertices = vertices; VertexColors = colors; Minimum = minimum; Maximum = maximum; MinY = minY; MaxY = maxY; Blocks = blocks; }
    /// <summary>Gets the original snapshot and shared texture atlas.</summary>
    public SeedBlockScene Scene { get; }
    /// <summary>Gets triangle vertices as position XYZ, atlas UV and directional shade, six floats per vertex.</summary>
    public ReadOnlyMemory<float> Vertices { get; }
    /// <summary>Gets one packed RGB multiplier per vertex, derived from its original face tint index; white preserves an untinted texture.</summary>
    public ReadOnlyMemory<uint> VertexColors { get; }
    /// <summary>Gets the drawn local model minimum.</summary>
    public Vector3 Minimum { get; }
    /// <summary>Gets the drawn local model maximum.</summary>
    public Vector3 Maximum { get; }
    /// <summary>Gets the inclusive world slice minimum.</summary>
    public int MinY { get; }
    /// <summary>Gets the inclusive world slice maximum.</summary>
    public int MaxY { get; }
    /// <summary>Gets the number of blocks with renderable models in this slice.</summary>
    public int Blocks { get; }
}