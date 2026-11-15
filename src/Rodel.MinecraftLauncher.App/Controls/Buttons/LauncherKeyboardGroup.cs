// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Reconcile;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Marks related launcher commands as one Tab stop with framework-owned directional navigation.</summary>
[InteractiveDecorator]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Sprout calls decorator projection hooks on the view instance.")]
public readonly partial record struct LauncherKeyboardGroup : IView
{
    /// <summary>Gets the projected content without changing its appearance or accessibility roles.</summary>
    public Widget? Content { get; init; }

    /// <summary>Preserves the content's natural measurement and arrangement.</summary>
    public OverlayPanel BuildChrome(Widget content)
    {
        var panel = new OverlayPanel();
        panel.Add(content);
        return panel;
    }

    /// <summary>Uses the shared focus traversal and input-mode rules rather than duplicating key handling in pages.</summary>
    public KeyboardNavigationInteraction BuildInteractionNode(ElementId id, Rect bounds, Widget? _) => new(id, bounds);

    /// <summary>Keeps the immutable grouping policy on the retained interaction node during reconciliation.</summary>
    public void SyncInteraction(InteractionNode _)
    {
    }
}