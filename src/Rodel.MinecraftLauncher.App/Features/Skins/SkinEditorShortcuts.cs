// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Registers editor commands for this window while leaving text input, IME and popup keys to their owners.</summary>
internal static class SkinEditorShortcuts
{
    internal static IDisposable Bind(SkinEditorState editor)
    {
        var controller = editor.Controller;
        var scope = controller.Window.Commands.PushScope();
        var gate = KeyScope.Global.InertWhenTyping().InertWhenPopup();
        var canvasGate = KeyScope.When("skin-canvas").InertWhenTyping().InertWhenPopup();
        bool Available() => !ModalPopup.IsOpen(controller.Window) && controller.Files.Available;
        bool Editable() => Available() && editor.HasProject;
        void Bind(
            Key key,
            KeyModifiers modifiers,
            string id,
            Action action,
            Func<bool> canExecute,
            KeyScope? inputScope = null) => scope.Bind(new KeyChord(key, modifiers), new Command(id, action, canExecute), inputScope ?? gate);
        Bind(
            Key.N,
            KeyModifiers.Control,
            "skin.new",
            () => controller.ShowNew(),
            Available);
        Bind(
            Key.O,
            KeyModifiers.Control,
            "skin.open",
            controller.ShowProjects,
            Available);
        Bind(
            Key.S,
            KeyModifiers.Control | KeyModifiers.Shift,
            "skin.save-as",
            controller.Files.ShowSaveAs,
            Editable);
        Bind(
            Key.I,
            KeyModifiers.Control | KeyModifiers.Shift,
            "skin.import",
            controller.Files.ShowImport,
            Available);
        Bind(
            Key.E,
            KeyModifiers.Control | KeyModifiers.Shift,
            "skin.export",
            () => _ = controller.Files.ExportAsync(),
            Editable);
        Bind(
            Key.Z,
            KeyModifiers.Control,
            "skin.undo",
            () => _ = controller.RestoreAsync(false),
            () => Editable() && editor.CanUndo);
        Bind(
            Key.Y,
            KeyModifiers.Control,
            "skin.redo",
            () => _ = controller.RestoreAsync(true),
            () => Editable() && editor.CanRedo);
        Bind(
            Key.Z,
            KeyModifiers.Control | KeyModifiers.Shift,
            "skin.redo.shift",
            () => _ = controller.RestoreAsync(true),
            () => Editable() && editor.CanRedo);
        Bind(
            Key.S,
            KeyModifiers.Control,
            "skin.save",
            () => _ = controller.RetryAsync(),
            () => editor.HasProject && !editor.Selecting && !editor.Pasting && !editor.Busy && !editor.FileActive && !editor.LayerDragging && !ModalPopup.IsOpen(controller.Window));
        Bind(
            Key.B,
            KeyModifiers.None,
            "skin.brush",
            () => editor.Tool = 0,
            Editable);
        Bind(
            Key.E,
            KeyModifiers.None,
            "skin.eraser",
            () => editor.Tool = 1,
            Editable);
        Bind(
            Key.G,
            KeyModifiers.None,
            "skin.fill",
            () => editor.Tool = 2,
            Editable);
        Bind(
            Key.I,
            KeyModifiers.None,
            "skin.picker",
            () => editor.Tool = 3,
            Editable);
        Bind(
            Key.C,
            KeyModifiers.Control,
            "skin.selection.copy",
            controller.Transfers.Copy,
            () => !ModalPopup.IsOpen(controller.Window) && controller.Transfers.CanCopy,
            canvasGate);
        Bind(
            Key.X,
            KeyModifiers.Control,
            "skin.selection.cut",
            () => _ = controller.Transfers.DeleteAsync(true),
            () => !ModalPopup.IsOpen(controller.Window) && controller.Transfers.CanChange,
            canvasGate);
        Bind(
            Key.V,
            KeyModifiers.Control,
            "skin.selection.paste",
            controller.Transfers.Paste,
            () => !ModalPopup.IsOpen(controller.Window) && controller.Transfers.CanPaste,
            canvasGate);
        Bind(
            Key.Delete,
            KeyModifiers.None,
            "skin.selection.delete",
            () => _ = controller.Transfers.DeleteAsync(),
            () => !ModalPopup.IsOpen(controller.Window) && controller.Transfers.CanChange,
            canvasGate);
        Bind(
            Key.Enter,
            KeyModifiers.None,
            "skin.selection.place",
            controller.Transfers.ConfirmPaste,
            () => editor.Pasting && !editor.Selecting && !editor.Busy && !ModalPopup.IsOpen(controller.Window),
            canvasGate);
        Bind(
            Key.R,
            KeyModifiers.None,
            "skin.selection",
            () => editor.Tool = 4,
            Editable);
        Bind(
            Key.D,
            KeyModifiers.Control,
            "skin.selection.clear",
            controller.Input.ClearSelection,
            Editable,
            canvasGate);
        Bind(
            Key.Escape,
            KeyModifiers.None,
            "skin.selection.escape",
            controller.Input.Escape,
            () => !ModalPopup.IsOpen(controller.Window) && !editor.Busy && !editor.FileActive && !editor.HasLayerDraft && !editor.LayerDragging && (controller.Input.HasGesture || editor.Pasting || editor.Selection is not null));
        Bind(
            Key.M,
            KeyModifiers.None,
            "skin.mirror",
            () => editor.Mirror = !editor.Mirror,
            Editable);
        Bind(
            Key.Digit1,
            KeyModifiers.None,
            "skin.view.model",
            () => editor.FlatView = false,
            Editable);
        Bind(
            Key.Digit2,
            KeyModifiers.None,
            "skin.view.flat",
            () => editor.FlatView = true,
            Editable);
        Bind(
            Key.BracketLeft,
            KeyModifiers.None,
            "skin.brush.smaller",
            () => editor.BrushSize = Math.Max(1, editor.BrushSize - 1),
            Editable);
        Bind(
            Key.BracketRight,
            KeyModifiers.None,
            "skin.brush.larger",
            () => editor.BrushSize = Math.Min(5, editor.BrushSize + 1),
            Editable);
        return scope;
    }
}