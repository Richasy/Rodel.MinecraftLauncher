// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Owns captured selection or paint input and one deferred paint gesture across all editor views.</summary>
internal sealed class SkinEditorInput
{
    private readonly SkinEditorController _controller;
    private readonly SkinEditorState _state;
    private object? _source, _hoverSource;
    private SkinEditingSession? _session;
    private uint _pointer;
    private PointerButton _button;
    private bool _gap;
    private SkinSurface? _lastSurface;
    private PendingStroke? _pending;
    private SkinHit? _hover;
    private string _maskKey = string.Empty;
    private SkinRenderSnapshot? _maskSnapshot, _selectionSnapshot;
    private SkinEditorSelection? _maskSelection, _selectionPrevious;
    private SkinSelectionPoint _selectionStart;
    private SkinSelectionPoint? _selectionLast;
    private SkinLayer _selectionLayer;
    private int _selectionParts;
    private bool _selectionAtlas, _moving, _placing;
    private (int X, int Y) _moveOrigin;
    private object? _cancelledSource;
    private uint _cancelledPointer;
    private Rendering.SkinBrushOverlay? _overlay;

    private sealed class PendingStroke(SkinEditingSession session, SkinEdit edit)
    {
        internal readonly SkinEditingSession Session = session;
        internal readonly SkinEdit Edit = edit with
        {
            PixelMask = edit.PixelMask?.ToArray(),
            MirrorParts = edit.MirrorParts?.ToArray(),
        };
        internal readonly List<SkinStrokePoint> Points = [];
        internal bool Ended, Ready;
    }

    internal SkinEditorInput(SkinEditorController controller, SkinEditorState state)
    {
        _controller = controller;
        _state = state;
        controller.Window.Deactivated += (_, _) =>
        {
            if (_source is { } source)
                Cancel(source);
            else
                ClearHover();
        };
        controller.CommitFinished += Promote;
        controller.DeferredRetryRequested += () =>
        {
            if (_pending is { Ready: true })
                Promote(true);
        };
        controller.DraftDiscarded += () =>
        {
            _pending = null;
            _source = null;
            controller.HasDeferredStroke = false;
            ClearHover();
        };
    }

    internal bool Owns(object source) => ReferenceEquals(source, _source);
    internal bool CanDefer => _controller.IsCommitting && _state.Tool is 0 or 1;

    internal void Hover(object source, SkinHit? hit)
    {
        if (_source is not null && !Owns(source))
            return;
        if (ReferenceEquals(_hoverSource, source) && (_hover?.Surface == hit?.Surface) && (_hover?.X == hit?.X) && (_hover?.Y == hit?.Y))
            return;
        _hoverSource = source;
        _hover = hit;
        _controller.Invalidate();
    }

    internal void ClearHover(object? source = null)
    {
        if (source is not null && !ReferenceEquals(source, _hoverSource))
            return;
        _hover = null;
        _hoverSource = null;
        _controller.Invalidate();
    }

    internal Rendering.SkinBrushOverlay Overlay(SkinRenderSnapshot snapshot)
    {
        if (_hover is { } candidate)
        {
            var face = SkinLayout.Face(snapshot.Model, candidate.Surface, snapshot.Size);
            if ((candidate.X < 0) || (candidate.Y < 0) || (candidate.X >= face.Width) || (candidate.Y >= face.Height))
                _hover = null;
        }

        var canPaint = _controller.LayerActions.CanPaint;
        var key = $"{canPaint}:{_state.Busy}:{snapshot.Revision}:{_state.ActiveLayerId}:{snapshot.Model}:{snapshot.Size}:{_state.Tool}:{_state.BrushSize}:{_state.Mirror}:{_state.Foreground}:{_state.TransparentInk}:{_state.Parts}:{_state.OuterSurface}:{_hover?.Surface}:{_hover?.X}:{_hover?.Y}";
        if (_overlay is not null && ReferenceEquals(_maskSnapshot, snapshot) && ReferenceEquals(_maskSelection, _state.Selection) && (_maskKey == key))
            return _overlay;
        _maskKey = key;
        _maskSnapshot = snapshot;
        _maskSelection = _state.Selection;
        SkinBrushMark[] marks = [];
        if (canPaint && _hover is { } hit && _state.Tool is 0 or 1 && (_state.ValidColor || _state.TransparentInk || _state.Tool == 1))
        {
            try
            {
                marks = snapshot.BrushFootprint(Edit(hit, snapshot) with
                {
                    Tool = _state.Tool == 1 ? SkinTool.Eraser : SkinTool.Pencil,
                    X = hit.X,
                    Y = hit.Y,
                    Points = null
                });
            }
            catch (ArgumentException)
            {
            }
        }

        if (_hover is { } target)
        {
            if ((_state.Tool == 2) && canPaint && _state.ValidColor && !_state.Busy && (_controller.Session?.HasDraft == false))
            {
                try
                {
                    marks = _controller.Session.FillFootprint(_controller.FillEdit(target));
                }
                catch (ArgumentException)
                {
                }
            }

            if (_state.Tool == 3)
                marks = [new(target.Surface, target.X, target.Y, 1)];
        }

        return _overlay = new(snapshot, marks, _state.Tool == 2, _state.Selection);
    }

