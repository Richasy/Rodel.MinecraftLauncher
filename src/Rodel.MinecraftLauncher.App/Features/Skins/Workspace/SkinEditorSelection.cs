// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Owns one immutable atlas selection shared by every viewport and every subsequent painting operation.</summary>
internal sealed class SkinEditorSelection
{
    private readonly bool[] _pixels;
    internal SkinModel Model { get; }
    internal int Size { get; }
    internal int Count { get; }

    internal bool Contains(int index) => _pixels[index];

    internal bool[] CopyMask() => (bool[])_pixels.Clone();

    internal SkinEditorSelection(SkinModel model, int size, bool[] pixels)
    {
        Model = model;
        Size = size;
        _pixels = pixels;
        Count = pixels.Count(value => value);
    }

    internal (int X, int Y) Origin
    {
        get
        {
            var indices = Enumerable.Range(0, _pixels.Length).Where(i => _pixels[i]).ToArray();
            return indices.Length == 0 ? (0, 0) : (indices.Min(i => i % Size), indices.Min(i => i / Size));
        }
    }

    internal static SkinEditorSelection Rectangle(
        SkinRenderSnapshot snapshot,
        SkinSelectionPoint first,
        SkinSelectionPoint last,
        SkinLayer layer,
        int parts,
        bool atlas)
    {
        var size = snapshot.Size;
        var left = Math.Clamp(Math.Min(first.X, last.X), 0, size - 1);
        var right = Math.Clamp(Math.Max(first.X, last.X), 0, size - 1);
        var top = Math.Clamp(Math.Min(first.Y, last.Y), 0, snapshot.Height - 1);
        var bottom = Math.Clamp(Math.Max(first.Y, last.Y), 0, snapshot.Height - 1);
        var pixels = new bool[size * snapshot.Height];
        foreach (var face in SkinLayout.Faces(snapshot.Model, size))
        {
            if ((face.Surface.Layer != layer) || ((parts & (1 << (int)face.Surface.Part)) == 0) || (!atlas && (face.Surface != first.Surface)))
                continue;
            for (var y = Math.Max(top, face.Y); y <= Math.Min(bottom, face.Y + face.Height - 1); y++)
            {
                for (var x = Math.Max(left, face.X); x <= Math.Min(right, face.X + face.Width - 1); x++)
                    pixels[(y * size) + x] = true;
            }
        }

        return new(snapshot.Model, size, pixels);
    }
}

/// <summary>Identifies a texture texel independently of whether a planar pointer currently lies on a mapped face.</summary>
internal readonly record struct SkinSelectionPoint(int X, int Y, SkinSurface? Surface = null);