// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Packs;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Diagnostics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

/// <summary>The compact instance list; operation state and Core workflows remain independent of the shell.</summary>
internal sealed partial class InstancesPage : Control
{
    private readonly LauncherClient _client;
    private readonly Window _window;
    private readonly HomeController _home;
    private readonly Action _create;
    private readonly Action _import;
    private readonly Action _play;
    internal Action<string>? CreateMultiplayer { get; set; }
    private readonly Action<string> _resumeInstallation;
    private readonly Action<string> _createServer;
    private readonly Action<string> _importServer;
    private readonly Action<string, string> _playServer;
    private readonly InstanceOperationPresenter _actions;
    private readonly Features.Servers.ServersPanel _servers;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly InstanceVirtualList _list;
    private readonly Features.Runtime.RuntimePage _runtime;
    private partial bool RuntimeVisible { get; set; }
    private Flyout? _popup;
    private MenuFlyout? _menu;
    private long _observedRevision = -1;
    private readonly InstanceListStore _rows;
    private readonly Dictionary<string, InstanceListStore> _serverRows = [with(StringComparer.Ordinal)];
    private InstanceListStore RowStore => ServerOwner is { } owner ? _serverRows[owner.Id] : _rows;
    private bool _opening;
    private bool _restoreListFocus;
    private bool _restoreImportFocus;
    private bool? _entryRequested;
    private bool _active;
    private FileDropHandler PackDrop => field ??= new(AcceptPackDrop, ReceivePackDrop, value => PackDropping = value);
    private partial bool PackDropping { get; set; }
    private bool LocalPackTarget => !ServerMode && ServerOwner is null && !RuntimeVisible;

    private bool AcceptPackDrop(IReadOnlyList<string> files) => _active && LocalPackTarget && !_opening && !_actions.Opening
        && !ModalPopup.IsOpen(_window) && PackDropSource.Accepts(files);

    private void ReceivePackDrop(IReadOnlyList<string> files) => ShowPackImport(files[0]);
    private partial Features.Servers.ServerFavorite? ServerOwner { get; set; }
    private partial string ClientSelection { get; set; }

    internal InstancesPage(
        LauncherClient client,
        HomeController home,
        Window window,
        Action create,
        Action import,
        Action play,
        Features.Servers.ServerConnectionPresenter connections,
        Action<string> createServer,
        Action<string> importServer,
        Action<string, string> playServer,
        Action<string> resumeInstallation,
        InstanceOperationPresenter actions)
    {
        _client = client;
        _home = home;
        _window = window;
        _create = create;
        _import = import;
        _play = play;
        _createServer = createServer;
        _importServer = importServer;
        _playServer = playServer;
        _resumeInstallation = resumeInstallation;
        ClientSelection = string.Empty;
        _actions = actions;
        _rows = CreateRowStore();
        _servers = new(
            home,
            window,
            connections,
            OpenServerClients);
        _list = new(this);
        _runtime = new(client, home, window, () => RuntimeVisible = false);
        State = home.State;
        Viewport = window.ClientSize;
    }

    internal partial HomePageState State { get; private set; }
    internal IReadOnlyList<ManagedInstance> Rows
    {
        get
        {
            var ids = State.Instances.Where(instance => !instance.Unreadable && instance.ServerId == ServerOwner?.Id)
                .Select(instance => instance.Id).ToHashSet(StringComparer.Ordinal);
            var rows = RowStore.Rows;
            return rows.All(row => ids.Contains(row.Id)) ? rows : [.. rows.Where(row => ids.Contains(row.Id))];
        }
    }
    public partial Size Viewport { get; private set; }
    /// <summary>Gets the latest instance inventory read failure for the page placeholder.</summary>
    public string ReadError => State.InstanceLoadError.Length > 0 ? State.InstanceLoadError : RowStore.Error;
    private bool Loading => !State.InstancesLoaded || RowStore.Loading;
    private partial bool ServerMode { get; set; }
    private bool Compact => Viewport.Width < 1080f;
    private string EmptyMessage => ReadError.Length > 0
        ? string.Format(Resources.InstancesReadError, ReadError)
        : ServerOwner is not null ? Resources.ServerClientsEmpty : Resources.EmptyInstances;
    private string Heading => ServerOwner is { } owner ? string.Format(Resources.ServerClientsHeading, owner.Name) : Resources.NavInstances;
    private string CreateLabel => ServerMode
        ? Resources.ServerAdd
        : ServerOwner is not null ? Resources.ServerCreateClient : Resources.CreateInstance;
    private HomePageState ListState => ServerOwner is null ? State : State with
    {
        SelectedId = ClientSelection
    };

