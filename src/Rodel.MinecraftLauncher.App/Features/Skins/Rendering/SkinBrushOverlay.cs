// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Freezes face identities and independent brush and selection boundaries for depth-correct GPU and atlas display.</summary>
internal sealed class SkinBrushOverlay
{
    internal uint[] Cells { get; }
    internal SkinBrushMark[] Marks { get; }

    internal SkinBrushOverlay(
        SkinRenderSnapshot snapshot,
        SkinBrushMark[] marks,
        bool fill = false,
        SkinEditorSelection? selection = null)
    {
        var bounds = new List<SkinBrushMark>(marks);
        var size = snapshot.Size;
        Cells = new uint[size * snapshot.Height];
        var faces = SkinLayout.Faces(snapshot.Model, size);
        foreach (var face in faces)
        {
            var id = (uint)(((int)face.Surface.Part * 12) + ((int)face.Surface.Layer * 6) + (int)face.Surface.Face + 1);
            for (var y = face.Y; y < (face.Y + face.Height); y++)
            {
                for (var x = face.X; x < (face.X + face.Width); x++)
                    Cells[(y * size) + x] = id;
            }
        }

        var covered = new bool[size * snapshot.Height];
        foreach (var mark in marks)
        {
            if (mark.Coverage <= 0)
                continue;
            var face = SkinLayout.Face(snapshot.Model, mark.Surface, size);
            covered[((face.Y + mark.Y) * size) + face.X + mark.X] = true;
        }

        foreach (var face in faces)
        {
            for (var y = 0; y < face.Height; y++)
            {
                for (var x = 0; x < face.Width; x++)
                {
                    var index = ((face.Y + y) * size) + face.X + x;
                    if (snapshot.IsMasked(index))
                    {
                        // Four guide edges above the brush and selection fields in the GPU mask.
                        uint guide = 0;
                        if (x == 0 || !snapshot.IsMasked(index - 1))
                            guide |= 1;
                        if (x == face.Width - 1 || !snapshot.IsMasked(index + 1))
                            guide |= 2;
                        if (y == 0 || !snapshot.IsMasked(index - size))
                            guide |= 4;
                        if (y == face.Height - 1 || !snapshot.IsMasked(index + size))
                            guide |= 8;
                        Cells[index] |= guide << 18;
                    }

                    if (selection is not null && (selection.Model == snapshot.Model) && (selection.Size == size) && selection.Contains(index))
                    {
                        uint edges = 0;
                        if ((x == 0) || !selection.Contains(index - 1))
                            edges |= 16384;
                        if ((x == (face.Width - 1)) || !selection.Contains(index + 1))
                            edges |= 32768;
                        if ((y == 0) || !selection.Contains(index - size))
                            edges |= 65536;
                        if ((y == (face.Height - 1)) || !selection.Contains(index + size))
                            edges |= 131072;
                        Cells[index] |= edges;
                        if (edges != 0)
                            bounds.Add(new(face.Surface, x, y, 1));
                    }

                    if (!covered[index])
                        continue;
                    if (fill)
                        Cells[index] |= 4096;
                    if ((x == 0) || !covered[index - 1])
                        Cells[index] |= 256;
                    if ((x == (face.Width - 1)) || !covered[index + 1])
                        Cells[index] |= 512;
                    if ((y == 0) || !covered[index - size])
                        Cells[index] |= 1024;
                    if ((y == (face.Height - 1)) || !covered[index + size])
                        Cells[index] |= 2048;
                }
            }
        }

        Marks = [.. bounds];
    }
}