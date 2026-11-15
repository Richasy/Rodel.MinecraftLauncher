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

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Owns the single non-modal editor window and its independent packaged placement preferences.</summary>
internal sealed class SkinEditorWindow
{
    private readonly Application _application;
    private readonly Window _launcher;
    private Window? _window;
    private readonly LauncherClient _client;
    internal SkinEditorState? State { get; private set; }

    internal SkinEditorWindow(Application application, Window launcher, LauncherClient client)
    {
        _application = application;
        _launcher = launcher;
        _client = client;
        // A real launcher exit closes its peer too; hiding/minimizing the launcher does not.
        launcher.Closed += (_, _) => _window?.Close();
        launcher.Closing += (_, args) =>
        {
            State?.Controller.LayerActions.StopDrag();
            if (State is { } state && (state.HasLayerDraft || state.FileActive || state.Busy || state.Controller.HasDeferredStroke || (state.Controller.Session?.HasDraft == true)))
            {
                args.Cancel = true;
                state.Status = state.Busy
                    ? Resources.SkinSaving
                    : state.FileActive
                    ? Resources.SkinPngPending
                    : state.HasLayerDraft ? Resources.SkinLayerPending : Resources.SkinDraftPending;
                Show();
            }
        };
    }

    internal void Show()
    {
        if (_window is { } existing)
        {
            existing.Show();
            if (existing.State == WindowState.Minimized)
                existing.Restore();
            existing.Activate();
            return;
        }

        var settings = AppSettings.Open("SkinEditor");
        try
        {
            // A peer window has its own taskbar entry and can be moved/minimized independently.
            // Seed native creation on the launcher's CURRENT display, before Sprout captures DPI.
            var area = _launcher.DisplayWorkArea;
            var size = new Size(Math.Min(1280f, area.Size.Width), Math.Min(800f, area.Size.Height));
            var position = new Point((area.Bounds.Left + ((area.Size.Width - size.Width) / 2f)) * area.Scale.Factor, (area.Bounds.Top + ((area.Size.Height - size.Height) / 2f)) * area.Scale.Factor);
            var window = _application.OpenWindow(new WindowSpec
            {
                Title = Resources.SkinEditorTitle,
                Automation = new() { AutomationId = "skin.editor" },
                Size = size,
                MinSize = new(960f, 640f),
                Placement = WindowPreferences.Read(settings),
                StartupLocation = WindowStartupLocation.Manual,
                Position = position,
                CaptionButtons = CaptionButton.All,
            });
            _window = window;
            window.Closed += (_, _) =>
            {
                _window = null;
                State = null;
                settings.Dispose();
            };
            WindowPreferences.Attach(window, settings);
            var view = new SkinEditorView(_client, window, settings);
            State = view.State;
            window.Closing += (_, args) =>
            {
                view.State.Controller.LayerActions.StopDrag();
                if (view.State.HasLayerDraft || view.State.FileActive || view.State.Busy || view.State.Controller.HasDeferredStroke || (view.State.Controller.Session?.HasDraft == true))
                {
                    args.Cancel = true;
                    view.State.Status = view.State.Busy
                        ? Resources.SkinSaving
                        : view.State.FileActive
                        ? Resources.SkinPngPending
                        : view.State.HasLayerDraft ? Resources.SkinLayerPending : Resources.SkinDraftPending;
                }
            };
            void Resize(object? sender, WindowSizeChangedEventArgs args)
            {
                view.State.Width = args.Size.Width;
                view.State.Height = args.Size.Height;
            }

            view.State.Width = window.ClientSize.Width;
            view.State.Height = window.ClientSize.Height;
            window.SizeChanged += Resize;
            window.Closed += (_, _) => window.SizeChanged -= Resize;
            window.SetContent(view);
            window.SetTitleBarContent(new SkinEditorTitleBar(view.State, window));
            if (PackageIdentity.IsPackaged)
                _ = window.UsePackageIcon();
            var appearance = new Effect(() =>
                {
                    window.FlowDirection = Localizer.CurrentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                    window.Title = Resources.SkinEditorTitle;
                    window.Background = LauncherTheme.Background;
                    var normal = new ChromeColors(LauncherTheme.Ink, Colors.Transparent);
                    var buttons = new CaptionButtonColors(Normal: normal, Inactive: normal);
                    window.Colors = new TitleBarColors(normal, normal, buttons);
                });
            window.Closed += (_, _) => appearance.Dispose();
            window.EnsureVisible();
            window.Show();
        }
        catch
        {
            if (_window is { } opened)
            {
                opened.Close();
            }
            else
            {
                settings.Dispose();
            }

            throw;
        }
    }
}