    internal PointerEventResult Handle(
        object source,
        in PointerEvent e,
        SkinHit? hit,
        bool atlas,
        SkinSelectionPoint? selectionPoint = null)
    {
        if (ReleaseCancelled(source, e))
            return new(true, PointerCaptureRequest.Release);
        if (_controller.Session is not { } session)
            return PointerEventResult.Ignored;
        Hover(source, hit);
        if (_state.Pasting && !_state.Busy && _source is null && (e.Kind == PointerEventKind.Move) && selectionPoint is { } pastePoint)
        {
            _controller.Transfers.Place(pastePoint.X, pastePoint.Y);
        }

        if (e.Kind == PointerEventKind.Cancel)
        {
            Cancel(source);
            return new(true, PointerCaptureRequest.Release);
        }

        if (e.Kind == PointerEventKind.Down)
        {
            if ((e.Button != PointerButton.Primary) || ((e.Modifiers & KeyModifiers.Alt) != 0))
                return PointerEventResult.Ignored;
            if (_source is not null || _pending is not null)
            {
                _state.Status = Resources.SkinInputPending;
                return new(true, PointerCaptureRequest.None);
            }

            if (_state.Pasting && !_state.Busy)
            {
                if (selectionPoint is not { } pasteStart)
                    return PointerEventResult.Ignored;
                _controller.Transfers.Place(pasteStart.X, pasteStart.Y);
                _cancelledSource = null;
                _source = source;
                _pointer = e.PointerId;
                _button = e.Button;
                _state.Selecting = true;
                _placing = true;
                return new(true, PointerCaptureRequest.Capture);
            }

            var defer = _state.Busy && CanDefer;
            if (_state.Busy && !defer)
                return new(true, PointerCaptureRequest.None);
            if (_state.Tool is 0 or 1 or 2 && _state.Selection is { } selection
                && (selectionPoint is not { } point || !selection.Contains((point.Y * selection.Size) + point.X)))
            {
                ClearSelection();
                return new(true, PointerCaptureRequest.None);
            }

            if (_state.Tool == 4)
            {
                if (!_controller.Files.Available || selectionPoint is not { } start)
                    return PointerEventResult.Ignored;
                if (_state.OuterSurface && !_state.OuterVisible)
                {
                    _state.OuterVisible = true;
                    return new(true, PointerCaptureRequest.None);
                }

                _cancelledSource = null;
                _source = source;
                _session = session;
                _pointer = e.PointerId;
                _button = e.Button;
                _selectionSnapshot = session.Render();
                _selectionPrevious = _state.Selection;
                _selectionStart = start;
                _selectionLast = null;
                _selectionParts = _state.Parts;
                _selectionLayer = _state.OuterSurface ? SkinLayer.Outer : SkinLayer.Base;
                _selectionAtlas = atlas;
                _moving = _state.Selection is { Count: > 0 } selected && selected.Contains((start.Y * selected.Size) + start.X) && _controller.Transfers.CanChange;
                if (_moving)
                {
                    try
                    {
                        _moveOrigin = _selectionPrevious!.Origin;
                        _controller.Transfers.BeginMove();
                    }
                    catch (Exception error)
                    {
                        _source = null;
                        _controller.Error(error.Message);
                        return new(true, PointerCaptureRequest.None);
                    }
                }

                _state.Selecting = true;
                if (!_moving)
                    UpdateSelection(start);
                return new(true, PointerCaptureRequest.Capture);
            }

            if (hit is null)
                return PointerEventResult.Ignored;
            if (_state.OuterSurface && !_state.OuterVisible)
            {
                _state.OuterVisible = true;
                return new(true, PointerCaptureRequest.None);
            }

            if (_state.Tool == 3)
            {
                var face = SkinLayout.Face(session.Render().Model, hit.Surface, session.Render().Size);
                _state.Foreground = $"#{session.Render().Pixels.Span[((face.Y + hit.Y) * session.Render().Size) + face.X + hit.X]:X8}";
                return new(true, PointerCaptureRequest.None);
            }

            if (!_controller.LayerActions.CanPaint)
            {
                _state.Status = Resources.SkinLayerUnavailable;
                return new(true, PointerCaptureRequest.None);
            }

            var snapshot = session.Render();
            if (_state.Tool == 1 && !_state.IsCape && hit.Surface.Layer == SkinLayer.Base
                && session.ActiveLayerId == SkinDocument.BackgroundLayerId && !TargetsMask(hit, snapshot))
            {
                _state.Status = Resources.SkinCannotEraseBase;
                return new(true, PointerCaptureRequest.None);
            }

            if (_state.Tool == 2)
            {
                _ = _controller.FillAsync(hit);
                return new(true, PointerCaptureRequest.None);
            }

            if (_state.Tool is not (0 or 1))
                return PointerEventResult.Ignored;
            if (session.HasDraft && !defer)
            {
                _state.Status = Resources.SkinDraftPending;
                return new(true, PointerCaptureRequest.None);
            }

            try
            {
                var edit = Edit(hit, snapshot);
                if (defer)
                {
                    _pending = new(session, edit);
                    _controller.HasDeferredStroke = true;
                }
                else
                {
                    session.Begin(edit);
                }

                _cancelledSource = null;
                _source = source;
                _session = session;
                _pointer = e.PointerId;
                _button = e.Button;
                _gap = false;
                _lastSurface = null;
                Append(hit, atlas);
                _state.Status = defer ? Resources.SkinInputPending : Resources.SkinDrawing;
                return new(true, PointerCaptureRequest.Capture);
            }
            catch (Exception ex)
            {
                if (!defer)
                    session.Cancel();
                _source = null;
                _controller.Error(ex.Message);
            }
        }

        if (!Owns(source) || (e.PointerId != _pointer))
            return PointerEventResult.Ignored;
        if (_state.Selecting)
        {
            if ((e.Kind == PointerEventKind.Move) || ((e.Kind == PointerEventKind.Up) && (e.Button == _button)))
            {
                if (_placing)
                {
                    if (selectionPoint is { } placement)
                        _controller.Transfers.Place(placement.X, placement.Y);
                }
                else if (selectionPoint is { } endpoint)
                {
                    UpdateSelection(endpoint);
                }

                if (e.Kind == PointerEventKind.Up)
                {
                    _source = null;
                    _state.Selecting = false;
                    _selectionPrevious = null;
                    _selectionSnapshot = null;
                    if (_placing)
                    {
                        _placing = false;
                        _controller.Transfers.ConfirmPaste();
                    }
                    else if (_moving)
                    {
                        _moving = false;
                        _ = _controller.CommitAsync();
                    }

                    return new(true, PointerCaptureRequest.Release);
                }
            }

            return new(true, PointerCaptureRequest.None);
        }

        if ((e.Kind == PointerEventKind.Move) || ((e.Kind == PointerEventKind.Up) && (e.Button == _button)))
        {
            try
            {
                var count = _pending?.Points.Count ?? session.StrokeSamples;
                if ((count < 4096) && hit is not null)
                    Append(hit, atlas);
                else
                    _gap = true;
                count = _pending?.Points.Count ?? session.StrokeSamples;
                if ((count >= 4096) || (e.Kind == PointerEventKind.Up))
                {
                    _source = null;
                    if (_pending is { } pending)
                    {
                        pending.Ended = true;
                        if (count >= 4096)
                            _state.Status = Resources.SkinPendingLimit;
                    }
                    else
                    {
                        _ = FinishAsync(count >= 4096);
                    }

                    return new(true, PointerCaptureRequest.Release);
                }
            }
            catch (Exception ex)
            {
                Cancel(source);
                _controller.Error(ex.Message);
                return new(true, PointerCaptureRequest.Release);
            }

            return new(true, PointerCaptureRequest.None);
        }

        return new(true, PointerCaptureRequest.None);
    }

