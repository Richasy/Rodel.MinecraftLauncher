// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Applies atomic pixel and paint-layer transactions independently of input devices and UI hosts.</summary>
public static class SkinEditor
{
    /// <summary>Creates a character with an opaque Base and transparent Outer, or a rectangular cape that permits alpha, with no additional paint layers.</summary>
    public static SkinDocument Create(SkinModel model, string color = "#808080", int size = 64) =>
        SkinDocument.Create(model, SkinColor.Parse(color), size);

    /// <summary>Applies 1-512 drawing/layer actions to a private snapshot and commits them as one undo step.</summary>
    /// <param name="document">The caller-owned source document, never modified by this operation.</param>
    /// <param name="edits">The drawing and layer actions belonging to one transaction.</param>
    /// <param name="expectedRevision">The last observed revision; stale actions are rejected.</param>
    /// <param name="cancellationToken">Cancels input enumeration and raster work without returning partial state.</param>
    /// <returns>The committed snapshot, or the unchanged input when no content changed.</returns>
    public static SkinDocument Apply(SkinDocument document, IReadOnlyList<SkinEdit> edits, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var working = Preview(document, edits, expectedRevision, cancellationToken);
        return Commit(document, working, new(string.Join("+", edits.Select(edit => edit.Tool).Distinct())), cancellationToken);
    }

    /// <summary>Renders a transaction on a private content copy without changing revision or history; discard it to cancel.</summary>
    public static SkinDocument Preview(SkinDocument document, IReadOnlyList<SkinEdit> edits, long expectedRevision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckRevision(document, expectedRevision);
        if (edits.Count is < 1 or > 512)
            throw new ArgumentException("A drawing transaction requires 1 through 512 actions.", nameof(edits));
        var working = document.CopyForPreview(edits.Any(edit => edit?.TransparencyMask == true));
        foreach (var edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.TransparencyMask && (edit.AtlasTransfer is not null || edit.Tool == SkinTool.Layer))
                throw new ArgumentException("Transparency masks require a brush or eraser.", nameof(edits));
            if (edit.Tool == SkinTool.Layer)
            {
                var change = edit.LayerChange ?? throw new ArgumentException("Layer actions require layerChange.", nameof(edits));
                SkinLayers.Apply(working, change);
                if (change.Id == SkinDocument.BackgroundLayerId)
                    working = working with { BackgroundName = change.Name! };
            }
            else if (edit.AtlasTransfer is not null)
            {
                SkinAtlasTransfers.Apply(working, edit, cancellationToken);
            }
            else
            {
                new SkinDrawing(working, edit, cancellationToken).Draw();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        working.Validate();
        return working with { TransparencyMask = SkinTransparency.NonEmpty(working.TransparencyMask), Undo = document.Undo, Redo = document.Redo };
    }

    /// <summary>Captures selected atlas pixels from one actual paint layer, preserving transparent included pixels and unselected holes.</summary>
    public static SkinAtlasBlock CaptureAtlas(SkinDocument document, bool[] mask, string? paintLayerId = null)
    {
        document.Validate();
        return SkinAtlasTransfers.Capture(document, mask, paintLayerId);
    }

    /// <summary>Replaces the complete model, texture and layer content as one undoable transaction; source histories are not imported.</summary>
    public static SkinDocument ReplaceContent(SkinDocument document, SkinDocument content, long expectedRevision,
        SkinTransactionInfo? transaction = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckRevision(document, expectedRevision);
        content.Validate();
        var state = content.CaptureState();
        var working = document with
        {
            Model = state.Model,
            Size = state.Size,
            BackgroundName = state.BackgroundName,
            Pixels = state.Pixels,
            Layers = [.. state.Layers],
            TransparencyMask = state.TransparencyMask,
        };
        return Commit(document, working, transaction ?? new("import"), cancellationToken);
    }

    /// <summary>Returns affected texels without changing pixels, using the actual brush or fill algorithm, layer sampling, clipping and symmetry.</summary>
    public static SkinBrushMark[] BrushFootprint(SkinDocument document, SkinEdit edit, CancellationToken cancellationToken = default)
    {
        document.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return new SkinDrawing(document, edit, cancellationToken).Footprint();
    }

    private static SkinDocument Commit(SkinDocument document, SkinDocument working, SkinTransactionInfo transaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document.HasSameContent(working))
            return document;
        return working with
        {
            Revision = checked(document.Revision + 1),
            Undo = [.. document.Undo.TakeLast(31), document.CaptureState()],
            Redo = [],
            Transaction = transaction
        };
    }

    /// <summary>Restores pixels and complete layer settings while advancing, never rewinding, the concurrency revision.</summary>
    public static SkinDocument Restore(SkinDocument document, long expectedRevision, bool redo = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckRevision(document, expectedRevision);
        if ((redo ? document.Redo : document.Undo).Count == 0)
            throw new InvalidOperationException(redo ? "There is no transaction to redo." : "There is no transaction to undo.");
        return RestoreTo(document, document.Undo.Count + (redo ? 1 : -1), expectedRevision, cancellationToken);
    }

