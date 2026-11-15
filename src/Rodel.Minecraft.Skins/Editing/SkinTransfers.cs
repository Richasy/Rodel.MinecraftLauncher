// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

internal static class SkinTransfers
{
    internal static SkinPixelBlock Capture(SkinDocument document, SkinSurface surface, SkinSelection? selection, string? paintLayerId)
    {
        var pixels = SkinCompositor.Pixels(document, paintLayerId);
        var face = SkinLayout.Face(document.Model, surface, document.Size);
        var area = SkinSelections.Validate(face, selection);
        var left = area.Inverted ? 0 : area.X;
        var top = area.Inverted ? 0 : area.Y;
        var width = area.Inverted ? face.Width : area.Width;
        var height = area.Inverted ? face.Height : area.Height;
        var captured = new uint[width * height];
        var mask = new bool[captured.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                mask[index] = SkinSelections.Contains(area, left + x, top + y);
                if (mask[index])
                    captured[index] = pixels[((face.Y + top + y) * document.Size) + face.X + left + x];
            }
        }

        return new(width, height, captured, mask.All(value => value) ? null : mask);
    }

    internal static SkinTransferPlan Plan(SkinDocument document, SkinEdit edit, CancellationToken cancellationToken)
    {
        var surface = edit.Surface ?? throw new ArgumentException("A pixel transfer requires a surface.", nameof(edit));
        var face = SkinLayout.Face(document.Model, surface, document.Size);
        var source = edit.Source ?? surface;
        var sourceFace = SkinLayout.Face(document.Model, source, document.Size);
        var flip = edit.Tool is SkinTool.FlipHorizontal or SkinTool.FlipVertical;
        var clearsSource = edit.Tool is SkinTool.Move or SkinTool.Cut || flip;
        if (clearsSource && (edit.LockAlpha || (edit.Opacity ?? 1) != 1))
            throw new ArgumentException("Cut, move and flip require full opacity and unlocked alpha; use Copy/Paste for partial painting.", nameof(edit));

        var sourceArea = SkinSelections.Validate(sourceFace, edit.SourceSelection ?? (edit.Tool == SkinTool.Cut || flip ? edit.Selection : null));
        var destinationArea = SkinSelections.Validate(face, edit.Selection);
        var block = edit.Tool == SkinTool.Paste ? edit.Clipboard ?? throw new ArgumentException("Paste requires clipboard pixels.", nameof(edit))
            : Capture(document, source, sourceArea, edit.SampleMerged ? null : edit.PaintLayerId ?? SkinDocument.BackgroundLayerId);
        if (block.Width is < 1 or > 128 || block.Height is < 1 or > 128 || block.Pixels is null || block.Pixels.Length != block.Width * block.Height
            || (block.Mask is not null && block.Mask.Length != block.Pixels.Length))
        {
            throw new ArgumentException("Clipboard pixels and mask must match a 1..128 pixel rectangle.", nameof(edit));
        }

        var turns = ((edit.QuarterTurns % 4) + 4) % 4;
        var rotatedWidth = turns % 2 == 0 ? block.Width : block.Height;
        var rotatedHeight = turns % 2 == 0 ? block.Height : block.Width;
        var width = edit.DestinationWidth ?? rotatedWidth;
        var height = edit.DestinationHeight ?? rotatedHeight;
        var left = flip ? sourceArea.Inverted ? 0 : sourceArea.X : edit.X;
        var top = flip ? sourceArea.Inverted ? 0 : sourceArea.Y : edit.Y;
        var writes = new Dictionary<int, SkinTransferPixel>();
        if (edit.Tool != SkinTool.Cut)
        {
            SkinSelections.CheckPoint(face, left, top);
            if (width < 1 || height < 1 || width > face.Width - left || height > face.Height - top)
                throw new ArgumentException("Transformed clipboard pixels must fit in the destination face.", nameof(edit));
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var rx = x * rotatedWidth / width;
                    var ry = y * rotatedHeight / height;
                    if (edit.FlipX || edit.Tool == SkinTool.FlipHorizontal)
                        rx = rotatedWidth - 1 - rx;
                    if (edit.FlipY || edit.Tool == SkinTool.FlipVertical)
                        ry = rotatedHeight - 1 - ry;
                    var (sx, sy) = turns switch
                    {
                        1 => (ry, block.Height - 1 - rx),
                        2 => (block.Width - 1 - rx, block.Height - 1 - ry),
                        3 => (block.Width - 1 - ry, rx),
                        _ => (rx, ry)
                    };
                    var index = (sy * block.Width) + sx;
                    if (block.Mask is not null && !block.Mask[index])
                        continue;
                    var selected = (flip && edit.SourceSelection is null)
                        || SkinSelections.Contains(destinationArea, left + x, top + y);
                    if (!selected)
                    {
                        if (edit.Tool == SkinTool.Move)
                            throw new ArgumentException("A move cannot clip selected destination pixels; enlarge the destination selection.", nameof(edit));
                        continue;
                    }

                    Add(writes, surface, left + x, top + y, block.Pixels[index]);
                }
            }
        }

        var clears = new Dictionary<int, SkinTransferPixel>();
        if (clearsSource)
        {
            var clear = edit.ClearColor is { } color ? SkinColor.Parse(color) : 0u;
            for (var y = 0; y < sourceFace.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < sourceFace.Width; x++)
                {
                    if (SkinSelections.Contains(sourceArea, x, y))
                        Add(clears, source, x, y, clear);
                }
            }

            if (flip)
            {
                foreach (var index in writes.Keys)
                    clears.Remove(index);
            }
        }

        return new([.. writes.Values], [.. clears.Values]);

        void Add(Dictionary<int, SkinTransferPixel> target, SkinSurface targetSurface, int x, int y, uint color)
        {
            Store(targetSurface, x, y);
            if (edit.Mirror != SkinMirrorMode.None)
            {
                var mirrored = SkinGeometry.MirrorPoint(document.Model, targetSurface, x, y, edit.Mirror, edit.MirrorParts, document.Size);
                Store(mirrored.Surface, mirrored.X, mirrored.Y);
            }
            return;

            void Store(SkinSurface addressed, int px, int py)
            {
                var rectangle = SkinLayout.Face(document.Model, addressed, document.Size);
                target.TryAdd(((rectangle.Y + py) * document.Size) + rectangle.X + px, new(addressed, px, py, color));
            }
        }
    }
}