    internal bool HasGesture => _source is not null;

    internal bool ReleaseCancelled(object source, in PointerEvent e)
    {
        if (!ReferenceEquals(source, _cancelledSource) || (e.PointerId != _cancelledPointer) || (e.Kind != PointerEventKind.Up))
            return false;
        _cancelledSource = null;
        return true;
    }

    internal void Escape()
    {
        if (_source is { } source)
        {
            Cancel(source);
        }
        else if (_state.Pasting)
        {
            _controller.Transfers.CancelPaste();
        }
        else
        {
            ClearSelection();
        }
    }

    internal void ClearSelection()
    {
        if (_state.Selecting || _state.Busy || _controller.HasDeferredStroke || (_controller.Session?.HasDraft == true))
            return;
        _state.Selection = null;
        _controller.Invalidate();
    }

    private void UpdateSelection(SkinSelectionPoint endpoint)
    {
        if (_selectionSnapshot is null || (!_selectionAtlas && (endpoint.Surface != _selectionStart.Surface)) || (_selectionLast == endpoint))
            return;
        _selectionLast = endpoint;
        if (_moving)
        {
            _controller.Transfers.Place(_moveOrigin.X + endpoint.X - _selectionStart.X, _moveOrigin.Y + endpoint.Y - _selectionStart.Y);
            return;
        }

        _state.Selection = SkinEditorSelection.Rectangle(
            _selectionSnapshot,
            _selectionStart,
            endpoint,
            _selectionLayer,
            _selectionParts,
            _selectionAtlas);
        _controller.Invalidate();
    }

