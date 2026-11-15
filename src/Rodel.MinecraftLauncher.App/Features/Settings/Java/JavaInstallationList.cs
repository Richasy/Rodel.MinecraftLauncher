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

internal sealed partial class JavaInstallationList : Control
{
    private readonly JavaManagerController _controller;
    private readonly Func<JavaInstallationRowData, JavaInstallationRow> _factory = data => new(data);

    internal JavaInstallationList(JavaManagerController controller)
    {
        _controller = controller;
        Rows = [];
        SelectedIndex = -1;
        Editable = true;
    }

    private partial JavaInstallationRowData[] Rows { get; set; }
    private partial int SelectedIndex { get; set; }
    private partial bool Editable { get; set; }

    internal JavaInstallationList WithState(JavaManagerState state)
    {
        Rows = [..state.Entries.Select(entry =>
                {
                    var isDefault = state.Default.Length > 0 && (entry.Id == state.Default || string.Equals(entry.Path, state.Default, StringComparison.OrdinalIgnoreCase));
                    var source = entry.Id is null
                        ? Resources.JavaDiscovered
                        : entry.Managed ? Resources.JavaManaged : Resources.JavaAssociated;
                    var name = string.Format(Resources.JavaEntryTitle, entry.Version, entry.Usable ? Resources.JavaUsable : Resources.JavaUnusable) + " · " + source + (isDefault ? " · " + Resources.JavaCurrentDefault : string.Empty);
                    return new JavaInstallationRowData(
                        entry,
                        entry.Key == state.Selected,
                        isDefault,
                        name,
                        new(
                            LauncherTheme.Ink,
                            LauncherTheme.Muted,
                            LauncherTheme.Warning,
                            LauncherTheme.Edge,
                            LauncherMaterials.Acrylic));
                })];
        SelectedIndex = Array.FindIndex(Rows, row => row.Selected);
        Editable = state.Editable;
        return this;
    }

    private Ui Body => ItemsView(
        Rows,
        static row => row.Entry.Key,
        _factory,
        new StackLayout { EstimatedItemExtent = 98f },
        accessibleName: static row => row.Name,
        itemAutomation: static row => new()
        {
            AutomationId = "java.entry." + row.Entry.Key,
            HelpText = row.Entry.Path
        })
        .SelectionMode(SelectionMode.Single)
        .SelectionVisualMode(ItemsViewSelectionVisualMode.None)
        .ItemContentInteraction(ItemsViewItemContentInteraction.PassiveOnly)
        .SelectedIndex(SelectedIndex)
        .StartBringItemIntoView(SelectedIndex)
        .OnSelectionChanged(OnSelectionChanged)
        .Enabled(Editable)
        .Automation(new()
        {
            Name = Resources.JavaLocalList,
            AutomationId = "java.list"
        });

    private void OnSelectionChanged(IReadOnlyList<int> indices)
    {
        if (indices.Count > 0 && indices[0] >= 0 && indices[0] < Rows.Length)
            _controller.Select(Rows[indices[0]].Entry.Key);
    }
}