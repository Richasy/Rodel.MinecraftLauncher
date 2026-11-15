// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Owns one validated composite for repeated render and pick queries without disk reads or history validation.</summary>
/// <remarks>Freezes display pixels from a caller-owned document; subsequent document changes do not affect this snapshot.</remarks>
public sealed class SkinRenderSnapshot(SkinDocument document)
{
    private readonly uint[] _pixels = SkinLayers.Pixels(document);
    private readonly byte[]? _transparency = SkinTransparency.Copy(document.TransparencyMask, document.Pixels.Length);
    private readonly SkinQuad[] _geometry = SkinGeometry.Quads(document.Model, document.Size);

    /// <summary>Gets the frozen arm model.</summary>
    public SkinModel Model { get; } = document.Model;
    /// <summary>Gets the atlas width and height in texels.</summary>
    public int Size { get; } = document.Size;
    /// <summary>Gets the actual atlas height, half-width for capes.</summary>
    public int Height { get; } = document.Height;
    /// <summary>Gets the durable revision; uncommitted previews retain their source revision.</summary>
    public long Revision { get; } = document.Revision;
    /// <summary>Gets borrowed read-only straight-alpha 0xRRGGBBAA display pixels.</summary>
    public ReadOnlyMemory<uint> Pixels => _pixels;
    /// <summary>Checks whether a row-major atlas texel is hidden by the frozen transparency mask.</summary>
    public bool IsMasked(int index) => (uint)index < (uint)_pixels.Length && SkinTransparency.Contains(_transparency, index);
    /// <summary>Returns caller-owned faces from the same mapping used by picking.</summary>
    public SkinQuad[] Quads() => SkinGeometry.Quads(Model, Size);
    /// <summary>Picks this frozen display state without validating history or rebuilding a composite.</summary>
    public SkinHit? Pick(SkinPickRequest request) => SkinGeometry.Pick(Model, Size, _pixels, request);

    /// <summary>Resolves half-open atlas coordinates to the same face and pixel-center convention as ray picking; unused texels return null.</summary>
    public SkinHit? PickAtlas(double x, double y, SkinLayer layer, SkinPart? part = null)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= Size || y >= Height) return null;
        foreach (var quad in _geometry)
        {
            var face = quad.Texture;
            if (face.Surface.Layer != layer || (part is not null && face.Surface.Part != part) || x < face.X || y < face.Y || x >= face.X + face.Width || y >= face.Y + face.Height) continue;
            var position = new System.Numerics.Vector3(quad.Origin) + (new System.Numerics.Vector3(quad.Right) * (float)((x - face.X) / face.Width))
                + (new System.Numerics.Vector3(quad.Down) * (float)((y - face.Y) / face.Height));
            var normal = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(new(quad.Down), new(quad.Right)));
            return new(face.Surface, (int)Math.Floor(x) - face.X, (int)Math.Floor(y) - face.Y, x - face.X - .5, y - face.Y - .5, 0,
                new(position.X, position.Y, position.Z), new(normal.X, normal.Y, normal.Z));
        }

        return null;
    }

    /// <summary>Computes geometric brush coverage without history validation or pixel mutation; layer write permissions remain the edit transaction's responsibility.</summary>
    public SkinBrushMark[] BrushFootprint(SkinEdit edit)
    {
        if (edit.Tool == SkinTool.Fill) throw new ArgumentException("A composite snapshot cannot preview layer-dependent fill. Use the editing session's fill footprint.", nameof(edit));
        var geometry = new SkinDocument { Model = Model, Size = Size, Pixels = _pixels, Layers = [] };
        return new SkinDrawing(geometry, edit with { PaintLayerId = null }, default).Footprint();
    }
}