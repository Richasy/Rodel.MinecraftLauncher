// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

internal sealed partial class InstanceVirtualList : Control
{
    private readonly InstancesPage _page;
    private readonly Action<IReadOnlyList<int>> _selectionChanged;
    private readonly Func<InstanceListRowState, InstanceListRow> _createRow;

    internal InstanceVirtualList(InstancesPage page)
    {
        _page = page;
        _selectionChanged = OnSelectionChanged;
        _createRow = row => new InstanceListRow(LauncherAppearance.Capture(), row, _page);
        Items = [];
        SelectedIndex = -1;
    }

    private partial IReadOnlyList<InstanceListRowState> Items { get; set; }
    private partial int SelectedIndex { get; set; }

    internal InstanceVirtualList WithRows(IReadOnlyList<ManagedInstance> rows, HomePageState state, bool compact)
    {
        SelectedIndex = rows.ToList().FindIndex(row => row.Id == state.SelectedId);
        var appearance = InstanceListAppearance.Capture();
        Items = [..rows.Select(row => new InstanceListRowState(
                    row,
                    compact,
                    appearance,
                    row.Id == state.SelectedId,
                    state.LaunchStates.GetValueOrDefault(row.Id),
                    state.PendingInstances.Contains(row.Id),
                    row.Runtime,
                    row.Configuration,
                    row.Content,
                    row.CanLaunch ? string.Empty : row.LaunchBlockReason,
                    state.InstallationIncomplete(row.Id)))];
        return this;
    }

    private Ui Body => ItemsView(
        Items,
        static row => row.Item.Id,
        _createRow,
        new StackLayout { EstimatedItemExtent = 76f },
        accessibleName: static row => row.Item.Name,
        itemAutomation: static row => new()
        {
            AutomationId = "instances.select." + row.Item.Id,
            HelpText = row.Selected ? Resources.CurrentInstance : Resources.SwitchInstance,
        })
        .SelectionMode(SelectionMode.Single)
        .SelectionVisualMode(LauncherTheme.HighContrast ? ItemsViewSelectionVisualMode.BackgroundOnly : ItemsViewSelectionVisualMode.None)
        .ItemChrome(LauncherAppearance.Capture().ButtonPalette.ListItems)
        .ItemContentInteraction(ItemsViewItemContentInteraction.Interactive)
        .SelectedIndex(SelectedIndex)
        .StartBringItemIntoView(SelectedIndex)
        .OnSelectionChanged(_selectionChanged)
        .Automation(new()
        {
            Name = Resources.NavInstances,
            AutomationId = "instances.list"
        });

    private void OnSelectionChanged(IReadOnlyList<int> indices)
    {
        if (indices.Count > 0 && indices[0] >= 0 && indices[0] < Items.Count)
            _page.Select(Items[indices[0]].Item.Id);
    }

    internal bool RestoreSelectionFocus(Window window)
    {
        var root = Interaction;
        if (root is null)
            return false;
        for (var current = window.FocusedElement; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }

        return FindSelected(root) is { } selected && (window.SetFocus(selected, FocusState.Programmatic) || ReferenceEquals(window.FocusedElement, selected));
    }

    private InteractionNode? FindSelected(InteractionNode node)
    {
        if (node.IsCollapsed || !node.IsEffectivelyEnabled)
            return null;
        if (node is ItemContainerInteraction row && row.Index == SelectedIndex && row.CanTakeFocus)
            return row;
        foreach (var child in node.Children)
        {
            if (FindSelected(child) is { } selected)
                return selected;
        }

        return null;
    }
}