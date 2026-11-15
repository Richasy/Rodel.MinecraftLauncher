// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Ai;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Owns settings navigation and edits; the shell only places the page and applies window lifecycle.</summary>
internal sealed partial class SettingsPage : Control
{
    private readonly CorePreferencesStore _core;
    internal readonly CorePreferencesForm _launch;
    internal readonly CorePreferencesForm _java;
    private readonly JavaManagerPage _javaManager;
    internal readonly CorePreferencesForm _network;
    internal readonly CorePreferencesForm _logs;
    internal readonly CorePreferencesForm _advanced;
    internal readonly GeneralSettingsSection _general;
    internal readonly AboutSettingsSection _about;
    private readonly SettingsNavigation _navigation;
    private readonly SettingsSearchBox _search;
    internal readonly AiSettingsSection AiSection;
    private readonly SettingsPageContent _content;
    private string? _pendingSearchTarget;
    private int _searchLayoutAttempts;
    private partial int SearchNoticeKind { get; set; }
    internal string SearchNotice => SearchNoticeKind == 1
        ? Resources.SettingsSearchConditional
        : SearchNoticeKind == 2
        ? Resources.SettingsSearchUnavailable
        : SearchNoticeKind == 3 ? Resources.SettingsProxyAddressNotice : string.Empty;
    internal readonly ContentMaintenanceSection _maintenance;
    internal Window Window { get; }
    internal AppSettings AppPreferences { get; }
    internal Features.Multiplayer.MultiplayerController Multiplayer { get; }
    internal readonly DataDirectorySection DataDirectory;
    internal readonly LanguageSection Language;
    internal readonly WallpaperSection Wallpapers;
    internal readonly ImageCacheSection ImageCache;

    internal SettingsPage(
        LauncherClient client,
        HomeController home,
        Window window,
        AppSettings settings,
        WallpaperSection wallpapers,
        AppImageCache imageCache,
        AiUiState aiState,
        Features.Multiplayer.MultiplayerController multiplayer)
    {
        Window = window;
        AppPreferences = settings;
        Multiplayer = multiplayer;
        _core = new(client, () => _ = home.RefreshAsync(), home.TrackManagementWork);
        _core.EnableAutoSave();
        _search = new(NavigateSearch);
        SearchNoticeKind = 0;
        AiSection = new(aiState.Configuration, window);
        DataDirectory = new(window, settings, client.Paths.Root);
        Language = new(window, settings);
        Wallpapers = wallpapers;
        ImageCache = new(imageCache, window);
        _general = new(this);
        _about = new(window);
        _maintenance = new(client, home, window, ImageCache);
        _launch = new(_core, "launch", window);
        _network = new(_core, "network", window);
        _javaManager = new(client, home, _core, window);
        _java = new(_core, "java", window, () =>
            {
                JavaManagerVisible = true;
                _ = _javaManager.OpenAsync();
            });
        _logs = new(_core, "logs", window, diagnostics: new(client, home, window));
        _advanced = new(_core, "advanced", window);
        _content = new(this);
        Category = "general";
        _navigation = new(SelectCategory);
        Theme = settings.ThemePreference;
        ThemeError = string.Empty;
        AfterLaunch = settings.AfterLaunchBehavior is "Tray" or "Taskbar" or "KeepVisible" ? settings.AfterLaunchBehavior : "Tray";
        AfterLaunchError = string.Empty;
        Width = window.ClientSize.Width;
        CloseBehavior = settings.CloseBehavior == "Tray" ? "Tray" : "Exit";
        CloseError = string.Empty;
    }

