// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.Numerics;

namespace Rodel.Minecraft.Seeds;

/// <summary>Contains original block-model triangles and a shared original-texture atlas for one immutable region snapshot.</summary>
public sealed class SeedBlockScene
{
    private readonly SeedModelFace[][] _models;
    private readonly bool[] _occluders;
    private readonly byte[] _texture;
    private readonly int _width;
    private readonly int _depth;

    internal SeedBlockScene(SeedRegionResult region, SeedModelFace[][] models, bool[] occluders, byte[] texture, int textureSize, string[] unavailable)
    {
        Region = region;
        _models = models;
        _occluders = occluders;
        _texture = texture;
        TextureSize = textureSize;
        UnavailableModels = unavailable;
        _width = region.Bounds.MaxX - region.Bounds.MinX + 1;
        _depth = region.Bounds.MaxZ - region.Bounds.MinZ + 1;
    }

    /// <summary>Gets the complete original state volume owning these models and container coordinates.</summary>
    public SeedRegionResult Region { get; }
    /// <summary>Gets the square RGBA atlas dimension; original textures use nearest sampling.</summary>
    public int TextureSize { get; }
    /// <summary>Gets original RGBA texels, with first-frame sampling for animated textures.</summary>
    public ReadOnlyMemory<byte> TexturePixels => _texture;
    /// <summary>Gets original block IDs whose special or missing models could not be drawn; block counts remain available.</summary>
    public IReadOnlyList<string> UnavailableModels { get; }

    /// <summary>Builds visible original model faces for an inclusive Y slice, exposing faces at newly cut boundaries; camera updates reuse this mesh.</summary>
    public SeedSceneMesh CreateMesh(int minY, int maxY, CancellationToken cancellationToken = default)
    {
        var b = Region.Bounds;
        if (minY < b.MinY || maxY > b.MaxY || maxY < minY) throw new ArgumentOutOfRangeException(nameof(minY), "The preview slice must belong to its frozen region.");
        var vertices = new List<float>();
        var colors = new List<uint>();
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        var blocks = 0;
        var stride = _width * _depth;
        var bytes = Region.Blocks;
        for (var y = minY; y <= maxY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var z = 0; z < _depth; z++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var index = ((y - b.MinY) * stride) + (z * _width) + x;
                    var palette = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2));
                    var faces = _models[palette];
                    if (faces.Length == 0) continue;
                    blocks++;
                    var offset = new Vector3(x, y - b.MinY, z);
                    foreach (var face in faces)
                    {
                        var step = face.Cull;
                        if (step != Vector3.Zero)
                        {
                            var nx = x + (int)step.X;
                            var ny = y + (int)step.Y;
                            var nz = z + (int)step.Z;
                            if (nx >= 0 && nx < _width && ny >= minY && ny <= maxY && nz >= 0 && nz < _depth)
                            {
                                var neighbor = ((ny - b.MinY) * stride) + (nz * _width) + nx;
                                if (_occluders[BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(neighbor * 2, 2))]) continue;
                            }
                        }

                        if (vertices.Count > 36_000_000) throw new InvalidOperationException("The selected slice exceeds the preview geometry budget. Select fewer layers.");
                        var tint = Tint(index, face.TintIndex);
                        ReadOnlySpan<int> order = [0, 1, 2, 0, 2, 3];
                        foreach (var corner in order)
                        {
                            var point = face.Positions[corner] + offset;
                            minimum = Vector3.Min(minimum, point);
                            maximum = Vector3.Max(maximum, point);
                            vertices.Add(point.X); vertices.Add(point.Y); vertices.Add(point.Z);
                            vertices.Add(face.TextureCoordinates[corner].X); vertices.Add(face.TextureCoordinates[corner].Y); vertices.Add(face.Shade);
                            colors.Add(tint);
                        }
                    }
                }
            }
        }

        if (vertices.Count == 0) { minimum = Vector3.Zero; maximum = new(_width, maxY - minY + 1, _depth); }

        return new(this, [.. vertices], [.. colors], minimum, maximum, minY, maxY, blocks);
    }

    private uint Tint(int voxel, int tintIndex)
    {
        if (tintIndex < 0 || Region.TintIndices.Length == 0) return 0xffffff;
        var entry = BinaryPrimitives.ReadUInt16LittleEndian(Region.TintIndices.AsSpan(voxel * 2, 2));
        if (entry >= Region.TintPalette.Length) throw new InvalidDataException("The original model tint index exceeds its frozen palette.");
        var colors = Region.TintPalette[entry].Colors;
        if (tintIndex >= colors.Length || colors[tintIndex] == -1) return 0xffffff;
        return (uint)colors[tintIndex] & 0xffffff;
    }
}