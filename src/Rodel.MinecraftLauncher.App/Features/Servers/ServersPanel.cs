// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

internal sealed partial class ServersPanel : Control
{
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly ServerConnectionPresenter _connections;
    private readonly Action<ServerFavorite> _manageClients;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _refresh;
    private long _refreshRevision = -1;
    private MenuFlyout? _menu;
    private InteractionNode? _returnFocus;
    private bool _restoreFocus;
    private bool _active;
    private bool _opening;

    internal ServersPanel(
        HomeController home,
        Window window,
        ServerConnectionPresenter connections,
        Action<ServerFavorite> manageClients)
    {
        _home = home;
        _window = window;
        _connections = connections;
        _manageClients = manageClients;
        Favorites = [];
        Statuses = new Dictionary<string, ServerStatus>();
        Error = string.Empty;
        Home = home.State;
        Viewport = window.ClientSize;
    }

    private partial ServerFavorite[] Favorites { get; set; }
    private partial IReadOnlyDictionary<string, ServerStatus> Statuses { get; set; }
    private partial string Error { get; set; }
    private partial bool Refreshing { get; set; }
    private partial HomePageState Home { get; set; }
    private partial Size Viewport { get; set; }
    private bool Compact => Viewport.Width < 1080f;
    private ServerRowData[] Rows => [..Favorites.Select(favorite =>
            {
                var status = Statuses.GetValueOrDefault(favorite.Address) ?? new();
                return new ServerRowData(
                    favorite,
                    status.Title,
                    status.Detail,
                    status.Error ?? status.Value?.Error ?? string.Empty,
                    favorite.InstanceLabel(Home.Instances),
                    Compact,
                    () => Open(ServerAction.Connect, favorite),
                    () => ShowMenu(favorite),
                    Resources.ServerConnect,
                    string.Format(Resources.ServerConnectHeading, favorite.Name),
                    string.Format(Resources.ServerMore, favorite.Name),
                    LauncherTheme.Ink,
                    LauncherTheme.Muted,
                    LauncherTheme.Edge,
                    _window.FlowDirection == FlowDirection.RightToLeft,
                    status.Icon);
            })];

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            Text(string.Format(Resources.ServerReadFailed, Error))
                .FontSize(12f).Foreground(LauncherTheme.Error)
                .Wrap()
                .Visible(Error.Length > 0)
                .Automation(new()
                {
                    Name = string.Format(Resources.ServerReadFailed, Error),
                    AutomationId = "servers.error"
                })
                .Cell(0, 0),
            Overlay([
                new LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(1f),
                Border(Grid(
                    [GridLength.Star()],
                    [GridLength.Auto, GridLength.Star(), GridLength.Auto],
                    [
                        Grid(
                            ServerRow.Columns(false),
                            [GridLength.Auto],
                            [
                                Text(Resources.ServerNameAddress)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Cell(0, 0),
                                Text(Resources.ServerStatusHeading)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Cell(1, 0),
                                Text(Resources.ServerClientInstance)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Cell(2, 0),
                                Text(Resources.ServerActions)
                                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                                    .Cell(3, 0),
                            ])
                            .ColumnSpacing(20f)
                            .Padding(24f, 14f)
                            .Visible(!Compact && Favorites.Length > 0)
                            .Cell(0, 0),
                        Overlay([
                            ScrollView(new ServerListBody(LauncherAppearance.Capture(),Rows))
                                .Automation(new()
                                {
                                    Name = Resources.ServersTab,
                                    AutomationId = "servers.list"
                                }),
                            new EmptyPlaceholderView(
                                "servers",
                                Resources.ServerEmpty,
                                Resources.ServerEmptyHint,
                                LauncherTheme.Quiet,
                                LauncherTheme.EmptyPlaceholder,
                                "servers.empty").Margin(24f)
                                .Visible(Favorites.Length == 0 && !Refreshing && Error.Length == 0),
                        ])
                            .Cell(0, 1),
                        Border(Grid(
                                [GridLength.Star(), GridLength.Auto],
                                [GridLength.Auto],
                                [
                                    Text(string.Format(Resources.ServerCount, Favorites.Length))
                                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                                        .VAlign(VerticalAlignment.Center)
                                        .Cell(0, 0),
                                    LauncherControls.OutlineCommand(Resources.ServerRefresh, () => _ = RefreshAsync(force: true))
                                        .Enabled(!Refreshing)
                                        .Automation(new()
                                        {
                                            Name = Resources.ServerRefresh,
                                            AutomationId = "servers.refresh"
                                        })
                                        .Cell(1, 0),
                                ]))
                            .Padding(24f, 12f)
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(0f, 1f, 0f, 0f)
                            .Cell(0, 2),
                    ]))
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f),
            ]).Cell(0, 1),
        ])
        .RowSpacing(Error.Length > 0 ? 12f : 0f);

    internal void Activate(bool active)
    {
        _active = active;
        if (active)
            _ = RefreshAsync();
        if (!active)
        {
            _menu?.Hide();
        }

        _connections.Statuses.SetTargets("instances", Favorites, active);
    }

    internal void Add() => Open(ServerAction.Add);

    internal void Direct() => Open(ServerAction.Direct);

    private async Task RefreshAsync(bool force = false)
    {
        if (!force && _refresh is not null && _refreshRevision == Home.InstancesRevision)
            return;
        _refresh?.Cancel();
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _refresh = refresh;
        _refreshRevision = Home.InstancesRevision;
        Refreshing = true;
        Error = string.Empty;
        try
        {
            var favorites = await _connections.ReadFavoritesAsync(refresh.Token, force);
            if (refresh.IsCancellationRequested)
                return;
            Favorites = favorites;
            _connections.Statuses.SetTargets("instances", favorites, _active, force: force);
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!refresh.IsCancellationRequested)
                Error = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_refresh, refresh))
            {
                _refresh = null;
                Refreshing = false;
            }

            refresh.Dispose();
        }
    }

    private void ShowMenu(ServerFavorite favorite)
    {
        _returnFocus = _window.FocusedElement;
        _menu = new MenuFlyout(
            new MenuFlyoutItem(Resources.ServerManageClients, () =>
                {
                    _menu?.Hide();
                    _manageClients(favorite);
                })
            {
                Icon = new SymbolIcon(Symbol.Setting),
                Automation = new() { AutomationId = "servers.menu.clients" }
            },
            new MenuFlyoutItem(Resources.ServerAddClient, () => Open(ServerAction.ClaimClient, favorite))
            {
                Icon = new SymbolIcon(Symbol.Add),
                Automation = new() { AutomationId = "servers.menu.add-client" }
            },
            new MenuFlyoutItem(Resources.ServerEdit, () => Open(ServerAction.Edit, favorite))
            {
                Icon = new SymbolIcon(Symbol.Edit),
                Automation = new() { AutomationId = "servers.menu.edit" }
            },
            new MenuFlyoutSeparator(),
            new MenuFlyoutItem(Resources.ServerRemove, () => Open(ServerAction.Remove, favorite))
            {
                Icon = new SymbolIcon(Symbol.Delete) { Foreground = LauncherTheme.Error },
                Automation = new() { AutomationId = "servers.menu.remove" }
            }).Style(LauncherMenus.CommandMenu);
        _menu.ShowAt(_window);
    }

    private void Open(ServerAction action, ServerFavorite? favorite = null)
    {
        if (_menu is { IsOpen: true } menu)
        {
            if (_opening)
                return;
            _opening = true;
            void OnMenuClosed()
            {
                menu.Closed -= OnMenuClosed;
                _opening = false;
                _window.RequestAnimationFrame(_ =>
                    {
                        if (!_lifetime.IsCancellationRequested)
                            Open(action, favorite);
                    });
            }

            menu.Closed += OnMenuClosed;
            menu.Hide();
            return;
        }

        if (_opening)
            return;
        if (ModalPopup.IsOpen(_window))
            return;
        if (action is not (ServerAction.Edit or ServerAction.Associate or ServerAction.Remove))
            _returnFocus = _window.FocusedElement;
        _connections.Show(action, favorite, favorite is null ? null : Statuses.GetValueOrDefault(favorite.Address), () => _restoreFocus = true);
    }

    private void OnHomeChanged(HomePageState state)
    {
        var inventoryChanged = Home.InstancesRevision != state.InstancesRevision;
        Home = state;
        if (inventoryChanged && _active)
            _ = RefreshAsync();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Viewport = args.Size;

    private void OnLayoutUpdated()
    {
        if (!_restoreFocus || !_active || ModalPopup.IsOpen(_window))
            return;
        var target = _returnFocus?.CanTakeFocus == true
            ? _returnFocus
            : Interaction is { IsCollapsed: false } root ? FocusTraversal.Next(root, current: null, forward: true) : null;
        if (target?.CanTakeFocus != true)
            return;
        _window.SetFocus(target, FocusState.Programmatic);
        _restoreFocus = false;
    }

    protected override IDisposable? OnMounted()
    {
        _home.StateChanged += OnHomeChanged;
        _window.SizeChanged += OnSizeChanged;
        _window.LayoutUpdated += OnLayoutUpdated;
        _connections.Changed += OnFavoritesChanged;
        _connections.Statuses.Changed += OnStatusesChanged;
        OnStatusesChanged();
        return null;
    }

    private void OnFavoritesChanged()
    {
        _refresh?.Cancel();
        _refresh = null;
        if (_active)
            _ = RefreshAsync();
    }

    private void OnStatusesChanged() => Statuses = _connections.Statuses.Values;

    protected override void OnDispose()
    {
        _lifetime.Cancel();
        _home.StateChanged -= OnHomeChanged;
        _window.SizeChanged -= OnSizeChanged;
        _window.LayoutUpdated -= OnLayoutUpdated;
        _connections.Changed -= OnFavoritesChanged;
        _connections.Statuses.Changed -= OnStatusesChanged;
        _connections.Statuses.SetTargets("instances", [], false);
        _lifetime.Dispose();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct ServerListBody(LauncherAppearance Theme, ServerRowData[] Rows)
{
    internal static Func<ServerRowData, ServerRow> RowTemplate(LauncherAppearance theme) => row => new ServerRow(theme, row);

    public Ui Body => ItemsRepeater(Rows, static row => row.Favorite.Id, RowTemplate(Theme), new StackLayout { EstimatedItemExtent = 82f }).KeyboardNavigation();
}