// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

[View]
internal readonly partial record struct HomeInstancePanel(
    LauncherAppearance Theme,
    string EmptyText,
    HomePageState State,
    LauncherLayout Layout,
    HomeAppearance Appearance,
    Action<string> Select,
    Action Manage,
    HomeInstanceList List,
    MenuFlyout AddMenu,
    Action Create,
    HomeServerState Servers,
    HomeServerList ServerList,
    Action<int> ChooseMode,
    Action<string> SelectServer,
    Action AddServer)
{
    internal bool EmptyError => Servers.ServerMode ? Servers.Error.Length > 0 : State.InstanceLoadError.Length > 0;
    internal bool Loading => Servers.ServerMode ? Servers.Loading : !State.InstancesLoaded;
    internal string EmptyLabel => Servers.ServerMode
        ? Servers.Loading
        ? Resources.HomeReadingServers
        : EmptyError ? Servers.Error : Resources.ServerEmpty
        : EmptyText;
    internal Color EmptyInk => EmptyError ? Appearance.Error : Appearance.Placeholder;
    internal string EmptyHint => Servers.ServerMode ? Resources.HomeEmptyServerHint : Resources.HomeEmptyLocalHint;
    internal bool ShowEmptyHint => !EmptyError && !(Servers.ServerMode && Servers.Loading);
    internal bool EmptyLocal => State.LocalInstances.Count == 0;
    internal bool EmptyServer => Servers.Favorites.Count == 0;
    internal IReadOnlyList<HomeInstanceRow> Rows
    {
        get
        {
            var state = State;
            var appearance = Appearance;
            var local = state.LocalInstances;
            return [..local.Select((item, index) => new HomeInstanceRow(
                        item,
                        item.Id == state.SelectedId,
                        index == local.Count - 1,
                        item.Id == state.SelectedId ? appearance.SelectedRow : appearance.NormalRow))];
        }
    }

    internal Func<HomeInstanceRow, InstanceShortcut> ItemTemplate
    {
        get
        {
            var select = Select;
            var theme = Theme;
            var list = List;
            return row => new(
theme, row.Instance,
                row.Selected,
                () => select(row.Instance.Id),
                row.Last,
                row.Appearance,
                () => list.ContextMenuFor(row.Instance.Id));
        }
    }

    internal HomeServerRow[] ServerRows
    {
        get
        {
            var servers = Servers;
            var appearance = Appearance;
            var select = SelectServer;
            var theme = Theme;
            return [..servers.Favorites.Select((item, index) =>
                    {
                        var status = servers.Statuses.GetValueOrDefault(item.Address) ?? new Features.Servers.ServerStatus();
                        var selected = item.Id == servers.SelectedId;
                        var outline = theme.HighContrast && selected ? theme.SelectionForeground : appearance.Ink;
                        var color = theme.HighContrast
                            ? outline
                            : status.Loading || status.CheckedAt is null
                            ? appearance.Quiet
                            : status.Online ? theme.SuccessForeground : theme.Warning;
                        return new HomeServerRow(
                            item.Id,
                            item.Name,
                            item.Address,
                            selected,
                            index == servers.Favorites.Count - 1,
                            () => select(item.Id),
                            selected ? appearance.SelectedRow : appearance.NormalRow,
                            System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft,
                            selected ? Resources.HomeCurrentServer : Resources.HomeSelectServer,
                            status.Icon,
                            color,
                            outline,
                            string.Format(Resources.ServerStatusFor, item.Name, status.Title),
                            status.Tooltip);
                    })];
        }
    }

    public Ui Body => Overlay([
        new LauncherMaterialSurface(Appearance.Surface).Margin(1f),
        Border(Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Row([
                            Text(Resources.HomeMyGames)
                                .FontSize(15f).FontWeight(700).Foreground(Appearance.Ink)
                                .VAlign(VerticalAlignment.Center),
                            Text((Servers.ServerMode ? Servers.Favorites.Count : State.LocalInstances.Count).ToString("D2"))
                                .FontSize(11f).Foreground(Appearance.Quiet)
                                .VAlign(VerticalAlignment.Center),
                        ])
                            .Spacing(10f)
                            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Theme,Resources.Manage, Manage)
                            .Automation(new()
                            {
                                Name = Servers.ServerMode ? Resources.ServersTab : Resources.ManageInstances,
                                AutomationId = "instances.manage"
                            })
                            .Cell(1, 0),
                    ])
                    .Margin(new Thickness(LauncherLayout.InstancePanelInset, 0f, LauncherLayout.InstancePanelInset, 12f))
                    .Cell(0, 0),
                new Features.Instances.InstanceViewSegments(
                    Servers.ServerMode,
                    ChooseMode,
                    Resources.InstancesLocal,
                    Resources.ServersTab,
                    Theme.Materials.SegmentedMaterial,
                    "home.targets").Margin(new Thickness(LauncherLayout.InstancePanelInset, 0f, LauncherLayout.InstancePanelInset, 12f))
                    .Cell(0, 1),
                new LoadingPlaceholderView(Theme, Servers.ServerMode ? Resources.HomeReadingServers : Resources.InstancesLoading, "instances.loading")
                    .Visible(Loading && (Servers.ServerMode ? EmptyServer : EmptyLocal))
                    .Cell(0, 2),
                new EmptyPlaceholderView(
                    EmptyError
                        ? "warning"
                        : Servers.ServerMode ? "servers" : "instances",
                    EmptyLabel,
                    ShowEmptyHint ? EmptyHint : string.Empty,
                    EmptyError ? Appearance.Error : Appearance.Quiet,
                    EmptyInk,
                    "instances.empty").Visible(!Loading && (Servers.ServerMode ? EmptyServer : EmptyLocal))
                    .Margin(new Thickness(LauncherLayout.InstancePanelInset, 24f, LauncherLayout.InstancePanelInset, 24f))
                    .Cell(0, 2),
                List.WithRows(Rows, State.SelectedId)
                    .Visible(!Servers.ServerMode && State.LocalInstances.Count > 0)
                    .Cell(0, 2),
                ServerList.WithRows(ServerRows, Servers.SelectedId)
                    .Visible(Servers.ServerMode && Servers.Favorites.Count > 0)
                    .Cell(0, 2),
                LauncherControls.Command(Theme,Resources.ServerAdd, AddServer)
                    .Style(Appearance.Command.Palette(Theme.ButtonPalette.Create(EmptyServer, false)))
                    .AsPrimary(EmptyServer)
                    .Automation(new()
                    {
                        Name = Resources.ServerAdd,
                        AutomationId = "home.servers.add"
                    })
                    .Margin(new Thickness(LauncherLayout.InstancePanelInset, 12f, LauncherLayout.InstancePanelInset, 0f))
                    .Visible(Servers.ServerMode)
                    .Cell(0, 3),
                SplitButton(new LauncherSplitButtonLabel(Resources.CreateInstance, LauncherStyles.Text), Create, AddMenu.Style(Theme.Menus.CommandMenu)
                        .Width(Layout.InstancePanelContentWidth))
                    .Style(EmptyLocal && State.InstancesLoaded ? Theme.Menus.AccentSplitCommand : Theme.Menus.SplitCommand)
                    .CornerRadius(CornerRadius.Zero)
                    .Padding(new Thickness(12f, 8f, 12f, 8f))
                    .SurfacePainter(EmptyLocal && State.InstancesLoaded ? LauncherSplitButtonSurface.PaintAccent : LauncherSplitButtonSurface.Paint)
                    .HAlign(HorizontalAlignment.Stretch)
                    .MinHeight(39f)
                    .Visible(!Servers.ServerMode)
                    .Automation(new()
                    {
                        Name = Resources.CreateInstance,
                        AutomationId = "instances.create"
                    })
                    .Margin(new Thickness(LauncherLayout.InstancePanelInset, 12f, LauncherLayout.InstancePanelInset, 0f))
                    .Cell(0, 3),
            ])
            .RowSpacing(0f))
        .Padding(new Thickness(0f, LauncherLayout.InstancePanelInset, 0f, 12f))
        .BorderBrush(Appearance.Edge)
        .BorderThickness(1f)
        .Shadow(Appearance.Shadow),
    ]);
}