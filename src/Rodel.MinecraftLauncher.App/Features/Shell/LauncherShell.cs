// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reactive;
using Sprout.Theming;
using System.ComponentModel;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

/// <summary>Composes the window's pages, navigation and app preferences; game state belongs to HomeController.</summary>
public sealed partial class LauncherShell : Control
{
    private readonly Window _window;
    private readonly AppSettings _settings;
    private readonly HomeController _home;
    private readonly Features.Accounts.AccountsFeature _accounts;
    private readonly Features.Servers.ServerConnectionPresenter _serverConnections;
    public partial int AccountRevision { get; private set; }
    private string AccountLabel
    {
        get
        {
            _ = AccountRevision;
            return string.Format(Resources.AccountSelectLabel, _accounts.CurrentName);
        }
    }

    private void OnAccountChanged()
    {
        AccountRevision++;
        _homePage.RefreshServerIdentity();
    }
    private readonly HomePage _homePage;
    private readonly Features.Instances.InstancesPage _instancesPage;
    private readonly Features.Instances.InstanceOperationPresenter _instanceActions;
    private readonly Features.Content.ContentPage _contentPage;
    private readonly Features.Multiplayer.MultiplayerPage _multiplayerPage;
    private readonly Features.Multiplayer.MultiplayerMiniWindow _multiplayerMini;
    private readonly Action<string> _createMultiplayer;
    private readonly WallpaperService _wallpapers;
    private readonly WallpaperSection _wallpaperSection;
    public partial int WallpaperRevision { get; private set; }
    private WallpaperItem CurrentWallpaper
    {
        get
        {
            _ = WallpaperRevision;
            return _wallpapers.Current;
        }
    }

    private void OnWallpaperChanged() => WallpaperRevision++;
    private readonly Features.Settings.SettingsPage _settingsPage;
    private readonly Features.Ai.AiPage _aiPage;
    private readonly Features.Ai.AiUiState _aiState;
    private readonly LauncherWindowBehavior _windowBehavior;

    internal LauncherShell(
        LauncherClient client,
        Window window,
        AppSettings settings,
        Application application,
        AppImageCache imageCache,
        Action showSkinEditor,
        Action showSeedExplorer,
        Action<Core.Worlds.Maps.WorldMapSelection> showWorldMap)
    {
        _window = window;
        _settings = settings;
        _wallpapers = new(window, settings);
        static Task<LaunchOptions> GameDefaultsAsync() => Task.FromResult(new LaunchOptions
        {
            GameLanguage = Features.Settings.LanguageSection.GameLanguage,
        });
        _home = new(client, settings.SelectedInstanceId, id => settings.SelectedInstanceId = id, GameDefaultsAsync);
        _wallpaperSection = new(_wallpapers);
        _home.PrepareServerConnection = new Features.Packs.ServerPackLaunchPresenter(client, _home, window).PrepareAsync;
        _wallpapers.Changed += OnWallpaperChanged;
        _accounts = new(client, window, showSkinEditor, showSeedExplorer);
        _home.RequireLaunchIdentity = _accounts.EnsureLaunchIdentityAsync;
        _accounts.Start();
        _accounts.Changed += OnAccountChanged;
        _serverConnections = new Features.Servers.ServerConnectionPresenter(client, _home, window, ShowLocalInstances)
        {
            ExternalSignIn = account => _accounts.ShowExternal(account),
        };
        _instanceActions = new(client, _home, window);
        _homePage = new(
            _home,
            window,
            () => Navigate("instances"),
            _wallpapers,
            () => OpenInstanceEntry(false),
            () => OpenInstanceEntry(true),
            OpenDependencyRepair,
            client,
            settings,
            _serverConnections,
            ShowServerInstances,
            () => Navigate("home"),
            OpenPackImport,
            _instanceActions)
        {
            ReturnToRoom = () => Navigate("multiplayer"),
        };
        _instancesPage = new(
            client,
            _home,
            window,
            _homePage.ShowCreate,
            _homePage.ShowImport,
            LaunchLocalFromManagement,
            _serverConnections,
            _homePage.ShowServerCreate,
            _homePage.ShowServerImport,
            _homePage.LaunchServerClient,
            _homePage.ShowResumeInstallation,
            _instanceActions);
        _contentPage = new(
            client,
            _home,
            window,
            () => Navigate("instances"),
            () => OpenInstanceEntry(false),
            () => OpenInstanceEntry(true),
            showWorldMap);
        var multiplayer = new Features.Multiplayer.MultiplayerController(client, _home, GameDefaultsAsync);
        _multiplayerPage = new(client, multiplayer, window, id =>
        {
            _home.Select(id);
            Navigate("content");
            _contentPage.ChooseKind(Features.Content.ContentKind.Mods);
        })
        {
            RoomChanged = RefreshMultiplayer,
        };
        _createMultiplayer = id =>
        {
            Navigate("multiplayer");
            _multiplayerPage.CreateFor(id);
        };
        MultiplayerAvailable = settings.MultiplayerEnabled;
        _instancesPage.CreateMultiplayer = MultiplayerAvailable ? _createMultiplayer : null;
        _windowBehavior = new(application, window, () => _contentPage.HasActiveDownloads || (!_homePage.HasOwnedWork && !_accounts.HasActiveWork && !_multiplayerPage.HasOwnedWork), exception => _home.ReportError(_home.State.SelectedId, exception.Message));
        _multiplayerMini = new(application, window, settings, multiplayer, () => { _windowBehavior.RestoreWindow(); Navigate("multiplayer"); });
        _windowBehavior.VisibilityChanged += _multiplayerMini.Refresh;
        _windowBehavior.Restoring += _multiplayerMini.HideForMainRestore;
        multiplayer.ReturnToGame = () =>
        {
            if (Features.Multiplayer.MultiplayerGameWindow.Restore(client, multiplayer.Room?.SessionId))
                _windowBehavior.AfterGameStarted(AfterLaunchPreference);
            else multiplayer.ReportError(Resources.MultiplayerGameUnavailable);
        };
        _home.GameStarted += OnGameStarted;
        _home.StateChanged += OnGamesChanged;
        var aiState = _aiState = new Features.Ai.AiUiState(client);
        _settingsPage = new(
            client,
            _home,
            window,
            settings,
            _wallpaperSection,
            imageCache,
            aiState,
            multiplayer);
        _aiPage = new(aiState, _home, window, target =>
            {
                Navigate("settings");
                _settingsPage.OpenAi(target);
            });
        Page = "home";
        ViewportWidth = window.ClientSize.Width;
        ViewportHeight = window.ClientSize.Height;
        window.SizeChanged += OnSizeChanged;
        window.Closing += OnClosing;
        settings.PropertyChanged += OnAppPreferenceChanged;
    }

