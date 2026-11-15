// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Incrementally previews one frozen brush gesture using the ordinary rasterizer and original pixel coverage.</summary>
public sealed class SkinStrokePreview
{
    private readonly SkinDrawing _drawing;
    private readonly List<SkinStrokePoint> _points = [];
    private readonly SkinEdit _edit;

    /// <summary>Copies content once and freezes brush settings. This object is single-thread owned and never persists data.</summary>
    public SkinStrokePreview(SkinDocument document, SkinEdit edit)
    {
        document.Validate();
        if (edit.Tool is not (SkinTool.Stroke or SkinTool.Pencil or SkinTool.Eraser))
            throw new ArgumentException("Incremental preview requires a brush tool.", nameof(edit));
        _edit = edit with { Points = null, PixelMask = edit.PixelMask?.ToArray(), MirrorParts = edit.MirrorParts?.ToArray(), Selection = edit.Selection is { } selection ? selection with { Points = selection.Points?.ToArray(), Mask = selection.Mask?.ToArray() } : null };
        Document = document.CopyForPreview(edit.TransparencyMask);
        _drawing = new(Document, _edit, default);
    }

    /// <summary>Gets borrowed working content; only this preview may mutate its arrays.</summary>
    public SkinDocument Document { get; }
    /// <summary>Gets the sampled point count, bounded by the canonical stroke limit.</summary>
    public int Count => _points.Count;
    /// <summary>Appends one ordered hit. A failed append invalidates the gesture and must be discarded by its owner.</summary>
    public void Append(SkinStrokePoint point)
    {
        if (_points.Count == 4096)
            throw new InvalidOperationException("A stroke supports at most 4096 samples; release to finish this stroke.");
        _drawing.Append(point);
        _points.Add(point);
    }
    /// <summary>Freezes the complete gesture for one canonical commit with the begin revision.</summary>
    public SkinEdit ToEdit() => _edit with { Points = [.. _points], PixelMask = _edit.PixelMask?.ToArray(), MirrorParts = _edit.MirrorParts?.ToArray() };
}