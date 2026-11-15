// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class DiagnosticsSessionList : Control
{
    private readonly DiagnosticsExportController _controller;
    private readonly Func<DiagnosticSessionItem, DiagnosticSessionView> _factory = item => new(LauncherAppearance.Capture(), item);

    internal DiagnosticsSessionList(DiagnosticsExportController controller)
    {
        _controller = controller;
        Items = [];
        Selected = -1;
    }

    private partial DiagnosticSessionItem[] Items { get; set; }
    private partial int Selected { get; set; }
    private partial bool Editable { get; set; }

    internal DiagnosticsSessionList WithState(DiagnosticsExportState state)
    {
        Items = [.. state.Sessions.Select((session, index) => new DiagnosticSessionItem(session, index == state.Selected))];
        Selected = state.Selected;
        Editable = state.Editable;
        return this;
    }

    private Ui Body => ItemsView(
        Items,
        static item => item.Session.Id,
        _factory,
        new StackLayout { EstimatedItemExtent = 64f },
        accessibleName: static item => item.Session.Label,
        itemAutomation: static item => new()
        {
            AutomationId = "diagnostics.session." + item.Session.Id,
            HelpText = item.Session.Id
        })
        .SelectionMode(SelectionMode.Single)
        .SelectionVisualMode(ItemsViewSelectionVisualMode.None)
        .SelectedIndex(Selected)
        .ItemContentInteraction(ItemsViewItemContentInteraction.PassiveOnly)
        .OnSelectionChanged(indices => Choose(indices))
        .Enabled(Editable)
        .Automation(new()
        {
            Name = Resources.DiagnosticsSelectSession,
            AutomationId = "diagnostics.sessions"
        });

    private void Choose(IReadOnlyList<int> indices)
    {
        if (indices.Count > 0)
            _controller.Select(indices[0]);
    }
}