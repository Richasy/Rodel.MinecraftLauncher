// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Worlds;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal static class WorldText
{
    internal static string Action(WorldAction action) => action switch
    {
        WorldAction.Import => Resources.WorldImport,
        WorldAction.Export => Resources.WorldExport,
        WorldAction.Backup => Resources.WorldBackup,
        WorldAction.Restore => Resources.WorldRestore,
        WorldAction.Remove => Resources.WorldRemove,
        WorldAction.Compress => Resources.WorldCompress,
        _ => Resources.WorldRecover,
    };

    internal static string Mode(WorldItem world) => world.Hardcore ? Resources.WorldHardcore : world.GameType switch
    {
        0 => Resources.WorldSurvival,
        1 => Resources.WorldCreative,
        2 => Resources.WorldAdventure,
        3 => Resources.WorldSpectator,
        _ => Resources.ValueNotAvailable,
    };

    internal static string Date(long? value)
    {
        if (value is null or < -62135596800000L or > 253402300799999L)
            return Resources.ValueNotAvailable;
        return DateTimeOffset.FromUnixTimeMilliseconds(value.Value).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }
}

internal sealed partial class WorldsPanel : Control
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly ContentController _controller;
    private readonly Window _window;
    private readonly Action<WorldAction, WorldItem?, string?> _open;
    private readonly WorldListStore _worlds;
    private MenuFlyout? _menu;
    private readonly WorldRows _rows;
    private readonly Action<Core.Worlds.Maps.WorldMapSelection> _showMap;

    internal WorldsPanel(
        LauncherClient client,
        HomeController home,
        ContentController controller,
        Window window,
        Action<WorldAction, WorldItem?, string?> open,
        Action<Core.Worlds.Maps.WorldMapSelection> showMap)
    {
        _client = client;
        _home = home;
        _controller = controller;
        _window = window;
        _open = open;
        _showMap = showMap;
        _worlds = Own(new WorldListStore((request, ct) => Task.Run(() => WorldListReader.ReadAsync(client, request, ct), ct), new DispatcherQueueScheduler()));
        RowFactory = item => new(item, this);
        _rows = new(this);
        ActionError = string.Empty;
        State = controller.State;
        Home = home.State;
    }

    internal IReadOnlyList<WorldItem> Rows => _worlds.Rows;
    internal Func<WorldItem, WorldRowView> RowFactory { get; }
    private partial string ActionError { get; set; }
    private string Error => ActionError.Length > 0 ? ActionError : _worlds.Error;
    private bool Loading => _worlds.Loading;
    private string CountText => WorldCountText.Format(Rows.Count);
    private int RecoveryCount => _worlds.RecoveryCount;
    private partial ContentState State { get; set; }
    private partial HomePageState Home { get; set; }
    internal bool Writable => !State.Active && !Home.Pending && !Home.CurrentLaunch.Busy && Home.LaunchObserved && Home.LaunchReadError.Length == 0 && Home.SelectedId == _worlds.InstanceId;

    internal Task SelectAsync(string id, bool force = false)
    {
        if (!force && _worlds.InstanceId == id)
            return Task.CompletedTask;
        if (_worlds.InstanceId != id)
            _menu?.Hide();
        ActionError = string.Empty;
        return _worlds.SelectAsync(id, force);
    }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star(), GridLength.Auto],
        [
            Border(Text(Error)
                    .Foreground(LauncherTheme.Error)
                    .Wrap())
                .Padding(20f, 8f)
                .Visible(Error.Length > 0)
                .Cell(0, 0),
            Overlay([
                ScrollView(_rows)
                    .Automation(new()
                    {
                        Name = Resources.ContentWorlds,
                        AutomationId = "world.list"
                    }),
                Grid(
                    [GridLength.Star()],
                    [GridLength.Star(), GridLength.Auto, GridLength.Star()],
                    [
                        Border(Text(Loading ? Resources.ContentLoading : Resources.WorldEmpty)
                                .FontSize(14f).Foreground(LauncherTheme.Muted)
                                .Wrap()
                                .Align(TextAlignment.Center))
                            .Padding(24f)
                            .Cell(0, 1),
                    ])
                    .Visible(Rows.Count == 0 && (Loading || Error.Length == 0)),
            ])
                .Cell(0, 1),
            Border(Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Text(CountText)
                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                            .VAlign(VerticalAlignment.Center)
                            .Automation(new()
                            {
                                Name = CountText,
                                AutomationId = "world.count"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.WorldRecover, () => _open(WorldAction.Recover, null, null))
                            .Style(LauncherButtons.DialogHeaderButton)
                            .MinHeight(28f)
                            .Enabled(Writable)
                            .Visible(RecoveryCount > 0)
                            .ToolTip(Resources.WorldRecoveryDescription)
                            .Automation(new()
                            {
                                Name = Resources.WorldRecover,
                                HelpText = Resources.WorldRecoveryDescription,
                                AutomationId = "world.recovery"
                            })
                            .Cell(1, 0),
                    ]))
                .Padding(20f, 8f)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(0f, 1f, 0f, 0f)
                .Cell(0, 2),
        ]);

    internal void Actions(WorldItem item)
    {
        if (item.InstanceId != _worlds.InstanceId || item.InstanceId != _home.State.SelectedId)
            return;
        MenuFlyoutItem Command(WorldAction action) => new(WorldText.Action(action), () =>
            {
                _menu?.Hide();
                _open(action, item, null);
            })
        {
            IsEnabled = Writable,
            Automation = new()
            {
                Name = WorldText.Action(action),
                AutomationId = "world.action." + action.ToString().ToLowerInvariant()
            }
        };
        _menu = new MenuFlyout(
            new MenuFlyoutItem(Resources.SeedReadWorld, () => { _menu?.Hide(); _showMap(new(item.InstanceId, item.Folder)); })
            {
                Automation = new() { Name = Resources.SeedReadWorld, AutomationId = "world.action.map" }
            },
            new MenuFlyoutItem(Resources.WorldOpenFolder, () => OpenFolder(item))
            {
                Automation = new()
                {
                    Name = Resources.WorldOpenFolder,
                    AutomationId = "world.action.folder"
                }
            },
            Command(WorldAction.Export),
            Command(WorldAction.Backup),
            Command(WorldAction.Restore),
            new MenuFlyoutSeparator(),
            Command(WorldAction.Remove)).Style(LauncherMenus.CommandMenu);
        _menu.ShowAt(_window);
    }

    private void OpenFolder(WorldItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_client.Worlds.GetDirectory(item.InstanceId, item.Folder)) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            ActionError = error.Message;
        }
    }

    private void OnHomeChanged(HomePageState state) => Home = state;

    private void OnChanged(ContentState state)
    {
        var refresh = State.Active && !state.Active && state.WorldOperation;
        State = state;
        if (refresh)
            _ = SelectAsync(_worlds.InstanceId, force: true);
    }

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        _home.StateChanged += OnHomeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _controller.Changed -= OnChanged;
        _home.StateChanged -= OnHomeChanged;
        _menu?.Hide();
        base.OnDispose();
    }
}

internal sealed partial class WorldRows(WorldsPanel panel) : Control
{
    private Ui Body => ItemsRepeater(panel.Rows, static row => row.Key, panel.RowFactory, new StackLayout { EstimatedItemExtent = 106f }).KeyboardNavigation();
}