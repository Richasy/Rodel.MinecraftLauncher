// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Popups;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed partial class HomeInstanceList : Control
{
    private readonly Window _window;
    private readonly PoolingItemFactory<HomeInstanceRow, InstanceShortcut> _factory;
    private readonly StackLayout _layout = new()
    {
        EstimatedItemExtent = InstanceShortcut.RowHeight,
        Spacing = InstanceShortcut.RowGap,
    };
    private readonly ScrollView _scroll;
    private readonly HomeListSelectionReveal _selectionReveal;
    private readonly Func<string, IFlyout?> _contextMenu;
    private string? _selectedId;
    private int _selectedIndex = -1;
    private bool _reveal;
    private readonly Dictionary<string, int> _keys = [with(StringComparer.Ordinal)];

    internal HomeInstanceList(Window window, Action<string> select, Func<string, IFlyout?> contextMenu)
    {
        _window = window;
        _contextMenu = contextMenu;
        _factory = new([], row => KeyFor(row.Instance.Id), row => new InstanceShortcut(
LauncherAppearance.Capture(), row.Instance,
                row.Selected,
                () => select(row.Instance.Id),
                row.Last,
                row.Appearance,
                () => ContextMenuFor(row.Instance.Id)), autoProject: true);
        var repeater = new ItemsRepeater(_factory, _layout).KeyboardNavigation();
        _scroll = new ScrollView(repeater).Padding(LauncherLayout.InstancePanelInset, 0f);
        _selectionReveal = new(repeater, _scroll);
    }

    internal IFlyout? ContextMenuFor(string id) => _contextMenu(id);

    private int KeyFor(string id)
    {
        if (!_keys.TryGetValue(id, out var key))
            _keys[id] = key = _keys.Count;
        return key;
    }

    internal HomeInstanceList WithRows(IReadOnlyList<HomeInstanceRow> rows, string selectedId)
    {
        var index = rows.ToList().FindIndex(row => row.Instance.Id == selectedId);
        _reveal |= selectedId != _selectedId || index != _selectedIndex;
        _selectedId = selectedId;
        _selectedIndex = index;
        _factory.UpdateItems(rows);
        return this;
    }

    private Ui Body => _scroll.Automation(new()
    {
        Name = Resources.MyInstances,
        AutomationId = "home.instances.scroll"
    });

    private void RevealSelection()
    {
        if (!_reveal || _selectedIndex < 0 || _scroll.ViewportSize.Height <= 0)
            return;
        _reveal = false;
        _reveal = !_selectionReveal.TryReveal(_selectedIndex, _factory.KeyAt(_selectedIndex));
    }

    protected override IDisposable? OnMounted()
    {
        _selectionReveal.Attach();
        _window.LayoutUpdated += RevealSelection;
        return null;
    }

    protected override void OnDispose()
    {
        _window.LayoutUpdated -= RevealSelection;
        _selectionReveal.Dispose();
        base.OnDispose();
    }
}