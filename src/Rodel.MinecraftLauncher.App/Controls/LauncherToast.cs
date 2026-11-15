// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Semantics;
using System.Runtime.CompilerServices;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>Shows a square, theme-following notification in its owning window without taking focus or blocking navigation.</summary>
internal static class LauncherToast
{
    private static readonly ConditionalWeakTable<Window, Notification> Notifications = [];

    internal static void Show(Window window, string message) => Notifications.GetValue(window, static owner => new(owner)).Show(message, false, false);

    internal static void ShowError(Window window, string message) => Notifications.GetValue(window, static owner => new(owner)).Show(message, true, true);

    internal static void ShowTransientError(Window window, string message) => Notifications.GetValue(window, static owner => new(owner)).Show(message, true, false);

    private sealed class Notification
    {
        private readonly Window _window;
        private readonly DispatcherQueueTimer _timer;
        private readonly Queue<string> _pendingErrors = new();
        private PopupHandle? _popup;
        private string? _activeError;

        internal Notification(Window window)
        {
            _window = window;
            _timer = DispatcherQueue.TryGetForCurrentThread()!.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _timer.IsRepeating = false;
            _timer.Tick += OnTick;
            window.Closed += (_, _) =>
            {
                Hide();
                _pendingErrors.Clear();
                _activeError = null;
                _timer.Tick -= OnTick;
            };
        }

        internal void Show(string message, bool error, bool requiresDismissal)
        {
            if (_activeError is not null)
            {
                if (requiresDismissal && message != _activeError && !_pendingErrors.Contains(message))
                    _pendingErrors.Enqueue(message);
                return;
            }

            Hide();
            _activeError = requiresDismissal ? message : null;
            var anchor = new ToastAnchor(_window);
            _popup = _window.Popups.Open(
                new LauncherToastCard(message, error, requiresDismissal, Dismiss, Pause),
                anchor.GetSnapshot()!.Value.Anchor,
                _window.FlowDirection == FlowDirection.RightToLeft ? PopupPlacement.TopEdgeAlignedLeft : PopupPlacement.TopEdgeAlignedRight,
                isLightDismissEnabled: false,
                gap: 0f,
                takeFocus: false,
                transition: PopupTransition.None,
                chromeFactory: () => new PopupChrome(
                    LauncherMaterials.Acrylic,
                    LauncherTheme.Edge,
                    1f,
                    CornerRadius.Zero,
                    default,
                    null,
                    0f,
                    420f,
                    0f,
                    0f),
                anchorProvider: anchor,
                participatesInDismiss: false);
            var popup = _popup;
            popup.Closed += () => { if (ReferenceEquals(_popup, popup)) _timer.Stop(); };
            if (!requiresDismissal) _timer.Start();
        }

        private void Pause(bool value)
        {
            if (_activeError is not null) return;
            if (value)
                _timer.Stop();
            else if (_popup?.IsOpen == true)
                _timer.Start();
        }

        private void Hide()
        {
            var popup = _popup;
            _popup = null;
            _timer.Stop();
            popup?.Close();
        }

        private void Dismiss()
        {
            Hide();
            _activeError = null;
            if (_pendingErrors.TryDequeue(out var message)) Show(message, true, true);
        }

        private void OnTick(object? sender, EventArgs e) { if (_activeError is null) Hide(); }
    }

    private sealed class ToastAnchor(Window window) : IPopupAnchorProvider
    {
        /// <inheritdoc/>
        public PopupAnchorSnapshot? GetSnapshot() => new(Rect.FromXywh(window.FlowDirection == FlowDirection.RightToLeft ? 24f : window.ClientSize.Width - 24f, window.ClientSize.Height - 24f, 0f, 0f), Rect.FromXywh(0f, 0f, window.ClientSize.Width, window.ClientSize.Height));
    }
}

internal sealed partial class LauncherToastCard(
    string message,
    bool error,
    bool requiresDismissal,
    Action close,
    Action<bool> hover) : Control
{
    private Ui Body => HoverArea(
        Grid(
            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                LauncherIcons.Line(error ? "error" : "check", 22f, error ? LauncherTheme.Error : LauncherTheme.ProgressAccent)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Text(message)
                    .FontSize(13f).Foreground(LauncherTheme.Ink)
                    .Wrap()
                    .Margin(new Thickness(12f, 0f, 0f, 0f))
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = message,
                        AutomationId = "app.toast.message",
                        LiveSetting = error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite
                    })
                    .Cell(1, 0),
                LauncherControls.OutlineCommand(
                    Row([
                        LauncherIcons.Line("close", 16f)
                    ]),
                    close)
                    .Style(LauncherButtons.PopupCloseButton)
                    .Width(30f).Height(30f)
                    .Margin(new Thickness(12f, 0f, 0f, 0f))
                    .ToolTip(Resources.AiClose)
                    .Automation(new()
                    {
                        Name = Resources.AiClose,
                        AutomationId = "app.toast.close"
                    })
                    .Visible(requiresDismissal).Cell(2, 0),
            ])
            .Padding(16f, 12f),
        hover);
}