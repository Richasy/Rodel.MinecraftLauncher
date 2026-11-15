// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Coordinates native clipboard commands and shared transfer drafts without duplicating pixel transfer rules.</summary>
internal sealed class SkinEditorTransfers(SkinEditorController controller, SkinEditorState state)
{
    private SkinEditorSelection? _beforePaste;
    private string _pasteConfiguration = string.Empty;
    private string Configuration => $"{state.Tool}:{state.Parts}:{state.OuterSurface}:{state.OuterVisible}";

    internal void CheckConfiguration()
    {
        if (state.Pasting && (_pasteConfiguration != Configuration))
            CancelPaste();
    }

    internal bool CanCopy => controller.Files.Available && state.Selection is { Count: > 0 };
    internal bool NeedsFill
    {
        get
        {
            if (state.IsCape || controller.Session?.ActiveLayerId != SkinDocument.BackgroundLayerId)
                return false;
            if (state.Selection is not { Count: > 0 } selection)
                return !state.OuterSurface;
            return SkinLayout.Faces(selection.Model, selection.Size).Where(face => face.Surface.Layer == SkinLayer.Base).Any(face => Enumerable.Range(0, face.Height).Any(y => Enumerable.Range(0, face.Width).Any(x => selection.Contains(((face.Y + y) * selection.Size) + face.X + x))));
        }
    }

    internal bool ValidFill
    {
        get
        {
            try
            {
                return !NeedsFill || ((SkinColor.Parse(state.SelectionFill) & 255) == 255);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    internal bool CanChange => CanCopy && controller.LayerActions.CanPaint && ValidFill;
    internal bool CanPaste => controller.Files.Available && controller.LayerActions.CanPaint;

    internal void Copy()
    {
        if (!CanCopy)
            return;
        try
        {
            if (!SkinSelectionClipboard.Write(controller.Window.Handle, controller.Session!.CaptureAtlas(state.Selection!.CopyMask())))
            {
                controller.Error(Resources.SkinClipboardUnavailable);
            }
        }
        catch (Exception error)
        {
            controller.Error(error.Message);
        }
    }

    internal async Task DeleteAsync(bool cut = false)
    {
        if (!CanChange)
            return;
        try
        {
            var session = controller.Session!;
            if (cut && !SkinSelectionClipboard.Write(controller.Window.Handle, session.CaptureAtlas(state.Selection!.CopyMask())))
            {
                controller.Error(Resources.SkinClipboardUnavailable);
                return;
            }

            session.BeginTransfer(Edit(SkinTool.Cut));
            await controller.CommitAsync();
        }
        catch (Exception error)
        {
            controller.Error(error.Message);
        }
    }

    internal void BeginMove()
    {
        if (!CanChange)
            throw new InvalidOperationException(Resources.SkinTransferUnavailable);
        controller.Session!.BeginTransfer(Edit(SkinTool.Move));
    }

    internal void Paste()
    {
        if (!CanPaste)
            return;
        try
        {
            var copied = SkinSelectionClipboard.Read(controller.Window.Handle);
            if (copied is null)
            {
                controller.Error(Resources.SkinClipboardUnavailable);
                return;
            }

            var snapshot = controller.Session!.Render();
            var (originX, originY) = state.Selection?.Origin ?? (Math.Clamp(copied.X, 0, snapshot.Size - 1), Math.Clamp(copied.Y, 0, snapshot.Size - 1));
            var edit = new SkinEdit
            {
                Tool = SkinTool.Paste,
                PaintLayerId = controller.Session.ActiveLayerId,
                AtlasTransfer = new()
                {
                    Block = copied.Block,
                    X = originX,
                    Y = originY,
                    Layer = state.OuterSurface ? SkinLayer.Outer : SkinLayer.Base,
                    DestinationMask = Destinations(snapshot)
                },
            };
            state.Tool = 4;
            controller.Session.BeginTransfer(edit);
            _beforePaste = state.Selection;
            _pasteConfiguration = Configuration;
            state.Pasting = true;
            PublishSelection();
        }
        catch (ArgumentException)
        {
            controller.Error(Resources.SkinTransferOpaque);
        }
        catch (Exception error)
        {
            controller.Error(error.Message);
        }
    }

    internal void Place(int x, int y)
    {
        try
        {
            controller.Session!.UpdateTransfer(x, y);
            PublishSelection();
        }
        catch (ArgumentException)
        {
            state.Status = Resources.SkinTransferInvalidPosition;
        }
    }

    internal void ConfirmPaste()
    {
        if (!state.Pasting || state.Selecting || state.Busy)
            return;
        state.Pasting = false;
        _beforePaste = null;
        _ = controller.CommitAsync();
    }

    internal void CancelPaste()
    {
        if (!state.Pasting || state.Busy)
            return;
        controller.Session!.Cancel();
        state.Selection = _beforePaste;
        state.Pasting = false;
        _beforePaste = null;
        controller.Invalidate();
    }

    internal void PublishSelection()
    {
        var snapshot = controller.Session!.Render();
        state.Selection = new(snapshot.Model, snapshot.Size, controller.Session.TransferMask());
        controller.Invalidate();
    }

    private SkinEdit Edit(SkinTool tool)
    {
        var (originX, originY) = state.Selection!.Origin;
        var snapshot = controller.Session!.Render();
        return new()
        {
            Tool = tool,
            PaintLayerId = controller.Session.ActiveLayerId,
            ClearColor = NeedsFill ? state.SelectionFill : null,
            AtlasTransfer = new()
            {
                SourceMask = state.Selection.CopyMask(),
                X = originX,
                Y = originY,
                Layer = state.OuterSurface ? SkinLayer.Outer : SkinLayer.Base,
                DestinationMask = Destinations(snapshot)
            },
        };
    }

    private bool[] Destinations(SkinRenderSnapshot snapshot) => SkinEditorSelection.Rectangle(
        snapshot,
        new(0, 0),
        new(snapshot.Size - 1, snapshot.Size - 1),
        state.OuterSurface ? SkinLayer.Outer : SkinLayer.Base,
        state.Parts,
        true).CopyMask();
}