    /// <summary>Moves to an indexed retained history state atomically, preserving future states and advancing the concurrency revision once.</summary>
    public static SkinDocument RestoreTo(SkinDocument document, int index, long expectedRevision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckRevision(document, expectedRevision);
        var states = document.Undo.Append(document.CaptureState()).Concat(document.Redo.AsEnumerable().Reverse()).ToArray();
        if (index < 0 || index >= states.Length)
            throw new ArgumentOutOfRangeException(nameof(index), "History position is outside the retained timeline.");
        if (index == document.Undo.Count)
            return document;
        var snapshot = states[index];
        return document with
        {
            Model = snapshot.Model,
            Size = snapshot.Size,
            BackgroundName = snapshot.BackgroundName,
            Pixels = (uint[])snapshot.Pixels.Clone(),
            Layers = [.. snapshot.Layers.Select(layer => layer.CopyPixels())],
            TransparencyMask = SkinTransparency.Copy(snapshot.TransparencyMask, snapshot.Pixels.Length),
            Transaction = snapshot.Transaction ?? new("restore", "system"),
            Revision = checked(document.Revision + 1),
            Undo = [.. states.Take(index).TakeLast(32)],
            Redo = [.. states.Skip(index + 1).Reverse().TakeLast(32)]
        };
    }

    /// <summary>Describes the retained timeline, whose indices must be paired with the observed document revision.</summary>
    public static SkinHistoryEntry[] History(SkinDocument document)
    {
        document.Validate();
        return [.. document.Undo.Append(document.CaptureState()).Concat(document.Redo.AsEnumerable().Reverse())
            .Select((state, index) => new SkinHistoryEntry(index, index == document.Undo.Count,
                state.Model, state.Size, state.Transaction ?? new("create", "system")))];
    }

    /// <summary>Samples the visible composite, or an explicitly addressed background/paint layer, as exact RGBA.</summary>
    public static string Sample(SkinDocument document, SkinSurface surface, int x, int y, string? paintLayerId = null)
    {
        var face = SkinLayout.Face(document.Model, surface, document.Size);
        SkinSelections.CheckPoint(face, x, y);
        var pixels = SkinCompositor.Pixels(document, paintLayerId);
        return $"#{pixels[((face.Y + y) * document.Size) + face.X + x]:X8}";
    }

    /// <summary>Captures selected pixels and their footprint for cross-face or cross-document clipboard operations.</summary>
    public static SkinPixelBlock Capture(SkinDocument document, SkinSurface surface, SkinSelection? selection = null, string? paintLayerId = null) =>
        SkinTransfers.Capture(document, surface, selection, paintLayerId);

    /// <summary>Mirrors a limb's visible composite into the opposite limb on one paint layer as a single undoable replacement.</summary>
    /// <param name="document">The source document, which remains unchanged.</param>
    /// <param name="part">The single arm or leg to copy.</param>
    /// <param name="expectedRevision">The observed document revision; stale copies are rejected.</param>
    /// <param name="includeOuter">Whether to copy the outer shell in addition to the base surface.</param>
    /// <param name="paintLayerId">The destination paint layer, or null for the protected background.</param>
    /// <param name="cancellationToken">Cancels without returning partially copied faces.</param>
    /// <returns>The committed document, preserving all other paint layers and the source limb.</returns>
    public static SkinDocument CopyPartToOpposite(SkinDocument document, SkinPart part, long expectedRevision,
        bool includeOuter = true, string? paintLayerId = null, CancellationToken cancellationToken = default)
    {
        if (document.Model == SkinModel.Cape || part is not (SkinPart.RightArm or SkinPart.LeftArm or SkinPart.RightLeg or SkinPart.LeftLeg))
            throw new ArgumentException("Only a character arm or leg can be copied to its opposite.", nameof(part));
        var edits = SkinLayout.Faces(document.Model, document.Size)
            .Where(face => face.Surface.Part == part && (includeOuter || face.Surface.Layer == SkinLayer.Base))
            .Select(face => new SkinEdit
            {
                Tool = SkinTool.Copy,
                Source = face.Surface,
                Surface = SkinGeometry.Mirror(face.Surface, SkinMirrorMode.Body),
                PaintLayerId = paintLayerId,
                SampleMerged = true,
                FlipX = true,
                BlendMode = SkinBlendMode.Replace,
            }).ToArray();
        return Apply(document, edits, expectedRevision, cancellationToken);
    }

    private static void CheckRevision(SkinDocument document, long revision)
    {
        document.Validate();
        if (document.Revision != revision)
            throw new InvalidOperationException($"Skin revision conflict: expected {revision}, actual {document.Revision}. Read the document before retrying.");
    }
}