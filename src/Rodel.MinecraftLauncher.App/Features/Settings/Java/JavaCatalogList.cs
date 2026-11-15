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

internal sealed partial class JavaCatalogList : Control
{
    private readonly JavaManagerController _controller;
    private readonly Func<JavaCatalogItem, JavaCatalogRow> _factory = item => new(LauncherAppearance.Capture(), item);

    internal JavaCatalogList(JavaManagerController controller)
    {
        _controller = controller;
        Items = [];
        SelectedIndex = -1;
        Editable = true;
    }

    private partial JavaCatalogItem[] Items { get; set; }
    private partial int SelectedIndex { get; set; }
    private partial bool Editable { get; set; }

    internal JavaCatalogList WithState(JavaManagerState state)
    {
        Items = [.. state.Components.Select((component, index) => new JavaCatalogItem(component, state.ComponentLabels[index], index == state.Component))];
        SelectedIndex = state.Component;
        Editable = state.Editable;
        return this;
    }

    private Ui Body => ItemsView(
        Items,
        static item => item.Component,
        _factory,
        new StackLayout { EstimatedItemExtent = 62f },
        accessibleName: static item => item.Label,
        itemAutomation: static item => new()
        {
            AutomationId = "java.component." + item.Component,
            HelpText = item.Component
        })
        .SelectionMode(SelectionMode.Single)
        .SelectionVisualMode(ItemsViewSelectionVisualMode.None)
        .SelectedIndex(SelectedIndex)
        .ItemContentInteraction(ItemsViewItemContentInteraction.PassiveOnly)
        .OnSelectionChanged(OnSelectionChanged)
        .Enabled(Editable)
        .Automation(new()
        {
            Name = Resources.JavaSelectVersion,
            AutomationId = "java.catalog.list"
        });

    private void OnSelectionChanged(IReadOnlyList<int> indices)
    {
        if (indices.Count > 0 && indices[0] >= 0 && indices[0] < Items.Length)
            _controller.SetComponent(indices[0]);
    }
}