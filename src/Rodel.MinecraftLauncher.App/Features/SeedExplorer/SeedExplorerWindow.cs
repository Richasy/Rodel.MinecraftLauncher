// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Shell;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Localization;
using Sprout.Reactive;
using Sprout.WindowsAppSDK;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Owns one non-modal seed explorer and its independent packaged window placement.</summary>
internal sealed class SeedExplorerWindow
{
    private readonly Application _application;
    private readonly Window _launcher;
    private readonly LauncherClient _client;
    private Window? _window;
    private SeedExplorerController? _controller;
    private Task _initialization = Task.CompletedTask;

    internal SeedExplorerWindow(Application application, Window launcher, LauncherClient client)
    {
        _application = application; _launcher = launcher; _client = client;
        launcher.Closed += (_, _) => _window?.Close();
    }

    internal void Show(Core.Worlds.Maps.WorldMapSelection? selection = null)
    {
        if (_window is { } existing)
        {
            existing.Show();
            if (existing.State == WindowState.Minimized) existing.Restore();
            existing.Activate();
            if (selection is not null) _ = OpenSelectionAsync(selection);
            return;
        }

        var settings = AppSettings.Open("SeedExplorer");
        try
        {
            var area = _launcher.DisplayWorkArea;
            var size = new Size(Math.Min(1280f, area.Size.Width), Math.Min(800f, area.Size.Height));
            var window = _application.OpenWindow(new WindowSpec
            {
                Title = Resources.SeedTitle,
                Automation = new() { AutomationId = "seed.explorer" },
                Size = size,
                MinSize = new(800f, 560f),
                Placement = WindowPreferences.Read(settings),
                StartupLocation = WindowStartupLocation.Manual,
                Position = new((area.Bounds.Left + ((area.Size.Width - size.Width) / 2)) * area.Scale.Factor,
                    (area.Bounds.Top + ((area.Size.Height - size.Height) / 2)) * area.Scale.Factor),
                CaptionButtons = CaptionButton.All,
            });
            _window = window;
            var state = new SeedExplorerState();
            var controller = new SeedExplorerController(_client, window, state);
            _controller = controller;
            state.Controller = controller;
            var view = new SeedExplorerView(state);
            WindowPreferences.Attach(window, settings);
            void Resize(object? sender, WindowSizeChangedEventArgs args) => state.Current = state.Current with { Width = args.Size.Width, Height = args.Size.Height };
            state.Current = state.Current with { Width = window.ClientSize.Width, Height = window.ClientSize.Height };
            window.SizeChanged += Resize;
            window.SetContent(view);
            window.SetTitleBarContent(new SeedExplorerTitleBar(state, view.Popups, window));
            if (PackageIdentity.IsPackaged)
                _ = window.UsePackageIcon();
            var appearance = new Effect(() =>
            {
                window.FlowDirection = Localizer.CurrentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                window.Title = Resources.SeedTitle; window.Background = LauncherTheme.Background;
                var normal = new ChromeColors(LauncherTheme.Ink, Colors.Transparent);
                window.Colors = new(normal, normal, new CaptionButtonColors(Normal: normal, Inactive: normal));
            });
            window.Closed += async (_, _) =>
            {
                _window = null; _controller = null; window.SizeChanged -= Resize;
                appearance.Dispose(); settings.Dispose();
                await controller.DisposeAsync();
            };
            window.EnsureVisible(); window.Show();
            _initialization = controller.InitializeAsync();
            if (selection is not null) _ = OpenSelectionAsync(selection);
        }
        catch
        {
            if (_window is { } opened) opened.Close(); else settings.Dispose();
            throw;
        }
    }

    private async Task OpenSelectionAsync(Core.Worlds.Maps.WorldMapSelection selection)
    {
        var controller = _controller;
        try
        {
            await _initialization;
            if (controller is not null && !controller.Disposed && controller == _controller) await controller.Worlds.OpenSelectionAsync(selection);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { controller?.Error(error); }
    }
}