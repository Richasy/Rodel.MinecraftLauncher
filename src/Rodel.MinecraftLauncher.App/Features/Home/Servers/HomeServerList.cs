// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Servers;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed partial class HomeServerList : Control
{
    private readonly Window _window;
    private readonly ScrollView _scroll;
    private readonly PoolingItemFactory<HomeServerRow, HomeServerShortcut> _factory;
    private readonly HomeListSelectionReveal _selectionReveal;
    private readonly StackLayout _layout = new()
    {
        EstimatedItemExtent = InstanceShortcut.RowHeight,
        Spacing = InstanceShortcut.RowGap,
    };
    private readonly Dictionary<string, int> _keys = [with(StringComparer.Ordinal)];
    private string _selection = string.Empty;
    private int _index = -1;
    private bool _reveal;

    internal HomeServerList(Window window)
    {
        _window = window;
        _factory = new([], row => Key(row.Id), static row => new HomeServerShortcut(LauncherAppearance.Capture(), row), autoProject: true);
        var repeater = new ItemsRepeater(_factory, _layout).KeyboardNavigation();
        _scroll = new(repeater);
        _scroll.Padding(LauncherLayout.InstancePanelInset, 0f);
        _selectionReveal = new(repeater, _scroll);
    }

    private int Key(string id)
    {
        if (!_keys.TryGetValue(id, out var value))
            _keys[id] = value = _keys.Count;
        return value;
    }

    internal HomeServerList WithRows(IReadOnlyList<HomeServerRow> rows, string selected)
    {
        var index = rows.ToList().FindIndex(row => row.Id == selected);
        _reveal |= selected != _selection || index != _index;
        _selection = selected;
        _index = index;
        _factory.UpdateItems(rows);
        return this;
    }

    private Ui Body => _scroll.Automation(new()
    {
        Name = Resources.ServersTab,
        AutomationId = "home.servers.scroll"
    });

    private void Reveal()
    {
        if (!_reveal || _index < 0 || _scroll.ViewportSize.Height <= 0)
            return;
        _reveal = false;
        _reveal = !_selectionReveal.TryReveal(_index, _factory.KeyAt(_index));
    }

    protected override IDisposable? OnMounted()
    {
        _selectionReveal.Attach();
        _window.LayoutUpdated += Reveal;
        return null;
    }

    protected override void OnDispose()
    {
        _window.LayoutUpdated -= Reveal;
        _selectionReveal.Dispose();
        base.OnDispose();
    }
}