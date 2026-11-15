// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Semantics;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Mounts a bounded fixed settings form through Sprout's retained child slots, without virtualization or another scroller.</summary>
internal sealed class SettingsRows(Control[] rows, float spacing = 10f) : Control
{
    private FlexPanel _panel = null!;

    /// <inheritdoc/>
    protected override void OnMount()
    {
        _panel = new FlexPanel(Orientation.Vertical, spacing: spacing, mainAlignment: MainAxisAlignment.Start);
        var interaction = new InteractionNode(RootId, default, HitTestBehavior.DeferToChild);
        var semantics = new SemanticsNode(RootId, SemanticsRole.Group, name: null, default);
        var specs = rows.Select((row, index) => new ControlChildSpec<Control>(() => row, _ =>
                {
                }, index)).ToArray();
        var slots = ReconcileChildren(specs, interaction, semantics);
        foreach (var slot in slots)
            _panel.Add(slot.Render);
        SetProjection(_panel, interaction, semantics);
    }

    /// <inheritdoc/>
    protected override void OnSyncGeometry()
    {
        SyncProjectionBounds(_panel.Bounds);
        SyncChildrenGeometry();
    }

    /// <inheritdoc/>
    protected override void OnDispose()
    {
        DisposeChildren();
        base.OnDispose();
    }
}