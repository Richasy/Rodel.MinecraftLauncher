// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Popups;

namespace Rodel.MinecraftLauncher.App.Features.Import;

internal sealed class ImportPresenter : IDisposable
{
    private readonly Window _window;
    private readonly ImportController _controller;
    private Flyout? _popup;

    internal ImportPresenter(Window window, ImportController controller)
    {
        _window = window;
        _controller = controller;
        _window.ActualThemeChanged += OnThemeChanged;
    }

    internal void Show(string? serverId = null)
    {
        if (ModalPopup.IsOpen(_window))
            return;
        _controller.Reset(serverId);
        ImportPopup? content = null;
        var popup = new Flyout(close => content = new ImportPopup(_controller, _window, close))
        {
            Placement = PopupPlacement.Full,
            IsLightDismissEnabled = false,
            IsModal = true,
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = Resources.ImportInstance,
            AutomationId = "import.popup",
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