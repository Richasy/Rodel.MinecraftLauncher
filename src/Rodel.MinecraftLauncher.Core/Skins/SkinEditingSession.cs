// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Owns a native editor's in-memory content and one transient gesture; disk access occurs only at explicit project boundaries.</summary>
public sealed class SkinEditingSession
{
    private readonly SkinService _service;
    private SkinDocument _document;
    private SkinStrokePreview? _stroke;
    private SkinTransferPreview? _transfer;
    private SkinRenderSnapshot? _render;
    private readonly Lock _gate = new();
    private bool _saving;

    internal SkinEditingSession(SkinService service, string id, SkinDocument document)
    {
        _service = service;
        Id = id;
        _document = document;
    }

    /// <summary>Gets the durable project identity.</summary>
    public string Id { get; }
    /// <summary>Gets the durable revision; previews never advance it.</summary>
    public long Revision { get { lock (_gate) return _document.Revision; } }
    /// <summary>Gets whether the current gesture has uncommitted content.</summary>
    public bool HasStroke { get { lock (_gate) return _stroke is not null; } }
    /// <summary>Gets whether either a brush or atlas-transfer draft awaits commit or cancellation.</summary>
    public bool HasDraft { get { lock (_gate) return _stroke is not null || _transfer is not null; } }
    /// <summary>Gets the number of ordered samples retained in the current gesture.</summary>
    public int StrokeSamples { get { lock (_gate) return _stroke?.Count ?? 0; } }
    /// <summary>Gets whether durable undo is available.</summary>
    public bool CanUndo => _document.Undo.Count > 0;
    /// <summary>Gets whether durable redo is available.</summary>
    public bool CanRedo => _document.Redo.Count > 0;
    /// <summary>Gets the selected layer, preserving hidden or locked selections rather than redirecting drawing to another layer.</summary>
    public string? ActiveLayerId
    {
        get { lock (_gate) return field ?? _document.Layers.LastOrDefault()?.Id ?? SkinDocument.BackgroundLayerId; }

        private set;
    }
    /// <summary>Gets whether the selected layer is visible, unlocked and contributes to the composite.</summary>
    public bool CanPaint { get { lock (_gate) return IsPaintable(ActiveLayerId); } }
    /// <summary>Selects an existing layer without changing pixels, revision or history; an unpaintable selection remains selected.</summary>
    public void SelectLayer(string id)
    {
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            if (!Layers.Any(layer => layer.Id == id)) throw new ArgumentException("Layer not found.", nameof(id));
            ActiveLayerId = id;
        }
    }
    /// <summary>Copies the addressed layer's current pixels for a thumbnail without reading disk or including undo history.</summary>
    public uint[] LayerPixels(string id)
    {
        lock (_gate)
        {
            var document = _transfer?.Document ?? _stroke?.Document ?? _document;
            var pixels = id == SkinDocument.BackgroundLayerId ? document.Pixels
                : document.Layers.FirstOrDefault(layer => layer.Id == id)?.Pixels ?? throw new ArgumentException("Layer not found.", nameof(id));
            return (uint[])pixels.Clone();
        }
    }
    /// <summary>Gets current layer metadata for the editor inspector.</summary>
    public SkinLayerInfo[] Layers => SkinLayers.Describe(_document);
    /// <summary>Gets a cached, independently owned composite; publication is synchronized with draft changes and durable completion.</summary>
    public SkinRenderSnapshot Render() { lock (_gate) return _render ??= new(_transfer?.Document ?? _stroke?.Document ?? _document with { Undo = [], Redo = [] }); }
    /// <summary>Returns brush geometry from the current immutable display snapshot without reading disk or validating undo history.</summary>
    public SkinBrushMark[] BrushFootprint(SkinEdit edit) => Render().BrushFootprint(edit);
    /// <summary>Calculates a fill preview against real layer pixels without changing the document, history or durable file.</summary>
    public SkinBrushMark[] FillFootprint(SkinEdit edit)
    {
        if (edit.Tool != SkinTool.Fill) throw new ArgumentException("A fill edit is required.", nameof(edit));
        lock (_gate) return SkinEditor.BrushFootprint(_document with { Undo = [], Redo = [] }, edit);
    }
    /// <summary>Begins a gesture with frozen raster settings; active drafts must be committed or cancelled first.</summary>
    public void Begin(SkinEdit edit)
    {
        lock (_gate)
        {
            if (_saving) throw new InvalidOperationException("Saving is in progress.");
            if (HasDraft) throw new InvalidOperationException("Finish or cancel the active stroke first.");
            RequirePaintable(edit);
            _stroke = new(_document with { Undo = [], Redo = [] }, edit);
        }
    }
    /// <summary>Appends one ordered sample without reading disk or replaying prior samples.</summary>
    public void Append(SkinStrokePoint point)
    {
        lock (_gate)
        {
            if (_saving) throw new InvalidOperationException("Saving is in progress.");
            (_stroke ?? throw new InvalidOperationException("No active stroke.")).Append(point);
            _render = null;
        }
    }
    /// <summary>Captures selected raw pixels on the active layer without reading disk or modifying history.</summary>
    public SkinAtlasBlock CaptureAtlas(bool[] mask)
    {
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            return SkinEditor.CaptureAtlas(_document with { Undo = [], Redo = [] }, mask, ActiveLayerId);
        }
    }
    /// <summary>Begins a validated, frozen transfer draft shared by all render consumers.</summary>
    public void BeginTransfer(SkinEdit edit)
    {
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            RequirePaintable(edit);
            _transfer = new(_document, edit); _render = null;
        }
    }
    /// <summary>Updates an atlas destination from the original baseline; invalid positions retain the last valid draft.</summary>
    public void UpdateTransfer(int x, int y)
    {
        lock (_gate)
        {
            if (_saving) throw new InvalidOperationException("Saving is in progress.");
            (_transfer ?? throw new InvalidOperationException("No active transfer.")).Update(x, y); _render = null;
        }
    }
    /// <summary>Copies the current transfer's exact destination footprint for all editor views.</summary>
    public bool[] TransferMask() { lock (_gate) return _transfer?.DestinationMask.ToArray() ?? throw new InvalidOperationException("No active transfer."); }

    /// <summary>Discards only the transient gesture and restores the last durable display.</summary>
    public void Cancel() { lock (_gate) { if (_saving) throw new InvalidOperationException("Saving is in progress."); _stroke = null; _transfer = null; _render = null; } }
    /// <summary>Applies one non-gesture edit against the current durable revision.</summary>
    public async Task ApplyAsync(SkinEdit edit, CancellationToken cancellationToken = default)
    {
        edit = edit with { PixelMask = edit.PixelMask?.ToArray(), MirrorParts = edit.MirrorParts?.ToArray(), AtlasTransfer = edit.AtlasTransfer is { } transfer ? transfer with { SourceMask = transfer.SourceMask?.ToArray(), DestinationMask = transfer.DestinationMask?.ToArray(), Block = transfer.Block is { } block ? block with { Pixels = [.. block.Pixels], Mask = block.Mask?.ToArray() } : null } : null };
        long revision;
        lock (_gate) { if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first."); RequirePaintable(edit); _saving = true; revision = _document.Revision; }

        try { var document = await _service.EditAsync(Id, revision, [edit], cancellationToken).ConfigureAwait(false); lock (_gate) Publish(document, edit.LayerChange); }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>Copies the source limb's visible composite into the opposite limb on the selected layer in one durable undo step.</summary>
    /// <param name="part">The single source arm or leg.</param>
    /// <param name="includeOuter">Whether to include the visible outer shell along with the base.</param>
    /// <param name="cancellationToken">Cancels publication without changing this session.</param>
    public async Task CopyPartToOppositeAsync(SkinPart part, bool includeOuter = true, CancellationToken cancellationToken = default)
    {
        long revision;
        string? layerId;
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            layerId = ActiveLayerId;
            RequirePaintable(new() { Tool = SkinTool.Copy, PaintLayerId = layerId });
            _saving = true;
            revision = _document.Revision;
        }

        try
        {
            var document = await _service.CopyPartToOppositeAsync(Id, revision, part, includeOuter, layerId, cancellationToken).ConfigureAwait(false);
            lock (_gate) Publish(document);
        }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>Replaces model, size and all layers from frozen PNG bytes as one durable history step; failure leaves this session unchanged.</summary>
    public async Task ImportAsync(SkinPngImport input, SkinModel model, CancellationToken cancellationToken = default)
    {
        long revision;
        lock (_gate) { if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first."); _saving = true; revision = _document.Revision; }

        try
        {
            var content = input.Decode(model);
            var document = await _service.ImportContentAsync(Id, revision, content, cancellationToken).ConfigureAwait(false);
            lock (_gate) Publish(document);
        }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>Adds and selects a topmost layer from frozen PNG bytes as one undo step; failure preserves content and the current selection.</summary>
    public async Task ImportLayerAsync(SkinPngImport input, string name, CancellationToken cancellationToken = default)
    {
        long revision;
        lock (_gate) { if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first."); _saving = true; revision = _document.Revision; }

        try
        {
            var document = await _service.ImportLayerAsync(Id, revision, input, name, cancellationToken).ConfigureAwait(false);
            lock (_gate) Publish(document, new() { Operation = SkinLayerOperation.Add, Id = document.Layers[^1].Id });
        }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>Exports the specified committed layer's unmodified RGBA at its original size; hidden and locked layers remain exportable.</summary>
    public Task ExportLayerAsync(string layerId, string outputPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            bytes = SkinPng.EncodeLayer(_document with { Undo = [], Redo = [] }, layerId);
        }

        return SkinService.PublishAsync(outputPath, bytes, overwrite, cancellationToken);
    }

    /// <summary>Exports this session's committed composite at its actual size, independently of external project writes; history is unchanged.</summary>
    public Task ExportAsync(string outputPath, bool overwrite = false, CancellationToken cancellationToken = default)
        => ExportAsync(outputPath, null, overwrite, cancellationToken);

    /// <summary>Publishes a committed composite at its original size or as a nearest-neighbor HD reduction to width 64 with model-specific height; project and history remain unchanged.</summary>
    /// <param name="outputPath">The explicit PNG destination.</param>
    /// <param name="size">The original texture dimension, 64 for HD reduction, or null to retain the committed size.</param>
    /// <param name="overwrite">Whether replacement of the destination was confirmed.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>The atomic file publication operation.</returns>
    public Task ExportAsync(string outputPath, int? size, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            bytes = SkinPng.Encode(_document with { Undo = [], Redo = [] }, size ?? _document.Size);
        }

        return SkinService.PublishAsync(outputPath, bytes, overwrite, cancellationToken);
    }

    /// <summary>Saves this editor's observed committed project and full retained history to an independent file; no draft or source is changed.</summary>
    /// <param name="outputPath">The explicitly chosen .rmcs destination.</param>
    /// <param name="overwrite">Whether replacement of that exact destination was confirmed.</param>
    /// <param name="cancellationToken">Cancels the snapshot publication.</param>
    public async Task SaveProjectAsAsync(string outputPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        long revision;
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            _saving = true; revision = _document.Revision;
        }

        try { await _service.SaveProjectAsAsync(Id, revision, outputPath, overwrite, cancellationToken).ConfigureAwait(false); }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>Commits one canonical gesture under the service's optimistic lease. Failure retains the draft for retry or discard.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        SkinEdit edit; long revision;
        lock (_gate) { if (_saving) throw new InvalidOperationException("Saving is in progress."); edit = _transfer?.ToEdit() ?? _stroke?.ToEdit() ?? throw new InvalidOperationException("No active draft."); revision = _document.Revision; _saving = true; }

        try
        {
            var document = await _service.EditAsync(Id, revision, [edit], cancellationToken).ConfigureAwait(false);
            lock (_gate) { _document = document; _stroke = null; _transfer = null; _render = null; }
        }
        finally { lock (_gate) _saving = false; }
    }
    /// <summary>Restores one durable transaction; a pending draft cannot be silently overwritten.</summary>
    public async Task RestoreAsync(bool redo = false, CancellationToken cancellationToken = default)
    {
        long revision;
        lock (_gate) { if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first."); revision = _document.Revision; _saving = true; }

        try { var document = await _service.RestoreAsync(Id, revision, redo, cancellationToken).ConfigureAwait(false); lock (_gate) Publish(document); }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>Copies retained history metadata and its matching revision from this session without rereading disk.</summary>
    public (long Revision, SkinHistoryEntry[] Entries) History()
    {
        lock (_gate) return (_document.Revision, SkinEditor.History(_document));
    }

    /// <summary>Restores a displayed timeline position at its observed revision; failures preserve the current session.</summary>
    /// <param name="index">The retained timeline position, rather than a revision number.</param>
    /// <param name="expectedRevision">The revision observed together with the displayed history.</param>
    /// <param name="cancellationToken">Cancels the durable restore operation.</param>
    public async Task RestoreToAsync(int index, long expectedRevision, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_saving || HasDraft) throw new InvalidOperationException("Finish the active operation first.");
            if (_document.Revision != expectedRevision) throw new InvalidOperationException("History has changed. Refresh before restoring.");
            _saving = true;
        }

        try
        {
            var document = await _service.RestoreToAsync(Id, expectedRevision, index, cancellationToken).ConfigureAwait(false);
            lock (_gate) Publish(document);
        }
        finally { lock (_gate) _saving = false; }
    }

    private bool IsPaintable(string? id) => id is null or SkinDocument.BackgroundLayerId
        || _document.Layers.Any(layer => layer.Id == id && layer.Visible && !layer.Locked && layer.Opacity > 0);

    private void RequirePaintable(SkinEdit edit)
    {
        if (edit.Tool != SkinTool.Layer && !IsPaintable(edit.PaintLayerId))
            throw new InvalidOperationException("The selected layer must be visible, unlocked and have nonzero opacity before drawing.");
    }

    private void Publish(SkinDocument document, SkinLayerChange? change = null)
    {
        var selected = ActiveLayerId;
        var oldIndex = _document.Layers.FindIndex(layer => layer.Id == selected);
        _document = document;
        if (change?.Operation is SkinLayerOperation.Add or SkinLayerOperation.Duplicate)
        {
            ActiveLayerId = change.Id;
        }
        else if (selected == SkinDocument.BackgroundLayerId || document.Layers.Any(layer => layer.Id == selected))
        {
            ActiveLayerId = selected;
        }
        else
        {
            ActiveLayerId = oldIndex > 0 && document.Layers.Count > 0 ? document.Layers[Math.Min(oldIndex - 1, document.Layers.Count - 1)].Id
                : document.Layers.FirstOrDefault()?.Id ?? SkinDocument.BackgroundLayerId;
        }

        _render = null;
    }
}