    private bool TargetsMask(SkinHit hit, SkinRenderSnapshot snapshot)
    {
        if (_state.TransparentInk)
            return true;
        if (_state.Tool != 1)
            return false;
        var face = SkinLayout.Face(snapshot.Model, hit.Surface, snapshot.Size);
        return snapshot.IsMasked(((face.Y + hit.Y) * snapshot.Size) + face.X + hit.X);
    }

    private SkinEdit Edit(SkinHit hit, SkinRenderSnapshot snapshot)
    {
        var mask = TargetsMask(hit, snapshot);
        return new()
        {
            Tool = _state.Tool == 1 ? SkinTool.Eraser : SkinTool.Stroke,
            Surface = hit.Surface,
            PaintLayerId = mask ? null : _controller.Session!.ActiveLayerId,
            TransparencyMask = mask,
            Color = mask ? null : _state.Foreground,
            PixelMask = _state.Selection?.CopyMask(),
            BrushSize = (int)_state.BrushSize,
            Mirror = _state.MirrorMode,
            MirrorParts = _state.MirrorParts,
            WrapFaces = true,
        };
    }

    private void Append(SkinHit hit, bool atlas)
    {
        var point = new SkinStrokePoint(hit.LocalX, hit.LocalY, hit.Surface, _gap || (atlas && _lastSurface is not null && (_lastSurface != hit.Surface)));
        if (_pending is { } pending)
            pending.Points.Add(point);
        else
            _session!.Append(point);
        _gap = false;
        _lastSurface = hit.Surface;
        _controller.Invalidate();
    }

    private async Task FinishAsync(bool limited)
    {
        await _controller.CommitAsync();
        if (limited && (_controller.Session?.HasDraft == false) && !_controller.HasDeferredStroke)
            _state.Status = Resources.SkinStrokeLimit;
    }

    internal void Cancel(object source)
    {
        ClearHover(source);
        if (!Owns(source))
            return;
        _cancelledSource = source;
        _cancelledPointer = _pointer;
        _source = null;
        if (_state.Selecting)
        {
            if (_placing)
            {
                _state.Selecting = false;
                _placing = false;
                _controller.Transfers.CancelPaste();
            }
            else if (_moving)
            {
                _session?.Cancel();
                _moving = false;
                _state.Selection = _selectionPrevious;
            }
            else
            {
                _state.Selection = _selectionPrevious;
            }

            _state.Selecting = false;
            _selectionPrevious = null;
            _selectionSnapshot = null;
            _controller.Invalidate();
            return;
        }

        if (_pending is { Ended: false })
        {
            _pending = null;
            _controller.HasDeferredStroke = false;
        }
        else if (!_state.Busy)
        {
            _session?.Cancel();
        }

        _controller.Invalidate();
    }

    private void Promote(bool succeeded)
    {
        if (!succeeded || _pending is not { } pending)
            return;
        pending.Ready = true;
        if (!ReferenceEquals(pending.Session, _controller.Session))
        {
            _state.Status = Resources.SkinDraftPending;
            return;
        }

        var began = false;
        try
        {
            pending.Session.Begin(pending.Edit);
            began = true;
            foreach (var point in pending.Points)
                pending.Session.Append(point);
            _pending = null;
            _controller.HasDeferredStroke = false;
            _controller.Invalidate();
            if (pending.Ended)
                _ = _controller.CommitAsync();
            else
                _state.Status = Resources.SkinDrawing;
        }
        catch (Exception ex)
        {
            if (began)
                pending.Session.Cancel();
            _controller.Error(ex.Message);
        }
    }
}