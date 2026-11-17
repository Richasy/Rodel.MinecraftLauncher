// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

/// <summary>Window visibility never changes ownership of Core tasks or game monitors.</summary>
internal sealed class LauncherWindowBehavior(
    Application application,
    Window window,
    Func<bool> canExit,
    Action<Exception> reportError) : IDisposable
{
    private static readonly WindowIcon DefaultIcon = WindowIcon.FromExecutable();
    private TrayIcon? _tray;
    private readonly GameWindowReturn _gameReturn = new();
    internal bool ExitRequested { get; private set; }
    internal event Action? VisibilityChanged;
    internal event Action? Restoring;

    internal void AfterGameStarted(string preference)
    {
        switch (preference)
        {
            case "Taskbar":
                window.Minimize();
                _gameReturn.Arm();
                break;
            case "Tray":
                HideToTray();
                _gameReturn.Arm();
                break;
        }
    }

    internal void HideToTray()
    {
        if (_tray is null)
        {
            _tray = application.CreateTrayIcon(() => new TrayIconOptions("launcher", window.Icon ?? DefaultIcon)
            {
                ToolTip = Resources.AppTitle,
                ContextMenu = BuildMenu,
                MenuThemeSource = TrayMenuThemeSource.Application,
            });
            _tray.Clicked += Restore;
            _tray.DoubleClicked += Restore;
            _tray.RecoveryFailed += OnRecoveryFailed;
        }
        // Never hide the only restore path until the shell has accepted its icon.
        _tray.Show();
        _gameReturn.Cancel();
        window.Hide();
        VisibilityChanged?.Invoke();
    }

    internal void AfterGamesObserved(HomePageState state, bool preparingLaunch)
    {
        if (_gameReturn.Observe(state, preparingLaunch) && (!window.IsVisible || window.State == WindowState.Minimized))
            Restore(this, EventArgs.Empty);
    }

    private MenuFlyout BuildMenu() => new MenuFlyout(new MenuFlyoutItem(Resources.RestoreLauncher, () => Restore(this, EventArgs.Empty))
    {
        Automation = new()
        {
            Name = Resources.RestoreLauncher,
            AutomationId = "tray.restore",
        },
    }, new MenuFlyoutItem(Resources.ExitLauncher, RequestExit)
    {
        IsEnabled = canExit(),
        Automation = new()
        {
            Name = Resources.ExitLauncher,
            AutomationId = "tray.exit"
        },
    }).Style(LauncherMenus.CommandMenu);

    private void RequestExit()
    {
        if (!canExit())
            return;
        ExitRequested = true;
        try
        {
            window.Close();
        }
        finally
        {
            ExitRequested = false;
        }
    }

    private void Restore(object? sender, EventArgs e)
    {
        Restoring?.Invoke();
        _gameReturn.Cancel();
        window.Show();
        if (window.State == WindowState.Minimized)
            window.Restore();
        window.Activate();
        VisibilityChanged?.Invoke();
    }

    internal void RestoreWindow() => Restore(this, EventArgs.Empty);

    private void OnRecoveryFailed(object? sender, TrayIconErrorEventArgs e)
    {
        Restore(sender, e);
        reportError(e.Error);
    }

    /// <summary>Releases the tray icon owned by this launcher window.</summary>
    public void Dispose() => _tray?.Dispose();
}