    internal static GridLength[] Columns(bool compact) => compact ? [GridLength.Star(1.1f), GridLength.Star(), GridLength.Pixels(160f)] : [GridLength.Star(1.2f), GridLength.Star(), GridLength.Pixels(94f), GridLength.Pixels(82f), GridLength.Pixels(192f)];

    private Ui Body => Overlay([
        Grid(
            [GridLength.Star()],
            [GridLength.Pixels(LauncherLayout.PageHeaderHeight), GridLength.Star()],
            [
                Grid(
                    [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Row([
                            LauncherControls.OutlineCommand(
                                Row([
                                    LauncherIcons.Line("back", 18f)
                                ]),
                                () => ChooseView(1))
                                .Style(LauncherButtons.PopupCloseButton)
                                .Width(32f).Height(32f)
                                .Visible(ServerOwner is not null)
                                .Automation(new()
                                {
                                    Name = Resources.ServersTab,
                                    AutomationId = "servers.clients.back"
                                }),
                            Text(Heading)
                                .FontSize(32f).FontWeight(700).Foreground(LauncherTheme.Ink)
                                .VAlign(VerticalAlignment.Bottom)
                                .Weight(1f)
                                .Automation(new()
                                {
                                    Name = Heading,
                                    AutomationId = "instances.heading"
                                }),
                        ])
                            .Spacing(12f)
                            .Cell(0, 0),
                        LauncherControls.Command(CreateLabel, () =>
                            {
                                if (ServerMode)
                                    _servers.Add();
                                else if (ServerOwner is { } owner)
                                        _createServer(owner.Id);
                                    else
                                        _create();
                            })
                            .VAlign(VerticalAlignment.Center)
                            .Automation(new()
                            {
                                Name = CreateLabel,
                                AutomationId = ServerMode ? "servers.add" : "instances.page.create"
                            })
                            .Cell(ServerMode ? 2 : 1, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                Text(ServerMode ? Resources.ServerDirect : Resources.PackImportMenu),
                                LauncherIcons.Line("chevron-down", 16f)
                                    .Visible(!ServerMode)
                            ])
                                .Spacing(8f),
                            () =>
                            {
                                if (ServerMode)
                                    _servers.Direct();
                                else
                                    ShowImportMenu();
                            })
                            .VAlign(VerticalAlignment.Center)
                            .Automation(new()
                            {
                                Name = ServerMode ? Resources.ServerDirect : Resources.PackImportMenu,
                                AutomationId = ServerMode ? "servers.direct" : "instances.page.import"
                            })
                            .Cell(ServerMode ? 1 : 2, 0),
                    ])
                    .ColumnSpacing(12f)
                    .VAlign(VerticalAlignment.Bottom)
                    .Cell(0, 0),
                DropTarget(Grid(
                    [GridLength.Star()],
                    [GridLength.Star(), GridLength.Auto],
                    [
                        Overlay([
                            new Controls.LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(1f),
                            Border(Grid(
                                [GridLength.Star()],
                                [GridLength.Auto, GridLength.Star()],
                                [
                                    Border(new InstanceViewSegments(
                                            ServerMode,
                                            ChooseView,
                                            Resources.InstancesLocal,
                                            Resources.ServersTab,
                                            LauncherMaterials.SegmentedMaterial))
                                        .Padding(16f, 12f)
                                        .BorderBrush(LauncherTheme.Edge)
                                        .BorderThickness(0f, 0f, 0f, 1f)
                                        .Visible(ServerOwner is null)
                                        .Cell(0, 0),
                                    Overlay([
                                        Border(Grid(
                                                [GridLength.Star()],
                                                [GridLength.Auto, GridLength.Star(), GridLength.Auto],
                                                [
                                                    new InstanceListHeading(Compact, InstanceListAppearance.Capture()).Cell(0, 0),
                                                    Overlay([
                                                        _list.WithRows(Rows, ListState, Compact),
                                                        new LoadingPlaceholderView(LauncherAppearance.Capture(), Resources.InstancesLoading, "instances.page.loading")
                                                            .Visible(Loading && Rows.Count == 0),
                                                        new EmptyPlaceholderView(
                                                            ReadError.Length > 0 ? "warning" : "instances",
                                                            EmptyMessage,
                                                            ReadError.Length > 0 ? string.Empty : Resources.HomeEmptyLocalHint,
                                                            ReadError.Length > 0 ? LauncherTheme.Error : LauncherTheme.Quiet,
                                                            ReadError.Length > 0 ? LauncherTheme.Error : LauncherTheme.EmptyPlaceholder,
                                                            "instances.page.empty").Margin(24f)
                                                            .Visible(!Loading && Rows.Count == 0),
                                                    ])
                                                        .Cell(0, 1),
                                                    Border(Text(string.Format(Resources.InstancesCount, Rows.Count))
                                                            .FontSize(12f).Foreground(LauncherTheme.Muted)
                                                            .Automation(new()
                                                            {
                                                                Name = string.Format(Resources.InstancesCount, Rows.Count),
                                                                AutomationId = "instances.page.count",
                                                                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                                            }))
                                                        .Padding(20f, 12f)
                                                        .BorderBrush(LauncherTheme.Edge)
                                                        .BorderThickness(0f, 1f, 0f, 0f)
                                                        .Cell(0, 2),
                                                ]))
                                            .BorderThickness(0f)
                                            .Visible(!ServerMode),
                                        _servers.Visible(ServerMode),
                                    ])
                                        .Cell(0, 1),
                                ]))
                            .BorderBrush(LauncherTheme.Edge)
                            .BorderThickness(1f),
                        ]).Cell(0, 0),
                        new FileDropHint(LauncherAppearance.Capture(),PackDropping, Resources.PackDropRelease, "instances.pack-drop")
                            .Visible(PackDropping && LocalPackTarget)
                            .Margin(new Thickness(0f, 8f, 0f, 0f))
                            .Cell(0, 1),
                    ]), PackDrop)
                    .Cell(0, 1),
            ])
            .RowSpacing(LauncherLayout.PageHeaderGap)
            .Visible(!RuntimeVisible),
        _runtime.Visible(RuntimeVisible)
    ]);

    internal void Activate(bool active)
    {
        _active = active;
        if (!active) PackDropping = false;
        _servers.Activate(active && ServerMode);
        if (active && !ServerMode)
            _ = RefreshRowsAsync();
    }

    internal void ShowServers() => ChooseView(1);

    internal void ShowLocal() => ChooseView(0);

    private void ChooseView(int index)
    {
        PackDropping = false;
        ServerOwner = null;
        ClientSelection = string.Empty;
        RuntimeVisible = false;
        ServerMode = index == 1;
        _servers.Activate(_active && ServerMode);
        if (_active && !ServerMode)
            _ = RefreshRowsAsync();
    }

    private void OpenServerClients(Features.Servers.ServerFavorite server)
    {
        if (!_serverRows.ContainsKey(server.Id))
            _serverRows[server.Id] = CreateRowStore();
        RuntimeVisible = false;
        ServerMode = false;
        ServerOwner = server;
        ClientSelection = server.PreferredInstanceId ?? string.Empty;
        _servers.Activate(false);
        _ = RefreshRowsAsync();
    }

    private void OnHomeChanged(HomePageState state)
    {
        State = state;
        if (_observedRevision != state.InstancesRevision)
        {
            _observedRevision = state.InstancesRevision;
            if (_active && !ServerMode)
                _ = RefreshRowsAsync();
        }
    }

    private InstanceListStore CreateRowStore() => new((summaries, progress, ct) => InstanceListStore.ReadAsync(_client, _home, summaries, progress, ct));

    private Task RefreshRowsAsync() => State.InstancesLoaded
        ? RowStore.EnsureAsync(State.InstancesRevision, [.. State.Instances.Where(instance => !instance.Unreadable && instance.ServerId == ServerOwner?.Id)])
        : Task.CompletedTask;

    private void ShowImportMenu()
    {
        _menu = new MenuFlyout(new MenuFlyoutItem(Resources.PackImportDirectory, () =>
                {
                    if (ServerOwner is { } owner)
                        _importServer(owner.Id);
                    else
                        _import();
                })
        {
            Automation = new()
            {
                Name = Resources.PackImportDirectory,
                AutomationId = "instances.import.directory"
            }
        }, new MenuFlyoutItem(Resources.PackImport, () => ShowPackDialog())
        {
            Automation = new()
            {
                Name = Resources.PackImport,
                AutomationId = "instances.import.pack"
            }
        }).Style(LauncherMenus.CommandMenu);
        _menu.ShowAt(_window);
    }

    internal void ShowPackImport(string? source = null)
    {
        ChooseView(0);
        ShowPackDialog(source: source);
    }

    private void ShowPackDialog(string? instanceId = null, string? source = null)
    {
        if (Controls.ModalPopup.IsOpen(_window))
            return;
        RuntimeVisible = false;
        if (ServerOwner is null)
            ChooseView(0);
        var controller = new Features.Packs.PackImportController(_client, _home, ServerOwner?.Id);
        if (source is not null)
        {
            controller.SetSource(source);
            _ = controller.InspectAsync();
        }
        else if (instanceId is not null)
        {
            _ = controller.OpenAsync(instanceId);
        }

        Features.Packs.PackImportDialog? content = null;
        _popup = new Flyout(close => content = new Features.Packs.PackImportDialog(controller, _window, close, Launch))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(Features.Packs.PackImportDialog.MaximumWidth)).Automation(new()
        {
            Name = Resources.PackImport,
            AutomationId = "pack.import.popup",
            IsDialog = true
        });
        _popup.Closing += e =>
        {
            e.Cancel = content?.CanClose == false;
            if (e.Cancel)
                content?.RequestClose();
        };
        _popup.Closed += () =>
        {
            if (content?.Succeeded == true)
                _restoreListFocus = true;
            else
                _restoreImportFocus = true;
        };
        Controls.ModalPopup.TryShow(_window, _popup);
    }

    internal void Select(string id)
    {
        if (ServerOwner is not null)
            ClientSelection = id;
        else
            _home.Select(id);
    }

    internal void RequestEntry(bool import)
    {
        ChooseView(0);
        if (!Controls.ModalPopup.IsOpen(_window))
            _entryRequested = import;
    }

    internal async void Launch(string id)
    {
        if (State.InstallationIncomplete(id))
        {
            _resumeInstallation(id);
            return;
        }

        var cancellation = _lifetime.Token;
        try
        {
            var owner = await Task.Run(() => _client.Instances.Get(id).ServerId, cancellation);
            if (cancellation.IsCancellationRequested)
                return;
            if (owner is not null)
            {
                _playServer(owner, id);
                return;
            }

            _play();
            await _home.LaunchCreatedAsync(id);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
                _home.ReportError(id, exception.Message);
        }
    }

    private async void LaunchNormally(string id)
    {
        await _home.StartLaunchAsync(new LaunchTask(id, new LaunchOptions { InheritServer = false }));
    }

    private async void ReleaseClient(string id)
    {
        try
        {
            await _client.Servers.SetClientOwnerAsync(id, null, ct: _lifetime.Token);
            await _home.RefreshAsync();
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
                Controls.LauncherToast.ShowError(_window, exception.Message);
        }
    }

    internal void ShowMenu(ManagedInstance item)
    {
        // A retained callback must still refer to an inventory member, never to a replacement row by index.
        if (Rows.FirstOrDefault(row => row.Id == item.Id) is not { } current)
            return;
        item = current;
        bool managed;
        try
        {
            managed = _client.Packs.IsManaged(item.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // External changes can race a valid inventory snapshot; refresh membership and expose the read failure.
            Controls.LauncherToast.ShowError(_window, exception.Message);
            _ = _home.RefreshAsync();
            return;
        }

        MenuFlyoutItem Command(
            string title,
            Action action,
            string suffix,
            bool enabled = true) => new(title, action)
            {
                IsEnabled = enabled,
                Automation = new()
                {
                    Name = string.Format(Resources.InstanceManageAction, title, item.Name),
                    AutomationId = "instances.menu." + suffix
                }
            };
        MenuFlyoutSubItem Group(string title, string suffix, params MenuFlyoutItemBase[] children) => new(title, children)
        {
            Automation = new()
            {
                Name = string.Format(Resources.InstanceManageAction, title, item.Name),
                AutomationId = "instances.menu." + suffix
            }
        };
        var delete = _actions.DeleteItem(item.Id, item.Name, RestoreOperationFocus);
        var tools = new List<MenuFlyoutItemBase>
        {
            Command(Resources.RuntimeHeading, () => OpenRuntime(item.Id), "runtime"),
            Command(Resources.BridgeTitle, () => ShowGameBridge(item.Id), "game-bridge"),
            Command(Resources.InstanceContentDetails, () => Open(item.Id, InstanceAction.Content), "content"),
        };
        if (managed)
        {
            tools.Add(new MenuFlyoutSeparator());
            tools.Add(Command(Resources.PackPreparation, () => ShowPackDialog(item.Id), "pack"));
            tools.Add(Command(Resources.PackUpdateHeading, () => ShowPackMaintenance(item.Id), "pack-update"));
        }

        var entries = new List<MenuFlyoutItemBase>
        {
            Command(State.InstallationIncomplete(item.Id)
                    ? Resources.CreateResumeInstallation
                    : item.Instance.ServerId is null ? Resources.LaunchGame : Resources.ServerConnect, () => Launch(item.Id), "launch", (State.InstallationIncomplete(item.Id) || item.CanLaunch) && !_home.State.PendingInstances.Contains(item.Id) && _home.State.LaunchStates.GetValueOrDefault(item.Id)?.Busy != true),
            Command(Resources.CurrentInstanceSettings, () => Open(item.Id, InstanceAction.Configure), "configure"),
            Command(Resources.MultiplayerCreate, () => CreateMultiplayer?.Invoke(item.Id), "multiplayer", CreateMultiplayer is not null && item.Instance.ServerId is null),
            new MenuFlyoutSeparator(),
            _actions.RenameItem(item.Id, item.Name, RestoreOperationFocus),
            _actions.IconItem(item.Id, item.Name, RestoreOperationFocus),
            Command(Resources.InstanceCopy, () => Open(item.Id, InstanceAction.Copy), "copy"),
        };
        if (item.Instance.ServerId is not null)
        {
            entries.Insert(1, Command(Resources.ServerPlainLaunch, () => LaunchNormally(item.Id), "local-launch", item.CanLaunch && !_home.State.PendingInstances.Contains(item.Id) && _home.State.LaunchStates.GetValueOrDefault(item.Id)?.Busy != true));
            entries.Add(Command(Resources.ServerReleaseClient, () => ReleaseClient(item.Id), "release-client"));
        }

        entries.AddRange([
            new MenuFlyoutSeparator(),
            Group(Resources.InstanceMenuTools, "tools", [.. tools]),
            Group(Resources.InstanceMenuBackups, "backups",
                Command(Resources.InstanceBackup, () => Open(item.Id, InstanceAction.Backup), "backup"),
                Command(Resources.InstanceRestore, () => Open(item.Id, InstanceAction.Restore), "restore"),
                new MenuFlyoutSeparator(),
                Command(Resources.InstanceExport, () => Open(item.Id, InstanceAction.Export), "export")),
            Group(Resources.InstanceMenuFolders, "folders",
                Command(Resources.InstanceGameFolder, () => OpenFolder(item, false), "game-folder"),
                Command(Resources.InstanceRuntimeFolder, () => OpenFolder(item, true), "runtime-folder")),
            new MenuFlyoutSeparator(),
            delete
        ]);

        _menu?.Hide();
        _menu = new MenuFlyout([.. entries]).Style(LauncherMenus.CommandMenu).MaxHeight(Math.Max(180f, Viewport.Height - 80f));
        _menu.Closed += delete.Dispose;
        _menu.ShowAt(_window);
    }

    private async void ShowGameBridge(string instanceId)
    {
        if (Controls.ModalPopup.IsOpen(_window) || _opening || _actions.Opening)
            return;
        _opening = true;
        var cancellation = _lifetime.Token;
        try
        {
            var (instance, enabled) = await Task.Run(() =>
            {
                var instance = _client.Instances.Get(instanceId);
                var enabled = _client.Settings.GetEffective(instance).Values["gameBridgeEnabled"]?.GetValue<bool>() == true;
                return (instance, enabled);
            }, cancellation);
            if (cancellation.IsCancellationRequested || Controls.ModalPopup.IsOpen(_window))
                return;
            _popup = new Flyout(close => new GameBridgeDialog(_client, instance, enabled, _window, close))
            {
                Placement = PopupPlacement.Full,
                IsModal = true,
                IsLightDismissEnabled = false
            }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
            {
                Name = Resources.BridgeTitle,
                AutomationId = "game.bridge.popup",
                IsDialog = true
            });
            _popup.Closed += () => _restoreListFocus = true;
            Controls.ModalPopup.TryShow(_window, _popup);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
            {
                Controls.LauncherToast.ShowError(_window, exception.Message);
                _ = _home.RefreshAsync();
            }
        }
        finally
        {
            _opening = false;
        }
    }

    private void ShowPackMaintenance(string instanceId)
    {
        if (Controls.ModalPopup.IsOpen(_window))
            return;
        var controller = new Features.Packs.PackMaintenanceController(_client, _home, instanceId);
        Features.Packs.PackMaintenanceDialog? content = null;
        _popup = new Flyout(close => content = new Features.Packs.PackMaintenanceDialog(controller, _window, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.MaxWidth(Features.Packs.PackMaintenanceDialog.MaximumWidth)).Automation(new()
        {
            Name = Resources.PackUpdateHeading,
            AutomationId = "pack.update.popup",
            IsDialog = true
        });
        _popup.Closing += args =>
        {
            args.Cancel = content?.CanClose == false;
            if (args.Cancel)
                content?.RequestClose();
        };
        _popup.Closed += () => _restoreListFocus = true;
        Controls.ModalPopup.TryShow(_window, _popup);
    }

    private void OpenFolder(ManagedInstance item, bool runtime)
    {
        try
        {
            Process.Start(new ProcessStartInfo(runtime ? item.Instance.MinecraftDirectory : item.Instance.GameDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Controls.LauncherToast.ShowError(_window, exception.Message);
        }
    }

    internal void Open(string id, InstanceAction action)
    {
        if (!_opening)
            _actions.Open(id, action, RestoreOperationFocus);
    }

    private void RestoreOperationFocus() => _restoreListFocus = true;

    private async void OpenRuntime(string id)
    {
        if (Controls.ModalPopup.IsOpen(_window))
            return;
        RuntimeVisible = true;
        await _runtime.OpenAsync(id);
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    private void OnLayoutUpdated()
    {
        if (_entryRequested is { } import && Interaction is { IsCollapsed: false } entryRoot)
        {
            var entryTarget = FocusTraversal.Next(entryRoot, current: null, forward: true);
            if (import && entryTarget is not null)
                entryTarget = FocusTraversal.Next(entryRoot, entryTarget, forward: true);
            if (entryTarget is not null && (_window.SetFocus(entryTarget, FocusState.Programmatic) || ReferenceEquals(_window.FocusedElement, entryTarget)))
            {
                _entryRequested = null;
                _window.RequestAnimationFrame(_ =>
                    {
                        if (_lifetime.IsCancellationRequested || Interaction is not { IsCollapsed: false } || Controls.ModalPopup.IsOpen(_window))
                        {
                            return;
                        }

                        if (import)
                            _import();
                        else
                            _create();
                    });
            }

            return;
        }

        if (_restoreImportFocus && _popup?.IsOpen != true && Interaction is { IsCollapsed: false } importRoot)
        {
            var create = FocusTraversal.Next(importRoot, current: null, forward: true);
            var importTarget = create is null ? null : FocusTraversal.Next(importRoot, create, forward: true);
            if (importTarget is { CanTakeFocus: true } && (_window.SetFocus(importTarget, FocusState.Programmatic) || ReferenceEquals(_window.FocusedElement, importTarget)))
                _restoreImportFocus = false;
            return;
        }

        if (!_restoreListFocus || ServerMode || RuntimeVisible || _opening || _actions.Opening || _popup?.IsOpen == true || Controls.ModalPopup.IsOpen(_window))
            return;
        if (Rows.Count > 0)
        {
            if (_list.RestoreSelectionFocus(_window))
                _restoreListFocus = false;
        }
        else if (Interaction is { } root && FocusTraversal.Next(root, current: null, forward: true) is { } target && (_window.SetFocus(target, FocusState.Programmatic) || ReferenceEquals(_window.FocusedElement, target)))
        {
            _restoreListFocus = false;
        }
    }

    protected override IDisposable? OnMounted()
    {
        _window.LayoutUpdated += OnLayoutUpdated;
        _home.StateChanged += OnHomeChanged;
        _window.SizeChanged += OnSizeChanged;
        OnHomeChanged(_home.State);
        return null;
    }

    protected override void OnDispose()
    {
        _window.LayoutUpdated -= OnLayoutUpdated;
        _home.StateChanged -= OnHomeChanged;
        _window.SizeChanged -= OnSizeChanged;
        _rows.Dispose();
        foreach (var rows in _serverRows.Values)
            rows.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _menu?.Hide();
        _popup?.Hide();
        base.OnDispose();
    }
}