    public partial string Page { get; private set; }
    private partial bool AiAvailable { get; set; }
    private partial bool MultiplayerAvailable { get; set; }

    private void RefreshMultiplayer() => _homePage.ActiveRoomName = MultiplayerAvailable ? _multiplayerPage.ActiveRoomName ?? string.Empty : string.Empty;

    private void OnAppPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.MultiplayerEnabled)) return;
        MultiplayerAvailable = _settings.MultiplayerEnabled;
        _instancesPage.CreateMultiplayer = MultiplayerAvailable ? _createMultiplayer : null;
        RefreshMultiplayer();
        if (!MultiplayerAvailable && Page == "multiplayer") Navigate("home");
    }

    private void OnAiConfigurationChanged()
    {
        var available = _aiState.Configuration.HasConfiguredModel;
        Untracked.Run(() =>
            {
                if (AiAvailable == available)
                    return;
                AiAvailable = available;
                _serverConnections.RepairRequested = available ? OpenAiRepair : null;
                if (!available && Page == "assistant")
                    Navigate("home");
            });
    }

    private void OpenAiRepair(Core.Application.AI.AiRepairCreate request)
    {
        if (!AiAvailable)
            return;
        Navigate("assistant");
        _ = _aiState.Chats.OpenRepairAsync(request, string.Format(Resources.AiRepairTitle, _home.State.Instances.FirstOrDefault(i => i.Id == request.InstanceId)?.DisplayName ?? request.InstanceId));
    }

    private void OpenPackImport(string? source = null)
    {
        Navigate("instances");
        _instancesPage.ShowPackImport(source);
    }

    private void OpenDependencyRepair(string instanceId, string sessionId)
    {
        _home.Select(instanceId);
        Navigate("content");
        _ = _contentPage.ShowDependencyRepairAsync(instanceId, sessionId);
    }

    internal string AfterLaunchPreference => _settingsPage.AfterLaunch;
    public partial float ViewportWidth { get; private set; }
    public partial float ViewportHeight { get; private set; }
    private LauncherLayout Layout => new(ViewportWidth, ViewportHeight);
    private string PageTitle => Page switch
    {
        "home" => Resources.NavHome,
        "instances" => Resources.NavInstances,
        "content" => Resources.NavContent,
        "multiplayer" => Resources.MultiplayerHeading,
        "settings" => Resources.NavSettings,
        _ => Resources.NavAssistant,
    };

    private Ui Body => Overlay([
        new ShellBackgroundSurface(new ShellBackgroundView(
            Page,
            LauncherTheme.Light,
            LauncherTheme.HighContrast,
            CurrentWallpaper.Source,
            CurrentWallpaper.Alignment,
            LauncherTheme.Background,
            LauncherMaterials.Shade,
            LauncherMaterials.HorizontalShade,
            LauncherTheme.PageShade,
            _wallpapers,
            new Size(ViewportWidth, ViewportHeight)))
            .OffThread(),
        new ShellNavigationView(
LauncherAppearance.Capture(),            Page,
            Navigate,
            _accounts.ShowMenu,
            AccountLabel,
            _accounts.CurrentAccount,
            _accounts.Avatars,
            !_accounts.Loaded,
            LauncherButtons.Button,
            LauncherButtons.Navigation(true),
            LauncherButtons.Navigation(false),
            LauncherMaterials.Acrylic,
            LauncherTheme.Edge,
            LauncherMaterials.Shadow,
            AiAvailable,
            MultiplayerAvailable,
            ViewportHeight < 650f,
            Math.Max(0f, ViewportHeight - LauncherLayout.NavigationMargin.Vertical)).Margin(LauncherLayout.NavigationMargin)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top),
        _homePage.Visible(Page == "home"),
        _instancesPage.Width(Layout.Width - 188f).Height(Layout.PageHeight)
            .Margin(LauncherLayout.PageMargin)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Visible(Page == "instances"),
        _contentPage.Width(Layout.Width - 188f).Height(Layout.PageHeight)
            .Margin(LauncherLayout.PageMargin)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Visible(Page == "content"),
        _settingsPage.Width(Layout.Width - LauncherLayout.PageMargin.Left).Height(Layout.PageHeight + LauncherLayout.PageMargin.Bottom)
            .Margin(new Thickness(LauncherLayout.PageMargin.Left, LauncherLayout.PageMargin.Top, 0f, 0f))
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Visible(Page == "settings"),
        _multiplayerPage.Width(Layout.Width - 188f).Height(Layout.PageHeight)
            .Margin(LauncherLayout.PageMargin)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Visible(Page == "multiplayer"),
        _aiPage.Width(Layout.Width - 188f).Height(Layout.PageHeight)
            .Margin(LauncherLayout.PageMargin)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Visible(Page == "assistant"),
    ]);

    private void Navigate(string page)
    {
        if (page == "multiplayer" && !MultiplayerAvailable) return;
        if (page == "assistant" && !AiAvailable)
            return;
        _aiPage.Activate(page == "assistant");
        Page = page;
        _homePage.Activate(page == "home");
        _instancesPage.Activate(page == "instances");
        _contentPage.Activate(page == "content");
        _wallpapers.Readability.Active = page == "home";
        _wallpapers.Readability.Invalidate();
        if (page == "settings")
            _ = _settingsPage.ActivateAsync();
    }

    private void ShowLocalInstances()
    {
        Navigate("instances");
        _instancesPage.ShowLocal();
    }

    private void ShowServerInstances()
    {
        Navigate("instances");
        _instancesPage.ShowServers();
    }

    private void LaunchLocalFromManagement()
    {
        _homePage.SelectLocalTarget();
        Navigate("home");
    }

    private void OpenInstanceEntry(bool import)
    {
        if (Controls.ModalPopup.IsOpen(_window))
            return;
        Navigate("instances");
        _instancesPage.RequestEntry(import);
    }

    private void OnGameStarted(string instanceId) => _windowBehavior.AfterGameStarted(AfterLaunchPreference);

    private void OnGamesChanged(HomePageState state) => _windowBehavior.AfterGamesObserved(state, _home.HasPreparingLaunch);

    internal void RestoreWindow() => _windowBehavior.RestoreWindow();

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e)
    {
        ViewportWidth = e.Size.Width;
        ViewportHeight = e.Size.Height;
    }
    private bool _downloadExitConfirmed;
    private bool _confirmingDownloadExit;
    private bool _flushingPreferences;
    private bool _preferencesFlushed;
    private bool _preferencesExitRequested;
    private bool _updateExitRequested;

    internal async Task<bool> InstallUpdateAsync(string installer, string sha256)
    {
        if (_homePage.HasOwnedWork || _accounts.HasActiveWork || _multiplayerPage.HasOwnedWork || _contentPage.HasActiveDownloads)
            return false;
        if (!await _instanceActions.FlushConfigurationAsync() || !await _settingsPage.FlushAsync())
            return false;
        var gate = installer + ".install";
        File.Delete(gate);
        using var worker = await Features.Updates.UpdateInstaller.StartAsync(installer, sha256, gate);
        var closed = false;
        void Authorize(object? sender, EventArgs args)
        {
            File.WriteAllText(gate, string.Empty);
            closed = true;
        }

        _window.Closed += Authorize;
        _updateExitRequested = true;
        try
        {
            _window.Close();
        }
        finally
        {
            _updateExitRequested = false;
            _window.Closed -= Authorize;
            if (!closed && !worker.HasExited)
                worker.Kill();
        }

        return closed;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_updateExitRequested)
            return;
        if (!_downloadExitConfirmed && _contentPage.HasActiveDownloads)
        {
            e.Cancel = true;
            if (_confirmingDownloadExit) return;
            _confirmingDownloadExit = true;
            try
            {
                _windowBehavior.RestoreWindow();
                if (await Features.Content.ContentDownloadConfirmation.ExitAsync(_window))
                {
                    _downloadExitConfirmed = true;
                    _window.Close();
                }
            }
            finally { _confirmingDownloadExit = false; }

            return;
        }

        if (!_preferencesFlushed && (_settingsPage.HasPendingWrites || _instanceActions.ConfigurationOpen))
        {
            e.Cancel = true;
            if (_flushingPreferences)
                return;
            _flushingPreferences = true;
            var explicitExit = _windowBehavior.ExitRequested;
            try
            {
                if (await _instanceActions.FlushConfigurationAsync() && await _settingsPage.FlushAsync())
                {
                    _preferencesFlushed = true;
                    _preferencesExitRequested = explicitExit;
                    try
                    {
                        _window.Close();
                    }
                    finally
                    {
                        _preferencesFlushed = false;
                        _preferencesExitRequested = false;
                    }
                }
                else
                {
                    _downloadExitConfirmed = false;
                    if (!_instanceActions.ConfigurationOpen)
                        Navigate("settings");
                }
            }
            finally
            {
                _flushingPreferences = false;
            }

            return;
        }

        if (_downloadExitConfirmed) return;

        var ownedWork = _homePage.HasOwnedWork || _accounts.HasActiveWork || _multiplayerPage.HasOwnedWork;
        if (!ownedWork && (_windowBehavior.ExitRequested || _preferencesExitRequested || (_settings.CloseBehavior != "Tray")))
        {
            return;
        }

        e.Cancel = true;
        if (ownedWork)
        {
            Navigate(_multiplayerPage.HasOwnedWork ? "multiplayer" : "home");
        }

        try
        {
            _windowBehavior.HideToTray();
        }
        catch (Exception exception)
        {
            _home.ReportError(_home.State.SelectedId, exception.Message);
        }
    }

    protected override IDisposable? OnMounted()
    {
        CaptionBackgroundSampler.Attach(_window, () => Page == "home", () => CurrentWallpaper);
        _wallpapers.Attach(Sprout.Theming.Environment.Resolve(ImageLoadServices.Key));
        _serverConnections.Statuses.Start(Sprout.Theming.Environment.Resolve(ImageLoadServices.Key));
        _home.Start();
        _multiplayerPage.Start();
        Scope.Run(() => _ = new Effect(OnAiConfigurationChanged));
        _ = _aiState.LoadConfigurationAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _settings.PropertyChanged -= OnAppPreferenceChanged;
        _windowBehavior.VisibilityChanged -= _multiplayerMini.Refresh;
        _windowBehavior.Restoring -= _multiplayerMini.HideForMainRestore;
        _multiplayerMini.Dispose();
        _accounts.Changed -= OnAccountChanged;
        _accounts.Dispose();
        _serverConnections.Dispose();
        _window.SizeChanged -= OnSizeChanged;
        _window.Closing -= OnClosing;
        _home.GameStarted -= OnGameStarted;
        _home.StateChanged -= OnGamesChanged;
        _wallpapers.Changed -= OnWallpaperChanged;
        _wallpapers.Dispose();
        _instanceActions.Dispose();
        _home.Dispose();
        _aiState.Dispose();
        _windowBehavior.Dispose();
        base.OnDispose();
    }
}