    internal partial string Category { get; private set; }
    private partial bool JavaManagerVisible { get; set; }
    internal partial ThemeRequest Theme { get; private set; }
    internal partial string ThemeError { get; private set; }
    internal partial string AfterLaunch { get; private set; }
    internal partial string AfterLaunchError { get; private set; }
    internal partial string CloseBehavior { get; private set; }
    internal partial string CloseError { get; private set; }
    internal partial float Width { get; private set; }

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Pixels(LauncherLayout.PageHeaderHeight), GridLength.Star()],
        [
            Grid(
                [GridLength.Star(), GridLength.Pixels(Math.Clamp(Width * .3f, 190f, 340f))],
                [GridLength.Auto],
                [
                    Text(Resources.NavSettings)
                        .FontSize(32f).FontWeight(700).Foreground(LauncherTheme.Ink)
                        .VAlign(VerticalAlignment.Bottom)
                        .Automation(new()
                        {
                            Name = Resources.NavSettings,
                            AutomationId = "settings.heading"
                        })
                        .Cell(0, 0),
                    _search.VAlign(VerticalAlignment.Center)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(16f)
                .VAlign(VerticalAlignment.Bottom)
                .Margin(new Thickness(0f, 0f, LauncherLayout.PageMargin.Right, 0f))
                .Cell(0, 0),
            Grid(
                [GridLength.Pixels(Width < 1038f ? 104f : 156f), GridLength.Star()],
                [GridLength.Star()],
                [
                    ScrollView(_navigation)
                        .Cell(0, 0),
                    Overlay([
                        ScrollView(_content.Margin(new Thickness(0f, 0f, 0f, LauncherLayout.PageMargin.Bottom)))
                            .Automation(new() { AutomationId = "settings.viewport" })
                            .Visible(!JavaManagerVisible || Category != "java"),
                        Grid(
                            [GridLength.Star()],
                            [GridLength.Auto, GridLength.Star()],
                            [
                                new JavaManagerHeading(LauncherAppearance.Capture(),Window.FlowDirection == FlowDirection.RightToLeft, () => JavaManagerVisible = false, LauncherTheme.Ink, LauncherButtons.IconButton).Cell(0, 0),
                                _javaManager.Cell(0, 1),
                            ])
                            .RowSpacing(14f)
                            .Margin(new Thickness(0f, 0f, LauncherLayout.PageMargin.Right, LauncherLayout.PageMargin.Bottom))
                            .Visible(JavaManagerVisible && Category == "java"),
                    ])
                        .Cell(1, 0),
                ])
                .ColumnSpacing(Width < 1038f ? 18f : 28f)
                .Cell(0, 1),
        ])
        .RowSpacing(LauncherLayout.PageHeaderGap);

    private void SelectCategory(string category)
    {
        SearchNoticeKind = 0;
        _pendingSearchTarget = null;
        Category = category;
        _navigation.Category = category;
        if (category == "content")
            _ = _maintenance.RefreshAsync();
    }

    internal void OpenAi(string target)
    {
        SelectCategory("ai");
        _pendingSearchTarget = target;
        _searchLayoutAttempts = 0;
    }

    private void NavigateSearch(SettingsSearchEntry entry)
    {
        SelectCategory(entry.Category);
        JavaManagerVisible = false;
        var target = entry.Id;
        var automaticMemory = _core.State.Text(CorePreferenceFields.All.First(field => field.Key == "memoryMode")) == "auto";
        if (entry.Category == "java" && ((entry.Id is "settings.number.memoryReserveMb" or "settings.number.autoMemoryMinMb" or "settings.number.autoMemoryMaxMb" && !automaticMemory) || (entry.Id == "settings.number.maxMemoryMb" && automaticMemory)))
        {
            target = "settings.field.memoryMode";
            SearchNoticeKind = 1;
        }

        if (entry.Id == "settings.input.network.proxy" && !_core.State.ProxyManual)
        {
            target = "settings.field.network.proxy";
            SearchNoticeKind = 3;
        }

        _pendingSearchTarget = target;
        _searchLayoutAttempts = 0;
    }

    private void OnSearchLayout()
    {
        if (_pendingSearchTarget is not { } id || Interaction is not { IsCollapsed: false } interactions || Semantics is not { } semantics)
            return;
        if (SettingsSearchLocator.Find(semantics, interactions, id) is null)
        {
            if (Category == "content")
                _maintenance.Expand();
            if (Category == "general" && id.StartsWith("settings.multiplayer", StringComparison.Ordinal))
                _general.ExpandMultiplayer();
            var form = Category switch
            {
                "launch" => _launch,
                "java" => _java,
                "network" => _network,
                "logs" => _logs,
                "advanced" => _advanced,
                _ => null
            };
            _ = form?.RealizeSearchTarget(id);
        }

        if (SettingsSearchLocator.Find(semantics, interactions, id) is { } target)
        {
            target.StartBringIntoView(new BringIntoViewOptions
            {
                Animated = false,
                VerticalAlignmentRatio = .3f
            });
            if (!target.IsEffectivelyEnabled || Window.SetFocus(target, FocusState.Programmatic) || ReferenceEquals(Window.FocusedElement, target))
                _pendingSearchTarget = null;
        }
        else if (++_searchLayoutAttempts >= 8)
        {
            _pendingSearchTarget = null;
            SearchNoticeKind = 2;
        }
    }

    internal void ChooseTheme(int index)
    {
        try
        {
            var theme = index == 1
                ? ThemeRequest.Light
                : index == 2 ? ThemeRequest.Dark : ThemeRequest.FollowSystem;
            AppPreferences.ThemePreference = theme;
            Sprout.Theming.Theme.Current.SetRequestedMode(theme);
            Theme = theme;
            ThemeError = string.Empty;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            ThemeError = exception.Message;
        }
    }

    internal void ChooseAfterLaunch(int index)
    {
        try
        {
            var value = index == 1
                ? "Taskbar"
                : index == 2 ? "KeepVisible" : "Tray";
            AppPreferences.AfterLaunchBehavior = value;
            AfterLaunch = value;
            AfterLaunchError = string.Empty;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            AfterLaunchError = exception.Message;
        }
    }

    internal void ChooseCloseBehavior(int index)
    {
        try
        {
            var value = index == 1 ? "Tray" : "Exit";
            AppPreferences.CloseBehavior = value;
            CloseBehavior = value;
            CloseError = string.Empty;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            CloseError = exception.Message;
        }
    }

    internal bool HasPendingWrites => _core.HasPendingWrites;

    internal Task<bool> FlushAsync() => _core.FlushAsync(finishEditing: true);

    internal async Task ActivateAsync()
    {
        if (!_core.State.Loaded && !_core.State.Busy)
            await _core.LoadAsync();
        if (Category == "content")
            await _maintenance.RefreshAsync();
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Width = args.Size.Width;

    protected override IDisposable? OnMounted()
    {
        Window.SizeChanged += OnSizeChanged;
        Window.LayoutUpdated += OnSearchLayout;
        return null;
    }

    protected override void OnDispose()
    {
        Window.SizeChanged -= OnSizeChanged;
        Window.LayoutUpdated -= OnSearchLayout;
        _core.Dispose();
        base.OnDispose();
    }
}

internal sealed partial class SettingsPageContent(SettingsPage owner) : Control
{
    private Ui Body => Column([
        Text(owner.SearchNotice)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(owner.SearchNotice.Length > 0)
            .Automation(new()
            {
                Name = owner.SearchNotice,
                AutomationId = "settings.search.notice",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        owner._general.Visible(owner.Category == "general"),
        owner._launch.Visible(owner.Category == "launch"),
        owner._java.Visible(owner.Category == "java"),
        owner._network.Visible(owner.Category == "network"),
        owner._maintenance.Visible(owner.Category == "content"),
        owner._logs.Visible(owner.Category == "logs"),
        owner._advanced.Visible(owner.Category == "advanced"),
        owner.AiSection.Visible(owner.Category == "ai"),
        owner._about.Visible(owner.Category == "about"),
    ])
        .Spacing(14f)
        .Padding(0f, 0f, LauncherLayout.PageMargin.Right, 0f);
}