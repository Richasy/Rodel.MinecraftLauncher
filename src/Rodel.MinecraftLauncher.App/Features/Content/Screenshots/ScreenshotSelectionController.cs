// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Screenshots;
using Sprout.Controls;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Keeps native selection separate from double-click and focused keyboard activation.</summary>
internal sealed class ScreenshotSelectionController
{
    private readonly ScreenshotStore _store;
    private readonly ItemsView _list;
    private readonly Action<ScreenshotFile> _open;
    private IReadOnlyList<ScreenshotFile> _files = [];
    private int[]? _fedSelection;

    internal ScreenshotSelectionController(ScreenshotStore store, ItemsView list, Action<ScreenshotFile> open)
    {
        _store = store;
        _list = list;
        _open = open;
        _list.SelectionMode(SelectionMode.Extended)
            .OnSelectionChanged(SelectionChanged);
    }

    internal void SetFiles(IReadOnlyList<ScreenshotFile> files)
    {
        _files = files;
        _fedSelection = null;
    }

    internal IDisposable BindKeyboard(CommandManager commands, Func<InteractionNode?> focused)
    {
        var root = _list.Interaction ?? throw new InvalidOperationException("Mount the screenshot list before binding keyboard commands.");
        root.KeyContext = (root.KeyContext ?? KeyContext.Empty).With("rodel-gallery", "screenshots");
        return commands.Bind(new KeyChord(Key.Enter), new Command("screenshot.open", () =>
        {
            if (FocusedFile(focused()) is { } file) _open(file);
        }, () => FocusedFile(focused()) is not null),
            KeyScope.WhenEquals("rodel-gallery", "screenshots").InertWhenTyping().InertWhenPopup());
    }

    private ScreenshotFile? FocusedFile(InteractionNode? focused)
    {
        ItemContainerInteraction? item = null;
        for (var node = focused; node is not null; node = node.Parent)
        {
            if (node is ItemContainerInteraction row) item = row;
            if (ReferenceEquals(node, _list.Interaction))
            {
                return item is { CanTakeFocus: true } && item.Index >= 0 && item.Index < _files.Count
                    && _store.Files.Contains(_files[item.Index]) ? _files[item.Index] : null;
            }
        }

        return null;
    }

    internal void Synchronize()
    {
        var selected = _store.Selected;
        var indices = _files.Select((file, index) => (file, index))
            .Where(item => selected.Contains(item.file))
            .Select(item => item.index)
            .ToArray();
        if (_fedSelection is not null && _fedSelection.SequenceEqual(indices)) return;
        _fedSelection = indices;
        _list.SelectedIndices(indices);
    }

    private void SelectionChanged(IReadOnlyList<int> indices)
    {
        _fedSelection = [.. indices];
        var kind = _list.SelectionChangeKind;
        if (kind != ItemsViewSelectionChangeKind.Programmatic) _store.BeginSelection();
        _store.SetSelection(indices.Where(index => index >= 0 && index < _files.Count).Select(index => _files[index]));
    }

    internal PointerEventResult HandlePointer(ScreenshotFile file, in PointerEvent packet)
    {
        if (packet.Kind != PointerEventKind.Down || packet.Button != PointerButton.Primary || packet.ClickCount != 2
            || (packet.Modifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) != 0
            || !_store.Files.Contains(file))
        {
            return PointerEventResult.Ignored;
        }

        _open(file);
        return new(true, PointerCaptureRequest.None);
    }
}