// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Plans complete atlas transfers before touching any source pixels; overlapping writes always use frozen input.</summary>
internal static class SkinAtlasTransfers
{
    internal static SkinAtlasBlock Capture(SkinDocument document, bool[] mask, string? layer)
    {
        var size = document.Size;
        if (mask.Length != size * document.Height) throw new ArgumentException("The atlas mask must contain Size*Size entries.", nameof(mask));
        var mapped = Mapped(document);
        var indices = Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray();
        if (indices.Length == 0 || indices.Any(i => mapped[i] is null)) throw new ArgumentException("Select mapped atlas pixels before capturing.", nameof(mask));
        var left = indices.Min(i => i % size); var top = indices.Min(i => i / size);
        var width = indices.Max(i => i % size) - left + 1; var height = indices.Max(i => i / size) - top + 1;
        var pixels = document.GetLayerPixels(layer, writable: false);
        var block = new uint[width * height]; var included = new bool[block.Length];
        foreach (var index in indices)
        {
            var offset = (((index / size) - top) * width) + (index % size) - left;
            block[offset] = pixels[index]; included[offset] = true;
        }

        return new(left, top, new(width, height, block, included));
    }

    internal static bool[] Apply(SkinDocument document, SkinEdit edit, CancellationToken cancellationToken)
    {
        var request = edit.AtlasTransfer ?? throw new ArgumentException("An atlas transfer is required.", nameof(edit));
        if (edit.Tool is not (SkinTool.Move or SkinTool.Cut or SkinTool.Paste) || edit.Selection is not null || edit.SourceSelection is not null
            || edit.PixelMask is not null || edit.Mirror != SkinMirrorMode.None || edit.MirrorParts is not null || edit.Surface is not null || edit.Source is not null
            || edit.Clipboard is not null || edit.LockAlpha || (edit.Opacity ?? 1) != 1 || edit.BlendMode is not null)
        {
            throw new ArgumentException("Atlas transfers require Move/Cut/Paste with full replacement and no face-local parameters.", nameof(edit));
        }

        if (!Enum.IsDefined(request.Layer) || (request.DestinationMask is { } destination && destination.Length != document.Size * document.Height))
            throw new ArgumentException("Invalid atlas destination.", nameof(edit));
        var captured = edit.Tool == SkinTool.Paste ? null : Capture(document, request.SourceMask ?? throw new ArgumentException("A source mask is required."), edit.PaintLayerId);
        var block = captured?.Block ?? request.Block ?? throw new ArgumentException("Paste requires an atlas block.");
        if (block.Width is < 1 or > 128 || block.Height is < 1 or > 128 || block.Pixels.Length != block.Width * block.Height
            || (block.Mask is not null && block.Mask.Length != block.Pixels.Length))
        {
            throw new ArgumentException("Invalid atlas clipboard block.");
        }

        if ((edit.Tool == SkinTool.Paste && request.SourceMask is not null) || (edit.Tool != SkinTool.Paste && request.Block is not null))
            throw new ArgumentException("Transfer source and clipboard cannot be combined.");
        var mapped = Mapped(document); var pixels = document.GetLayerPixels(edit.PaintLayerId);
        var background = document.Model != SkinModel.Cape && edit.PaintLayerId is null or SkinDocument.BackgroundLayerId;
        var writes = new Dictionary<int, uint>(); var clears = new Dictionary<int, uint>();
        var mask = new bool[pixels.Length];
        if (edit.Tool != SkinTool.Cut)
        {
            for (var y = 0; y < block.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < block.Width; x++)
                {
                    var source = (y * block.Width) + x;
                    if (block.Mask is not null && !block.Mask[source]) continue;
                    var px = (long)request.X + x; var py = (long)request.Y + y;
                    var index = px >= 0 && py >= 0 && px < document.Size && py < document.Height ? (int)((py * document.Size) + px) : -1;
                    var legal = index >= 0 && mapped[index] == request.Layer && (request.DestinationMask is null || request.DestinationMask[index]);
                    if (!legal)
                    {
                        if (edit.Tool == SkinTool.Move) throw new ArgumentException("Every moved pixel must have a valid destination.");
                        continue;
                    }

                    CheckOpaque(index, block.Pixels[source]);
                    writes[index] = block.Pixels[source]; mask[index] = true;
                }
            }
        }

        if (edit.Tool is SkinTool.Cut or SkinTool.Move)
        {
            var clear = edit.ClearColor is { } color ? SkinColor.Parse(color) : 0u;
            foreach (var index in Enumerable.Range(0, pixels.Length).Where(i => request.SourceMask![i]))
            {
                var value = background && mapped[index] == SkinLayer.Base ? clear : 0u;
                CheckOpaque(index, value); clears[index] = value;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var pair in clears) pixels[pair.Key] = pair.Value;
        foreach (var pair in writes) pixels[pair.Key] = pair.Value;
        return mask;

        void CheckOpaque(int index, uint color)
        {
            if (background && mapped[index] == SkinLayer.Base && (color & 255) != 255)
                throw new ArgumentException("Background base transfers require opaque pixels and an explicit opaque source fill color.");
        }
    }

    private static SkinLayer?[] Mapped(SkinDocument document)
    {
        var mapped = new SkinLayer?[document.Size * document.Height];
        foreach (var face in SkinLayout.Faces(document.Model, document.Size))
        {
            for (var y = face.Y; y < face.Y + face.Height; y++)
                for (var x = face.X; x < face.X + face.Width; x++) mapped[(y * document.Size) + x] = face.Surface.Layer;
        }

        return mapped;
    }
}