// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Input;

namespace Rodel.MinecraftLauncher.App.Controls.Reports;

/// <summary>Moves focus after a popup's replacement view has completed layout.</summary>
internal sealed class PopupFocusRecovery : IDisposable
{
    private readonly Window _window;
    private Control? _pending;
    private InteractionNode? _preferred;

    internal PopupFocusRecovery(Window window)
    {
        _window = window;
        window.LayoutUpdated += OnLayoutUpdated;
    }

    internal void Request(Control content, InteractionNode? preferred = null)
    {
        _pending = content;
        _preferred = preferred;
    }

    internal void RequestIfLost(Control content)
    {
        if (_window.FocusedElement is null || !_window.FocusedElement.CanTakeFocus)
            Request(content);
    }

    private void OnLayoutUpdated()
    {
        if (_pending?.Interaction is not { IsCollapsed: false } root)
            return;
        var target = FindTarget(root, _preferred);
        if (target is null)
            return;
        _ = _window.SetFocus(target, FocusState.Programmatic);
        if (ReferenceEquals(_window.FocusedElement, target))
        {
            _pending = null;
            _preferred = null;
        }
    }

    internal static InteractionNode? FindTarget(InteractionNode root, InteractionNode? preferred)
    {
        var target = IsLiveDescendant(preferred, root) ? preferred : FocusTraversal.Next(root, current: null, forward: true);
        // Traversal starts at the replacement view, but its popup or another ancestor
        // can already be collapsed during close. Check the complete focus contract.
        return target?.CanTakeFocus == true ? target : null;
    }

    private static bool IsLiveDescendant(InteractionNode? node, InteractionNode root)
    {
        if (node?.CanTakeFocus != true)
            return false;
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _window.LayoutUpdated -= OnLayoutUpdated;
        _pending = null;
        _preferred = null;
    }
}