// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Import;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Features.Packs;
using Rodel.MinecraftLauncher.App.Features.Servers;
using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed partial class HomePage : Control
{
    private HomePageText Presentation => ServerState.ServerMode ? ServerState.Presentation : HomePageText.Capture(State);
    private HomePageState HeroState => ServerState.ServerMode ? ServerState.Project() : State;
    private readonly HomeController _controller;
    private readonly Window _window;
    private readonly WallpaperService _wallpapers;
    private readonly Action _manage;
    private readonly Action _manageServers;
    private readonly Action _showHome;
    private readonly Action _create;
    private readonly Action _import;
    private readonly Action<string?> _importPack;
    private readonly InstanceOperationPresenter _instanceActions;
    private readonly PopupFocusRecovery _instanceFocus;
    private bool _active = true;
    internal partial string ActiveRoomName { get; set; }
    internal Action? ReturnToRoom { get; set; }
    private FileDropHandler PackDrop => field ??= new(AcceptPackDrop, ReceivePackDrop, value => PackDropping = value);
    private partial bool PackDropping { get; set; }
    private readonly LaunchErrorPresenter _errors;
    private readonly ImportPresenter _imports;
    private readonly Features.Create.CreatePresenter _creates;
    private string? _menuLanguage;
    private MenuFlyout InstanceMenu
    {
        get
        {
            var language = Sprout.Localization.Localizer.CurrentLanguage;
            if (field is not null && _menuLanguage == language)
                return field;
            _menuLanguage = language;
            return field = new(new MenuFlyoutItem(Resources.PackImportDirectory, _import)
            {
                Automation = new()
                {
                    Name = Resources.PackImportDirectory,
                    AutomationId = "instances.import"
                },
            }, new MenuFlyoutItem(Resources.PackImport, () => _importPack(null))
            {
                Automation = new()
                {
                    Name = Resources.PackImport,
                    AutomationId = "instances.import.pack"
                }
            });
        }
    }
    private readonly HomeInstanceList _instanceList;
    private readonly HomeServerList _serverList;
    private readonly HomeServerController _servers;
    private readonly ServerConnectionPresenter _connections;

    internal HomePage(
        HomeController controller,
        Window window,
        Action manage,
        WallpaperService wallpapers,
        Action create,
        Action import,
        Action<string, string> findDependency,
        LauncherClient client,
        AppSettings settings,
        ServerConnectionPresenter connections,
        Action manageServers,
        Action showHome,
        Action<string?> importPack,
        InstanceOperationPresenter instanceActions)
    {
        _controller = controller;
        ActiveRoomName = string.Empty;
        _window = window;
        _wallpapers = wallpapers;
        _manage = manage;
        _manageServers = manageServers;
        _showHome = showHome;
        _create = create;
        _import = import;
        _importPack = importPack;
        _instanceActions = instanceActions;
        _instanceFocus = new(window);
        _errors = new(window, controller.DiagnoseAsync, findDependency);
        _imports = new(window, controller.Import);
        _creates = new(window, controller.Create, id => _ = LaunchCreatedAsync(id));
        _instanceList = new(window, controller.Select, id => _instanceActions.ContextMenu(id, RestoreInstanceFocus));
        _serverList = new(window);
        _connections = connections;
        _servers = new(client, controller, settings, connections);
        ServerState = _servers.State;
        State = controller.State;
        ViewportWidth = window.ClientSize.Width;
        ViewportHeight = window.ClientSize.Height;
    }

    internal partial HomePageState State { get; private set; }
    private partial HomeServerState ServerState { get; set; }
    internal bool HasOwnedWork => _controller.HasOwnedWork || ServerState.Submitting;

    private void RestoreInstanceFocus()
    {
        if (_active)
            _instanceFocus.RequestIfLost(_instanceList);
    }

    internal void SelectLocalTarget() => ChooseMode(0);

    private async Task LaunchCreatedAsync(string id)
    {
        await _controller.RefreshAsync();
        if (_controller.State.Instances.FirstOrDefault(item => item.Id == id)?.ServerId is { } serverId)
        {
            LaunchServerClient(serverId, id);
            return;
        }

        SelectLocalTarget();
        _showHome();
        await _controller.LaunchCreatedAsync(id);
    }

    internal async void LaunchServerClient(string serverId, string instanceId)
    {
        _showHome();
        await _servers.LaunchClientAsync(serverId, instanceId);
    }

    internal void RefreshServerIdentity() => _servers.RefreshIdentity();

    internal void Activate(bool active)
    {
        _active = active;
        if (!active) PackDropping = false;
        _servers.Activate(active);
    }

    private bool AcceptPackDrop(IReadOnlyList<string> files) => _active && !ServerState.ServerMode && !_instanceActions.Opening
        && !ModalPopup.IsOpen(_window) && PackDropSource.Accepts(files);

    private void ReceivePackDrop(IReadOnlyList<string> files) => _importPack(files[0]);
    public partial float ViewportWidth { get; private set; }
    public partial float ViewportHeight { get; private set; }
    private LauncherLayout Layout => new(ViewportWidth, ViewportHeight);
    private static HomeAppearance Appearance => HomeAppearance.Capture();

    private Ui Body => Overlay([
        LauncherControls.OutlineCommand(string.Format(Resources.MultiplayerReturnRoom, ActiveRoomName), () => ReturnToRoom?.Invoke())
            .Visible(ActiveRoomName.Length > 0)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Margin(new Thickness(188f, 100f, 0f, 0f))
            .Automation(new() { Name = Resources.MultiplayerReturn, AutomationId = "home.multiplayer" }),
        Grid(
            [GridLength.Star(), GridLength.Pixels(Layout.InstancePanelWidth)],
            [GridLength.Star()],
            [
                new HomeHeroView(
LauncherAppearance.Capture(),                    Presentation,
                    HeroState,
                    Layout,
                    Appearance,
                    _wallpapers,
                    RunAction,
                    Configure,
                    OpenError,
                    CancelLaunch,
                    ServerState.ServerMode ? new HomeServerHero(
                        ServerState.Selected?.Address ?? string.Empty,
                        ServerState.Selected is not null,
                        ServerState.CanAct,
                        ServerState.CanConfigure,
                        ServerState.Submitting || ServerState.ReadingTarget || ServerState.Launch.TaskActive) : null,
                    _window.FlowDirection == FlowDirection.RightToLeft).MaxWidth(Layout.HeroWidth)
                    .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Bottom)
                    .Margin(new Thickness(0f, 0f, 28f, 0f))
                    .Cell(0, 0),
                DropTarget(
                    Grid(
                        [GridLength.Star()],
                        [GridLength.Star(), GridLength.Auto],
                        [
                            new HomeInstancePanel(
LauncherAppearance.Capture(),                                Presentation.EmptyText,
                                State,
                                Layout,
                                Appearance,
                                _controller.Select,
                                Manage,
                                _instanceList,
                                InstanceMenu,
                                _create,
                                ServerState,
                                _serverList,
                                ChooseMode,
                                _servers.Select,
                                _servers.Add).Cell(0, 0),
                            new FileDropHint(
LauncherAppearance.Capture(),                                    PackDropping,
                                    Resources.PackDropRelease,
                                    "home.instances.pack-drop")
                                .Visible(PackDropping && !ServerState.ServerMode)
                                .Margin(new Thickness(0f, 8f, 0f, 0f))
                                .Cell(0, 1),
                        ]),
                    PackDrop)
                    .VAlign(VerticalAlignment.Stretch)
                    .Cell(1, 0),
            ])
            .Margin(Layout.HomeMargin),
    ]);

    protected override IDisposable? OnMounted()
    {
        _controller.StateChanged += OnStateChanged;
        _servers.Changed += OnServersChanged;
        _servers.Start();
        _window.SizeChanged += OnSizeChanged;
        State = _controller.State;
        return null;
    }

    private void OnStateChanged(HomePageState state)
    {
        State = state;
        ObserveProblemTarget();
    }

    private void OnServersChanged(HomeServerState state)
    {
        ServerState = state;
        ObserveProblemTarget();
    }

    private void ObserveProblemTarget() => _controller.ObserveProblemsFor(ServerState.ServerMode ? ServerState.InstanceId : State.SelectedId);

    private void ChooseMode(int index)
    {
        PackDropping = false;
        _servers.ChooseMode(index);
    }

    private void RunAction()
    {
        if (ServerState.ServerMode)
        {
            if (ServerState.Selected is null)
                return;
            else if (ServerState.InstanceId is { } id && State.InstallationIncomplete(id))
                ShowResumeInstallation(id);
            else
                _ = _servers.RunAsync();
        }
        else if (State.Selected is null)
        {
            return;
        }
        else
        {
            if (State.CanResumeInstallation)
                ShowResumeInstallation(State.SelectedId);
            else
                _ = _controller.RunLaunchActionAsync();
        }
    }

    internal void ShowResumeInstallation(string id) => _creates.ShowFor(id, State.Instances.FirstOrDefault(item => item.Id == id)?.ServerId);

    private void Configure()
    {
        if (ServerState.ServerMode)
            _servers.Configure();
        else if (State.CanConfigure && State.Selected is { } selected)
            _instanceActions.Open(selected.Id, InstanceAction.Configure);
    }

    private async void CancelLaunch()
    {
        var id = ServerState.ServerMode ? ServerState.InstanceId : State.SelectedId;
        if (string.IsNullOrEmpty(id))
            return;
        try
        {
            await _controller.CancelLaunchAsync(id);
        }
        catch (Exception exception)
        {
            _controller.ReportError(id, exception.Message);
        }
    }

    private void Manage()
    {
        if (ServerState.ServerMode)
            _manageServers();
        else
            _manage();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e)
    {
        ViewportWidth = e.Size.Width;
        ViewportHeight = e.Size.Height;
    }

    internal void ShowCreate() => _creates.Show();

    internal void ShowImport() => _imports.Show();

    internal void ShowServerCreate(string serverId) => _creates.ShowFor(null, serverId);

    internal void ShowServerImport(string serverId) => _imports.Show(serverId);

    private async void OpenError()
    {
        var instanceId = ServerState.ServerMode ? ServerState.InstanceId : State.SelectedId;
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            _connections.ShowRecovery(instanceId, ServerState.ServerMode ? ServerState.Selected : null);
            return;
        }

        try
        {
            await _errors.ShowAsync(HeroState, _connections.RepairRequested);
        }
        catch (Exception exception)
        {
            _controller.ReportError(State.SelectedId, exception.Message);
        }
    }

    protected override void OnDispose()
    {
        _controller.StateChanged -= OnStateChanged;
        _servers.Changed -= OnServersChanged;
        _servers.Dispose();
        _window.SizeChanged -= OnSizeChanged;
        _errors.Dispose();
        _imports.Dispose();
        _creates.Dispose();
        _instanceFocus.Dispose();
        base.OnDispose();
    }
}