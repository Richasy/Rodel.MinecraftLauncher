// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Create;

internal sealed class CreatePresenter : IDisposable
{
    private readonly Window _window;
    private readonly CreateController _controller;
    private Flyout? _popup;
    private readonly Action<string> _launch;

    internal CreatePresenter(Window window, CreateController controller, Action<string> launch)
    {
        _window = window;
        _controller = controller;
        _launch = launch;
        _window.ActualThemeChanged += OnThemeChanged;
    }

    internal void Show() => ShowFor(null);

    internal void ShowFor(string? instanceId, string? serverId = null)
    {
        if (ModalPopup.IsOpen(_window))
            return;
        _controller.Open(instanceId, serverId);
        CreatePopup? content = null;
        var popup = new Flyout(close => content = new CreatePopup(_controller, _window, close, _launch))
        {
            Placement = PopupPlacement.Full,
            IsLightDismissEnabled = false,
            IsModal = true,
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = Resources.CreateInstance,
            AutomationId = "create.popup",
            IsDialog = true
        });
        popup.Closing += e =>
        {
            e.Cancel = content?.CanClose == false;
            if (e.Cancel)
                content?.RequestClose();
        };
        _popup = popup;
        ModalPopup.TryShow(_window, popup);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _popup?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup);

    public void Dispose()
    {
        _window.ActualThemeChanged -= OnThemeChanged;
        _popup?.Hide();
    }
}