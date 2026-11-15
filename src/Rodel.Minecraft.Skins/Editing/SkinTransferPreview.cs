// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Previews one frozen atlas transfer against its original content, without accumulated clears or history.</summary>
public sealed class SkinTransferPreview
{
    private readonly SkinDocument _baseline;
    private SkinEdit _edit;

    /// <summary>Freezes transfer arrays and the source baseline; construction validates the initial destination.</summary>
    public SkinTransferPreview(SkinDocument document, SkinEdit edit)
    {
        _baseline = document with { Pixels = [.. document.Pixels], Layers = [.. document.Layers.Select(layer => layer.CopyPixels())], Undo = [], Redo = [] };
        _edit = edit with { AtlasTransfer = edit.AtlasTransfer?.Freeze() ?? throw new ArgumentException("An atlas transfer is required.", nameof(edit)) };
        Document = _baseline;
        Update(_edit.AtlasTransfer.X, _edit.AtlasTransfer.Y);
    }

    /// <summary>Gets validated preview content; a failed update retains the last valid preview.</summary>
    public SkinDocument Document { get; private set; }
    /// <summary>Gets a separate mask of the last valid destination pixels.</summary>
    public bool[] DestinationMask { get; private set; } = [];
    /// <summary>Rebuilds from the frozen baseline. Invalid destructive destinations leave all current preview state unchanged.</summary>
    public void Update(int x, int y)
    {
        var edit = _edit with { AtlasTransfer = _edit.AtlasTransfer! with { X = x, Y = y } };
        var working = _baseline with { Pixels = [.. _baseline.Pixels], Layers = [.. _baseline.Layers.Select(layer => layer.CopyPixels())] };
        var mask = SkinAtlasTransfers.Apply(working, edit, default);
        working.Validate();
        Document = working; DestinationMask = mask; _edit = edit;
    }
    /// <summary>Returns a detached canonical request for a single durable transaction.</summary>
    public SkinEdit ToEdit() => _edit with { AtlasTransfer = _edit.AtlasTransfer!.Freeze() };
}