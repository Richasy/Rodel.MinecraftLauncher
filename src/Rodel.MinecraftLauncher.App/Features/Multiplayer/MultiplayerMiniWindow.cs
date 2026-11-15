// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Popups;
using System.ComponentModel;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Owns a lazily created room status window; its dismissal never controls a room or game lifetime.</summary>
internal sealed class MultiplayerMiniWindow : IDisposable
{
    private readonly Application _application;
    private readonly Window _main;
    private readonly AppSettings _settings;
    private readonly MultiplayerController _controller;
    private readonly Action _restore;
    private readonly MultiplayerMiniVisibility _visibility = new();
    private Window? _window;
    private Action? _refreshView;
    private Flyout? _members;
    private bool _disposed;

    internal MultiplayerMiniWindow(Application application, Window main, AppSettings settings, MultiplayerController controller, Action restore)
    {
        _application = application; _main = main; _settings = settings; _controller = controller; _restore = restore;
        controller.Changed += Refresh;
        main.StateChanged += OnMainChanged; main.Activated += OnMainChanged;
        main.DisplayChanged += OnMainChanged; main.PositionChanged += OnPositionChanged;
        main.ActualThemeChanged += OnMainChanged; main.Closed += OnMainClosed;
        settings.PropertyChanged += OnPreferenceChanged;
    }

    internal void Refresh()
    {
        if (_disposed) return;
        if (!_visibility.Update(_controller.Room, _settings.MultiplayerEnabled && _settings.MultiplayerMiniWindow, !_main.IsVisible || _main.State == WindowState.Minimized))
        {
            _members?.Hide();
            if (_window?.IsVisible == true) _window.Hide();
            return;
        }

        if (MultiplayerPresentation.OnlineCount(_controller.Room) is null) _members?.Hide();
        if (_window is null)
        {
            // A peer remains visible while the main window is minimized. Chrome.None belongs to this compact
            // status surface; the main window retains Sprout's normal caption buttons and non-client behavior.
            var window = _application.OpenWindow(new WindowSpec
            {
                Title = Resources.MultiplayerMiniWindow,
                Automation = new() { Name = Resources.MultiplayerMiniWindow, AutomationId = "multiplayer.mini.window" },
                Chrome = WindowChrome.None,
                Size = new(320f, 64f),
                MinSize = new(320f, 64f),
                MaxSize = new(320f, 64f),
                IsResizable = false,
                StartupLocation = WindowStartupLocation.Manual,
                Position = Position(),
                Background = LauncherTheme.Background,
            });
            _window = window;
            var view = new MultiplayerMiniView(_controller, Dismiss, ReturnToRoom, ShowMembers);
            _refreshView = view.Refresh;
            window.SetContent(view);
            window.IsAlwaysOnTop = true;
            window.Closing += OnClosing;
            window.Closed += (_, _) => { _window = null; _refreshView = null; };
        }

        var position = Position();
        if (_window.Position != position) _window.Position = position;
        _window.FlowDirection = _main.FlowDirection;
        _window.Title = Resources.MultiplayerMiniWindow;
        _refreshView?.Invoke();
        // Showing a status window must not take keyboard focus away from the running game.
        if (!_window.IsVisible) _window.ShowInactive();
    }

    private Point Position()
    {
        var area = _main.DisplayWorkArea;
        return new((area.Bounds.Right - 332f) * area.Scale.Factor, (area.Bounds.Bottom - 72f) * area.Scale.Factor);
    }

    private void Dismiss()
    {
        _visibility.Dismiss(); _members?.Hide(); _window?.Hide();
    }

    internal void HideForMainRestore()
    {
        _visibility.Update(_controller.Room, _settings.MultiplayerEnabled && _settings.MultiplayerMiniWindow, false);
        _members?.Hide(); _window?.Hide();
    }

    private void ReturnToRoom()
    {
        _members?.Hide(); _window?.Hide(); _restore();
    }

    private void ShowMembers()
    {
        if (_window is not { } window || MultiplayerPresentation.OnlineCount(_controller.Room) is null) return;
        if (_members?.IsOpen == true) { _members.Hide(); return; }

        _members = new Flyout(() => new MultiplayerMiniMembers(_controller, Math.Max(120f, Math.Min(340f, _main.DisplayWorkArea.Size.Height - 180f))))
        {
            Placement = PopupPlacement.TopEdgeAlignedRight,
            ShouldConstrainToRootBounds = false,
            Gap = 8f,
        };
        _members.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f)).Automation(new() { Name = Resources.MultiplayerMembers, AutomationId = "multiplayer.mini.members.flyout" });
        _members.ShowAt(window, Rect.FromXywh(0f, 0f, 320f, 64f));
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_disposed) return;
        e.Cancel = true; Dismiss();
    }

    private void OnMainChanged(object? sender, EventArgs e) => Refresh();
    private void OnPositionChanged(object? sender, WindowPositionChangedEventArgs e) => Refresh();
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(AppSettings.MultiplayerEnabled) or nameof(AppSettings.MultiplayerMiniWindow) or nameof(AppSettings.Language)) Refresh(); }

    private void OnMainClosed(object? sender, EventArgs e) => Dispose();

    /// <summary>Closes only owned UI and releases observers when the launcher exits.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Changed -= Refresh;
        _main.StateChanged -= OnMainChanged; _main.Activated -= OnMainChanged;
        _main.DisplayChanged -= OnMainChanged; _main.PositionChanged -= OnPositionChanged;
        _main.ActualThemeChanged -= OnMainChanged; _main.Closed -= OnMainClosed;
        _settings.PropertyChanged -= OnPreferenceChanged;
        _members?.Hide(); _window?.Close(); _refreshView = null;
    }
}