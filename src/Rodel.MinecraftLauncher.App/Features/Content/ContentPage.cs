// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Worlds;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentPage : Control
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly ContentController _controller;
    private readonly ContentSearchController _search;
    private readonly ContentDownloads _downloads;
    private readonly ContentDownloadPanel _downloadPanel;
    internal bool HasActiveDownloads => _downloads.Active;
    private readonly ContentCollection _collection;
    private readonly ContentOnboarding _onboarding;
    private readonly ScrollView _onboardingScroll;
    internal WorldsPanel Worlds { get; }
    internal ScreenshotPanel Screenshots { get; }
    private readonly Action _manageInstances;
    private readonly Action _create;
    private readonly Action _import;
    private readonly PopupFocusRecovery _focus;
    private Flyout? _popup;
    private bool _active;
    private bool _searchOpen;
    private bool _disposed;
    private bool _requestingInstance;
    private bool _pickingFiles;
    private MenuFlyout? _toolsMenu;
    private InteractionNode? _searchReturnFocus;
    internal Window Window { get; }
    internal Flyout BundledModsFlyout { get; }
    internal ContentDropTarget DropTarget { get; }
    internal System.Numerics.Vector2 SearchOffset { get; set; }
    internal event Action? SearchRowsChanged;

    internal ContentPage(
        LauncherClient client,
        HomeController home,
        Window window,
        Action manageInstances,
        Action create,
        Action import,
        Action<Core.Worlds.Maps.WorldMapSelection> showWorldMap)
    {
        _client = client;
        _home = home;
        Window = window;
        _manageInstances = manageInstances;
        _create = create;
        _import = import;
        _controller = new(client, home);
        _search = new(client);
        _downloads = new(client, home);
        _downloadPanel = new(_downloads, window, entry => ShowDownload(entry, false));
        _downloads.Completed += OnDownloadCompleted;
        Worlds = new(
            client,
            home,
            _controller,
            window,
            ShowWorldOperation,
            showWorldMap);
        Screenshots = new(this, client);
        _focus = new(window);
        DropTarget = new(this);
        _collection = new(this);
        _onboarding = new(this);
        _onboardingScroll = new(_onboarding);
        State = _controller.State;
        Online = _search.State;
        Home = home.State;
        Viewport = window.ClientSize;
        Query = string.Empty;
        BundledModsFlyout = new Flyout(close => CreateBundledPicker(close))
        {
            Placement = PopupPlacement.BottomEdgeAlignedRight,
            ShouldConstrainToRootBounds = true,
        };
    }

    internal partial ContentState State { get; private set; }
    internal partial ContentSearchState Online { get; private set; }
    internal partial HomePageState Home { get; private set; }
    internal partial Size Viewport { get; private set; }
    internal partial string Query { get; private set; }
    internal partial ContentKind Kind { get; private set; }
    internal partial ContentKind? SearchKind { get; private set; }
    internal partial bool Dropping { get; private set; }
    private partial ContentViewMode ViewMode { get; set; }
    internal bool WorldMode => ViewMode == ContentViewMode.Worlds;
    internal bool ScreenshotMode => ViewMode == ContentViewMode.Screenshots;
    internal bool InstalledMode => ViewMode == ContentViewMode.Installed;
    internal partial ContentSortColumn SortColumn { get; private set; }
    internal partial bool SortDescending { get; private set; }

    internal void ChooseSort(ContentSortColumn column)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
    }

    private partial bool ActionDialogOpen { get; set; }
    private partial bool SelectedInstanceDownloading { get; set; }
    internal bool Compact => Viewport.Width < 1080f;
    internal bool HasTarget => Home.Selected is { Unreadable: false };
    internal bool NoInstances => Home.InstancesLoaded && Home.InstanceLoadError.Length == 0 && Home.Instances.Count == 0;
    internal bool QueryActive => Query.Trim().Length > 0;
    internal bool CanChange => !SelectedInstanceDownloading && InstalledMode && HasTarget && State.InstanceId == Home.SelectedId && State.Kind == Kind && !State.Active && !State.Loading && !Home.Pending && !Home.CurrentLaunch.Busy && Home.LaunchObserved && Home.LaunchReadError.Length == 0;
    internal bool CanDrop => !SelectedInstanceDownloading && _active && !ScreenshotMode && HasTarget && !State.Active && !_pickingFiles && (!WorldMode || Worlds.Writable) && !ModalPopup.IsOpen(Window);
    internal bool CanCheckUpdates => !SelectedInstanceDownloading && HasTarget && InstalledMode && !State.Active && !State.Loading && State.InstanceId == Home.SelectedId && State.Kind == Kind;
    internal string ChangeBlocked => ScreenshotMode ? string.Empty : Home.LaunchReadError.Length > 0
        ? Resources.LaunchObservationFailed
        : Home.Selected is not null && (Home.Pending || Home.CurrentLaunch.Busy) && !State.Active ? Resources.InstanceBusyConflict : string.Empty;
    internal string SelectionNotice => State.SelectionError.Length > 0
        ? string.Format(Resources.ContentSelectionReadFailed, State.SelectionError)
        : Kind == ContentKind.Shaders && State.ShaderSelection is { Engine: null }
        ? Resources.ContentShaderEngineMissing
        : Kind == ContentKind.Shaders && State.ShaderSelection is { Selected: not null, Present: false }
        ? Resources.ContentShaderSelectionMissing
        : Kind == ContentKind.ResourcePacks && State.ResourceSelection?.Pending is not null ? Resources.ContentPackPending : string.Empty;
    internal InstanceSummary[] Instances => [.. Home.Instances.Where(item => !item.Unreadable)];
    internal ContentItem[] LocalMatches => [.. State.Items.Where(item => item.Name.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase) || item.FileName.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase))];
    internal IReadOnlyList<ContentRowState> Rows => BuildRows(false);
    internal IReadOnlyList<ContentRowState> SearchRows => BuildRows(true);

    private List<ContentRowState> BuildRows(bool searching)
    {
        var appearance = ContentRowAppearance.Capture();
        var prefix = Home.SelectedId + ":" + (searching ? "search" : Kind.Directory()) + ":";
        ContentRowState Row(string key) => new(
            prefix + key,
            appearance,
            Home.SelectedId,
            Kind,
            CanChange,
            Compact,
            Window.FlowDirection == FlowDirection.RightToLeft);
        var rows = new List<ContentRowState>();
        if (searching && !QueryActive)
            return [];
        if (searching && Online.Local.Count > 0)
        {
            rows.Add(Row("local") with { Heading = string.Format(Resources.ContentLocalHeading, Online.Local.Count) });
            rows.AddRange(Online.Local.Select(match => Row("file:" + match.Kind + ":" + match.Item.FileName) with
            {
                Local = match.Item,
                Kind = match.Kind,
                SearchOnly = true
            }));
        }

        if (!searching)
        {
            rows.AddRange(ContentSorting.Order(State.Items, SortColumn, SortDescending).Select(item => Row("file:" + item.Key) with { Local = item }));
        }

        if (searching && State.ReadError.Length > 0)
            rows.Add(Row("read-error") with { Message = string.Format(Resources.ContentListFailed, State.ReadError) });
        if (searching)
        {
            if (Online.LocalError.Length > 0)
                rows.Add(Row("local-read-error") with { Message = Online.LocalError });
            rows.Add(Row("online") with { Heading = string.Format(Resources.ContentOnlineHeading, SearchKind?.Title() ?? Resources.ContentAllTypes) });
            rows.AddRange(Online.Hits.Select(hit => Row("project:" + hit.Id) with
            {
                Remote = hit,
                Kind = hit.Kind,
                CanChange = !State.Active && (!HasTarget || (!Home.Pending && !Home.CurrentLaunch.Busy)),
                Installed = Online.InstalledProjects.Contains(hit.Id),
            }));
            if (Online.Notice == ContentSearchNoticeKind.Failed)
                rows.Add(Row("search-error") with { SearchNotice = new(ContentSearchNoticeKind.Failed, Online.Error) });
            if (Online.Notice == ContentSearchNoticeKind.Empty)
                rows.Add(Row("no-results") with { SearchNotice = new(ContentSearchNoticeKind.Empty) });
        }

        return rows;
    }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Pixels(LauncherLayout.PageHeaderHeight), GridLength.Auto, GridLength.Star()],
        [
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Text(Resources.NavContent)
                        .FontSize(32f).FontWeight(700).Foreground(LauncherTheme.Ink)
                        .VAlign(VerticalAlignment.Bottom)
                        .Automation(new()
                        {
                            Name = Resources.NavContent,
                            AutomationId = "content.heading"
                        })
                        .Cell(0, 0),
                    Row([
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("search", 18f),
                                Text(Resources.ContentUnifiedSearch)
                            ])
                                .Spacing(8f),
                            () => ShowSearch())
                            .MinHeight(40f)
                            .VAlign(VerticalAlignment.Center)
                            .Enabled(!State.Active)
                            .Automation(new()
                            {
                                Name = Resources.ContentUnifiedSearch,
                                AutomationId = "content.search.open"
                            }),
                        LauncherControls.Command(WorldMode ? Resources.WorldImport : Resources.ContentFromFiles, () => ShowImport())
                            .MinHeight(40f)
                            .VAlign(VerticalAlignment.Center)
                            .Enabled(!State.Active && (!WorldMode || Worlds.Writable))
                            .Visible(HasTarget)
                            .Automation(new()
                            {
                                Name = WorldMode ? Resources.WorldImport : Resources.ContentFromFiles,
                                AutomationId = "content.import"
                            }),
                        LauncherControls.Command(
                            Row([
                                LauncherIcons.Line("refresh", 18f)
                            ]),
                            Refresh)
                            .Style(LauncherButtons.IconButton.Padding(0f))
                            .Width(40f).Height(40f)
                            .Visible(HasTarget && WorldMode)
                            .Enabled(!State.Active)
                            .ToolTip(Resources.ContentRefresh)
                            .Automation(new()
                            {
                                Name = Resources.ContentRefresh,
                                AutomationId = "content.refresh"
                            }),
                        DropDownButton(
                            new ContentToolsIcon(),
                            CreateToolsMenu())
                            .Style(default(DropDownButtonStyle).Button(LauncherButtons.IconButton.Padding(0f)).ChevronVisible(false))
                            .Width(40f).Height(40f)
                            .Visible(HasTarget && InstalledMode)
                            .Enabled(!State.Active)
                            .ToolTip(Resources.ContentUpdatesMore)
                            .Automation(new()
                            {
                                Name = Resources.ContentUpdatesMore,
                                AutomationId = "content.more"
                            }),
                    ])
                        .Spacing(12f)
                        .Visible(!ScreenshotMode)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(ScreenshotMode ? 0f : 12f)
                .VAlign(VerticalAlignment.Bottom)
                .Cell(0, 0),
            Grid(
                [GridLength.Star(), GridLength.Auto, GridLength.Auto],
                [GridLength.Auto],
                [
                    Border(Row([
                            LauncherIcons.Line("success", 20f, LauncherTheme.SuccessForeground)
                                .VAlign(VerticalAlignment.Center)
                                .Visible(State.Outcome == "succeeded"),
                            Column([
                                Text(State.Status)
                                    .FontSize(12f).Foreground(State.Failure is not null ? LauncherTheme.Error : LauncherTheme.Ink)
                                    .Wrap()
                                    .Automation(new()
                                    {
                                        Name = State.Status,
                                        AutomationId = "content.status",
                                        LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                    }),
                                Text(State.Progress)
                                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                                    .Visible(State.Active && State.Progress.Length > 0),
                                Sprout.Markup.ProgressBar()
                                    .Indeterminate(State.Percent is null)
                                    .Value(State.Percent ?? 0)
                                    .Thickness(3f)
                                    .Visible(State.Active),
                            ])
                                .Spacing(4f)
                                .Weight(1f)
                                .Shrink(1f)
                                .MinContentWidth(0f)
                                .VAlign(VerticalAlignment.Center),
                        ])
                            .Spacing(12f))
                        .Padding(State.Outcome == "succeeded" ? 12f : 0f)
                        .Background(State.Outcome == "succeeded" ? LauncherTheme.SuccessBackground : Colors.Transparent)
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(Resources.ContentOperationDetails, () => ShowReport())
                        .Visible(State.HasActionDetails && !State.Active)
                        .Automation(new()
                        {
                            Name = Resources.ContentOperationDetails,
                            AutomationId = "content.details"
                        })
                        .Cell(1, 0),
                    LauncherControls.OutlineCommand(Resources.DialogCancel, _controller.Cancel)
                        .Visible(State.Active)
                        .Enabled(!State.CancelRequested)
                        .Automation(new()
                        {
                            Name = Resources.DialogCancel,
                            AutomationId = "content.cancel"
                        })
                        .Cell(2, 0),
                ])
                .ColumnSpacing(State.Active || State.HasActionDetails ? 12f : 0f)
                .Visible(!ScreenshotMode && State.Status.Length > 0 && (!State.InlineChange || State.Failure is not null || State.Outcome == "canceled") && !ActionDialogOpen)
                .Margin(new Thickness(0f, LauncherLayout.PageHeaderGap, 0f, 0f))
                .Cell(0, 1),
            Overlay([
                _collection.Visible(HasTarget),
                _onboardingScroll.Automation(new() { AutomationId = "content.onboarding.viewport" })
                    .Visible(!HasTarget),
                _downloadPanel.HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom).Margin(16f)
            ])
                .Margin(new Thickness(0f, LauncherLayout.PageHeaderGap, 0f, 0f))
                .Cell(0, 2),
        ]);

    internal void Activate(bool active)
    {
        if (_active == active)
            return;
        _active = active;
        Screenshots.Activate(active && ScreenshotMode);
        Dropping = false;
        if (!active)
        {
            _search.Suspend();
            _downloadPanel.Hide();
            _toolsMenu?.Hide();
            _popup?.Hide();
            BundledModsFlyout.Hide();
            return;
        }

        var previous = ScreenshotMode ? Screenshots.InstanceId : State.InstanceId;
        OnHomeChanged(_home.State);
        if (ScreenshotMode)
        {
            if (previous == Screenshots.InstanceId) _ = Screenshots.SelectAsync(Home.SelectedId, true);
            return;
        }

        if (previous == State.InstanceId)
            _ = _controller.RefreshAsync();
    }

    internal void ChooseInstance(int index)
    {
        var instances = Instances;
        if (!State.Active && !Screenshots.Busy && index >= 0 && index < instances.Length && instances[index].Id != Home.SelectedId)
        {
            Query = string.Empty;
            _home.Select(instances[index].Id);
        }
    }

    internal void ChooseKind(ContentKind kind)
    {
        BundledModsFlyout.Hide();
        if (State.Active || Screenshots.Busy || (Kind == kind && InstalledMode))
            return;
        Screenshots.Activate(false);
        ViewMode = ContentViewMode.Installed;
        Kind = kind;
        Dropping = false;
        _ = _controller.SelectAsync(Home.SelectedId, kind);
    }

    internal void ChooseWorlds()
    {
        BundledModsFlyout.Hide();
        if (State.Active || Screenshots.Busy)
            return;
        Screenshots.Activate(false);
        ViewMode = ContentViewMode.Worlds;
        Dropping = false;
        if (State.InstanceId != Home.SelectedId) _ = _controller.SelectAsync(Home.SelectedId, Kind);
        _ = Worlds.SelectAsync(Home.SelectedId, force: true);
    }

    internal void ChooseScreenshots()
    {
        BundledModsFlyout.Hide();
        if (State.Active || Screenshots.Busy) return;
        ViewMode = ContentViewMode.Screenshots;
        Dropping = false;
        _search.Suspend();
        _ = Screenshots.SelectAsync(Home.SelectedId, true);
        Screenshots.Activate(_active);
    }

    internal void ChangeQuery(string query)
    {
        Query = query;
        SearchOffset = default;
        _ = _search.SearchAsync(Home.SelectedId, SearchKind, query);
    }

    internal void ChooseSearchKind(ContentKind? kind)
    {
        SearchKind = kind;
        SearchOffset = default;
        _ = _search.SearchAsync(Home.SelectedId, SearchKind, Query);
    }

    internal void LoadMore()
    {
        if (_searchOpen && !_disposed && Online.CanLoadMore)
            _ = _search.SearchAsync(Home.SelectedId, SearchKind, Query, append: true);
    }

    internal void Refresh()
    {
        if (State.Active)
            return;
        if (ScreenshotMode)
        {
            _ = Screenshots.SelectAsync(Home.SelectedId, true);
            return;
        }

        _ = _controller.RefreshAsync();
        if (WorldMode)
            _ = Worlds.SelectAsync(Home.SelectedId, force: true);
        if (_searchOpen)
            _ = _search.SearchAsync(Home.SelectedId, SearchKind, Query);
    }

    private void OnDownloadsChanged() => SelectedInstanceDownloading = _downloads.HasActive(Home.SelectedId);

    private void OnHomeChanged(HomePageState state)
    {
        SelectedInstanceDownloading = _downloads.HasActive(state.SelectedId);
        if (Home.SelectedId != state.SelectedId)
            BundledModsFlyout.Hide();
        if (!ReferenceEquals(Home.Instances, state.Instances) || Home.InstancesLoaded != state.InstancesLoaded || Home.SelectedId != state.SelectedId || Home.Pending != state.Pending || Home.CurrentLaunch.Busy != state.CurrentLaunch.Busy || Home.Details != state.Details || Home.LaunchObserved != state.LaunchObserved || Home.LaunchReadError != state.LaunchReadError || Home.InstanceLoadError != state.InstanceLoadError)
        {
            Home = state;
        }

        if (!_active)
            return;
        var id = state.Selected is { Unreadable: false } ? state.SelectedId : string.Empty;
        if (ScreenshotMode)
        {
            _ = Screenshots.SelectAsync(id);
            return;
        }

        if (WorldMode)
            _ = Worlds.SelectAsync(id);
        _ = _controller.SelectAsync(id, Kind);
        if (Online.InstanceId != id)
        {
            if (_searchOpen)
                _ = _search.SearchAsync(id, SearchKind, Query);
        }
    }

    internal async void SetEnabled(
        string id,
        ContentKind kind,
        ContentItem item,
        bool enabled)
    {
        var selected = kind == ContentKind.Mods ? item.Enabled : item.InUse;
        if (CanChange && id == State.InstanceId && kind == Kind && selected.HasValue && enabled != selected.Value)
        {
            var returnFocus = Window.FocusedElement;
            var action = kind == ContentKind.Mods
                ? enabled ? ContentAction.Enable : ContentAction.Disable
                : enabled ? ContentAction.Use : ContentAction.StopUsing;
            await _controller.ExecuteAsync(id, action, item.FileName, kind);
            _focus.Request(this, returnFocus);
        }
    }

    internal async void MoveResourcePack(ContentRowState row, bool higher)
    {
        if (!CanChange || row.Local?.InUse != true || row.Kind != ContentKind.ResourcePacks || row.InstanceId != State.InstanceId)
            return;
        var focus = Window.FocusedElement;
        await _controller.ExecuteAsync(row.InstanceId, higher ? ContentAction.RaisePriority : ContentAction.LowerPriority, row.Local.FileName, row.Kind);
        _focus.Request(this, focus);
    }

    internal void ChooseShaderEngine(int index)
    {
        if (State.ShaderSelection is { } selection && index >= 0 && index < selection.AvailableEngines.Count)
            _ = _controller.ChooseShaderEngineAsync(selection.AvailableEngines[index]);
    }

    internal void ShowRemove(string id, ContentKind kind, ContentItem item)
    {
        if (CanChange && id == State.InstanceId && kind == Kind)
            ShowAction(ContentAction.Remove, item);
    }

    private MenuFlyout CreateToolsMenu()
    {
        var menu = new MenuFlyout { Placement = PopupPlacement.BottomEdgeAlignedRight }.Style(LauncherMenus.CommandMenu);
        var entries = new List<MenuFlyoutItemBase>
        {
            new MenuFlyoutItem(Resources.ContentRefresh, () =>
                {
                    menu.Hide();
                    Refresh();
                })
            {
                Automation = new()
                {
                    Name = Resources.ContentRefresh,
                    AutomationId = "content.refresh"
                }
            },
        };
        if (InstalledMode)
        {
            entries.Add(new MenuFlyoutItem(Resources.ContentUpdatesCheckCategory, () =>
                    {
                        menu.Hide();
                        ShowUpdates(State.Items);
                    })
            {
                IsEnabled = CanCheckUpdates && State.Items.Count > 0,
                Automation = new()
                {
                    Name = Resources.ContentUpdatesCheckCategory,
                    AutomationId = "content.updates.check"
                }
            });
        }

        if (InstalledMode && Kind == ContentKind.Mods)
        {
            entries.Add(new MenuFlyoutItem(Resources.DependencyAnalysisTitle, () =>
                    {
                        menu.Hide();
                        ShowDependencyAnalysis(Window.FocusedElement);
                    })
            {
                IsEnabled = HasTarget && !State.Loading && State.InstanceId == Home.SelectedId,
                Automation = new()
                {
                    Name = Resources.DependencyAnalysisTitle,
                    AutomationId = "content.analysis.open"
                }
            });
        }

        foreach (var entry in entries)
            menu.Items.Add(entry);
        menu.Opened += () => _toolsMenu = menu;
        menu.Closed += () =>
        {
            if (ReferenceEquals(_toolsMenu, menu))
                _toolsMenu = null;
        };
        return menu;
    }

    internal void ShowItemMenu(ContentRowState row) => _collection.ShowContextMenu(row);

    private void ShowDependencyAnalysis(InteractionNode? returnFocus)
    {
        if (!HasTarget || ModalPopup.IsOpen(Window))
            return;
        var id = Home.SelectedId;
        var name = Home.Selected!.Name;
        ContentDependencyReport? report = null;
        ShowPopup(
            close => report = new(
                _client,
                Window,
                id,
                name,
                close),
            () => report?.CanClose != false,
            Resources.DependencyAnalysisTitle,
            "content.analysis.popup",
            returnFocus: returnFocus);
    }

    internal void ShowUpdates(IReadOnlyList<ContentItem> items, InteractionNode? returnFocus = null)
    {
        if (!CanCheckUpdates || items.Count == 0 || ModalPopup.IsOpen(Window))
            return;
        var id = Home.SelectedId;
        var name = Home.Selected!.Name;
        var kind = Kind;
        _controller.ClearOutcome();
        ContentUpdatesDialog? dialog = null;
        ShowPopup(
            close => dialog = new(
                _client,
                _controller,
                Window,
                id,
                name,
                kind,
                items,
                close),
            () => dialog?.CanClose != false,
            Resources.ContentUpdatesTitle,
            "content.updates.popup",
            returnFocus: returnFocus,
            requestClose: () => dialog?.RequestClose());
    }

    internal async void ConfigurePolicy(
        string id,
        ContentKind kind,
        ContentItem item,
        bool? pinned,
        string? channel)
    {
        if (!CanChange || id != Home.SelectedId || kind != Kind)
            return;
        await _controller.ConfigurePolicyAsync(
            id,
            kind,
            item.FileName,
            pinned,
            channel);
        _focus.RequestIfLost(this);
    }

    internal async void ShowImport()
    {
        if (ScreenshotMode) return;
        if (WorldMode)
        {
            ShowWorldOperation(WorldAction.Import, null, null);
            return;
        }

        if (!HasTarget || State.Active || _pickingFiles || ModalPopup.IsOpen(Window))
            return;
        var instanceId = Home.SelectedId;
        var instanceName = Home.Selected!.Name;
        var returnFocus = Window.FocusedElement;
        _pickingFiles = true;
        try
        {
            var picker = Window.CreateFileOpenPicker();
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            if (!_disposed && _active && files is { Count: > 0 })
                ShowFileAdd([.. files.Select(file => file.Path)], instanceId);
        }
        catch (Exception exception)
        {
            if (!_disposed)
                _controller.ReportError(exception, instanceName);
        }
        finally
        {
            _pickingFiles = false;
            if (!_disposed && !ModalPopup.IsOpen(Window))
                _focus.Request(this, returnFocus);
        }
    }

    private BundledModsPicker CreateBundledPicker(Action close)
    {
        var instanceId = Home.SelectedId;
        return new BundledModsPicker(State.BundledMods, State.BundledModsError, selected =>
            {
                var modIds = selected.ToArray();
                // Keep the invoked popup node alive until its native accessibility action returns.
                Window.RequestAnimationFrame(frame =>
                    {
                        if (_disposed || !CanChange || Home.SelectedId != instanceId)
                        {
                            close();
                            return;
                        }

                        close();
                        _ = _controller.InstallBundledModsAsync(instanceId, modIds);
                    });
            });
    }

    private void ShowWorldOperation(WorldAction action, WorldItem? world, string? source)
    {
        if (!HasTarget || State.Active || ModalPopup.IsOpen(Window))
            return;
        if (world is not null && world.InstanceId != Home.SelectedId)
        {
            _controller.ReportError(new LauncherOperationException("WORLD_TARGET_CHANGED", "The selected instance changed; reopen the world operation."), Home.Selected?.Name ?? string.Empty);
            return;
        }

        _controller.ClearOutcome();
        WorldOperationDialog? dialog = null;
        ShowPopup(
            close => dialog = new(
                _client,
                _controller,
                Window,
                Home.SelectedId,
                action,
                world,
                source,
                close),
            () => dialog?.CanClose != false,
            WorldText.Action(action),
            "world.operation.popup",
            () => _ = Worlds.SelectAsync(Home.SelectedId, force: true),
            requestClose: () => dialog?.RequestClose());
    }

    private void ShowFileAdd(IReadOnlyList<string> sources, string? instanceId = null)
    {
        if (State.Active || ModalPopup.IsOpen(Window))
            return;
        if (!HasTarget)
        {
            CreateInstance();
            return;
        }

        _controller.ClearOutcome();
        ContentFilesDialog? dialog = null;
        ShowPopup(close => dialog = new(
                _client,
                _controller,
                _home,
                Window,
                instanceId ?? Home.SelectedId,
                sources,
                close), () => dialog?.CanClose != false, Resources.ContentFromFiles, "content.files.popup", requestClose: () => dialog?.RequestClose());
    }

    private void RequestInstance()
    {
        _requestingInstance = true;
        _popup?.Hide();
        Window.RequestAnimationFrame(_ =>
            {
                _create();
                _requestingInstance = false;
            });
    }

    private void ShowAction(ContentAction action, ContentItem? item)
    {
        if (!CanChange || Home.Selected is not { } instance || ModalPopup.IsOpen(Window))
            return;
        _controller.ClearOutcome();
        ContentActionDialog? dialog = null;
        ShowPopup(close => dialog = new(
                _controller,
                Window,
                instance.Id,
                instance.Name,
                action,
                item,
                close), () => dialog?.CanClose != false, action == ContentAction.Import ? Resources.ContentImport : Resources.ContentRemove, "content.operation.popup", requestClose: () => dialog?.RequestClose());
    }

    internal void ShowInstall(ContentSearchHit hit)
    {
        if (State.Active || !_searchOpen || _popup is not { } searchPopup) return;
        var instance = Home.Selected;
        var entry = _downloads.Create(hit, Online, new(instance?.Id ?? string.Empty, instance?.Name ?? string.Empty));
        searchPopup.Hide();
        Window.RequestAnimationFrame(_ => ShowDownload(entry, true));
    }

    private void ShowDownload(ContentDownload entry, bool returnToSearch)
    {
        if (_disposed || !_active || ModalPopup.IsOpen(Window)) return;
        entry.HasDialog = true;
        ContentInstallDialog? dialog = null;
        ShowPopup(close => dialog = new(_downloads, entry, Window, close, _home, RequestInstance),
            () => dialog?.CanClose != false,
            string.Format(Resources.ContentInstallTitle, entry.Project.Name), "content.install.popup",
            () =>
            {
                entry.HasDialog = false;
                _downloads.Release(entry);
                Window.RequestAnimationFrame(frame =>
                {
                    if (!_disposed && _active && !_requestingInstance && returnToSearch)
                    {
                        OpenSearch(true);
                        _ = _search.RefreshInstalledAsync(Home.SelectedId);
                    }
                });
            }, requestClose: () => dialog?.RequestClose());
    }

    private void OnDownloadCompleted(ContentDownload entry)
    {
        if (_disposed) return;
        if (State.InstanceId == entry.Install.Target.Id) _ = _controller.RefreshAsync();
        if (_searchOpen && Home.SelectedId == entry.Install.Target.Id) _ = _search.RefreshInstalledAsync(Home.SelectedId);
    }

    internal void ShowSearch() => OpenSearch(false);

    internal void ViewInstalled(ContentKind kind)
    {
        _popup?.Hide();
        Window.RequestAnimationFrame(_ =>
            {
                if (!_disposed)
                    ChooseKind(kind);
            });
    }

    internal async Task ShowDependencyRepairAsync(string id, string sessionId)
    {
        if (_disposed || State.Active || Home.SelectedId != id)
            return;
        Kind = ContentKind.Mods;
        await _controller.SelectAsync(id, Kind);
        if (_disposed || !_active || Home.SelectedId != id)
            return;
        Window.RequestAnimationFrame(_ =>
            {
                if (_disposed || !_active || Home.SelectedId != id || ModalPopup.IsOpen(Window))
                    return;
                _controller.ClearOutcome();
                DependencyRepairDialog? dialog = null;
                ShowPopup(close => dialog = new(
                        _client,
                        _controller,
                        Window,
                        id,
                        sessionId,
                        Home.Selected!.DisplayName,
                        close), () => dialog?.CanClose != false, Resources.DependencyRepairTitle, "content.repair.popup", requestClose: () => dialog?.RequestClose());
            });
    }

    private void OpenSearch(bool returning)
    {
        if (State.Active || ModalPopup.IsOpen(Window))
            return;
        if (!returning)
        {
            _searchReturnFocus = Window.FocusedElement;
            if (SearchKind is not null)
                SearchOffset = default;
            SearchKind = null;
        }

        _searchOpen = true;
        Dropping = false;
        ContentSearchPopup? content = null;
        ShowPopup(
            close => content = new ContentSearchPopup(this, close),
            () => true,
            Resources.ContentUnifiedSearch,
            "content.search.popup",
            () =>
            {
                _searchOpen = false;
                _search.Suspend();
                _focus.Request(this, _searchReturnFocus);
            },
            () => content?.FocusSearch());
        if (!ActionDialogOpen)
            _searchOpen = false;
        else if (QueryActive && (Online.InstanceId != Home.SelectedId || Online.Kind != SearchKind || Online.Hits.Count == 0 || Online.Error.Length > 0))
            _ = _search.SearchAsync(Home.SelectedId, SearchKind, Query);
    }

    internal void ShowPopup(
        Func<Action, Control> content,
        Func<bool> canClose,
        string title,
        string id,
        Action? closed = null,
        Action? opened = null,
        InteractionNode? returnFocus = null,
        Action? requestClose = null)
    {
        returnFocus ??= Window.FocusedElement;
        var popup = new Flyout(close => content(close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = title,
            AutomationId = id,
            IsDialog = true
        });
        _popup = popup;
        if (opened is not null)
            popup.Opened += opened;
        popup.Closing += e =>
        {
            e.Cancel = !canClose();
            if (e.Cancel)
                requestClose?.Invoke();
        };
        popup.Closed += () =>
        {
            if (ReferenceEquals(_popup, popup))
                _popup = null;
            ActionDialogOpen = false;
            _focus.Request(this, returnFocus);
            closed?.Invoke();
        };
        ActionDialogOpen = ModalPopup.TryShow(Window, popup);
    }

    private void ShowReport()
    {
        if (!State.HasActionDetails || State.Active || ModalPopup.IsOpen(Window))
            return;
        var snapshot = State;
        DetailReportPopup? report = null;
        ShowPopup(close =>
            {
                report = new(
                    Window,
                    Resources.ContentOperationDetails,
                    snapshot.ReadableReport,
                    close,
                    Math.Min(560f, Window.ClientSize.Width - 72f),
                    "content.report");
                report.SetReport(snapshot.ReadableReport, snapshot.RawReport);
                return report;
            }, () => report?.Exporting != true, Resources.ContentOperationDetails, "content.report.popup");
    }

    internal void SetDropping(bool value) => Dropping = value;

    internal void DropFiles(IReadOnlyList<string> files)
    {
        if (!CanDrop || files.Count == 0)
            return;
        if (WorldMode)
        {
            if (files.Count != 1)
                _controller.ReportError(new ArgumentException(Resources.WorldSingleSource), Home.Selected?.Name ?? string.Empty);
            else
                ShowWorldOperation(WorldAction.Import, null, files[0]);
        }
        else
        {
            ShowFileAdd(files);
        }
    }

    internal void ManageInstances() => _manageInstances();

    internal void CreateInstance() => _create();

    internal void ImportInstance() => _import();

    private void OnChanged(ContentState state)
    {
        State = state;
        SearchRowsChanged?.Invoke();
    }

    private void OnSearchChanged(ContentSearchState state)
    {
        Online = state;
        SearchRowsChanged?.Invoke();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    private void OnOnboardingLayout()
    {
        if (_onboardingScroll.Interaction is not { IsCollapsed: false })
            return;
        var height = _onboardingScroll.ViewportSize.Height;
        if (Math.Abs(_onboarding.ViewportHeight - height) > .1f)
            _onboarding.ViewportHeight = height;
    }

    protected override IDisposable? OnMounted()
    {
        _downloads.Changed += OnDownloadsChanged;
        _controller.Changed += OnChanged;
        _search.Changed += OnSearchChanged;
        _home.StateChanged += OnHomeChanged;
        Window.SizeChanged += OnSizeChanged;
        Window.LayoutUpdated += OnOnboardingLayout;
        OnHomeChanged(_home.State);
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        Screenshots.Activate(false);
        _downloads.Changed -= OnDownloadsChanged;
        _controller.Changed -= OnChanged;
        _search.Changed -= OnSearchChanged;
        _home.StateChanged -= OnHomeChanged;
        Window.SizeChanged -= OnSizeChanged;
        Window.LayoutUpdated -= OnOnboardingLayout;
        _controller.Dispose();
        _search.Dispose();
        _downloads.Completed -= OnDownloadCompleted;
        _downloads.Dispose();
        _popup?.Hide();
        _focus.Dispose();
        BundledModsFlyout.Hide();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct ContentToolsIcon
{
    /// <summary>Provides the line icon for the native content tools dropdown.</summary>
    public Ui Body => Row([
        LauncherIcons.Line("more", 18f)
    ]);
}