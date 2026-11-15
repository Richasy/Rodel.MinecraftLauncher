// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Localization;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.Theming;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed class WallpaperService : IDisposable
{
    internal readonly Window Window;
    internal readonly WallpaperStore Store;
    internal readonly WallpaperReadability Readability;
    internal ImageLoadServices? Images { get; private set; }
    internal event Action? Changed;
    private Flyout? _picker;
    private Flyout? _rename;
    internal bool RenameOpen => _rename?.IsOpen == true;
    internal WallpaperPicker? PickerContent { get; private set; }
    private bool _disposed;
    private readonly Effect _languageEffect;
    private string? _language;
    internal bool Enabled => !_disposed && !LauncherTheme.HighContrast;
    private WallpaperItem[] _current = [];
    internal WallpaperItem Current => _current[LauncherTheme.Light ? 0 : 1];

    private void RefreshCurrent() => _current = [Store.Current(true), Store.Current(false)];

    internal WallpaperService(Window window, AppSettings settings)
    {
        Window = window;
        var localPath = AppStorage.LocalPath;
        Store = new(settings, System.IO.Path.Combine(localPath, "Wallpapers"));
        RefreshCurrent();
        Readability = new(this);
        window.ActualThemeChanged += OnThemeChanged;
        _languageEffect = new Effect(() =>
            {
                var language = Localizer.CurrentLanguage;
                if (_language == language)
                    return;
                _language = language;
                RefreshCurrent();
                _picker?.Automation(new()
                {
                    Name = Resources.WallpaperHeading,
                    AutomationId = "wallpaper.picker"
                });
                _rename?.Automation(new()
                {
                    Name = Resources.WallpaperRename,
                    AutomationId = "wallpaper.rename.dialog"
                });
                Changed?.Invoke();
            });
    }

    internal void Attach(ImageLoadServices? images)
    {
        Images = images;
        Readability.Attach();
        _ = RefreshCustomFilesAsync();
    }

    private async Task RefreshCustomFilesAsync()
    {
        var changed = await Store.RefreshCustomFilesAsync();
        if (_disposed)
            return;
        if (changed)
        {
            RefreshCurrent();
            Readability.Invalidate();
            Changed?.Invoke();
        }
    }

    internal void Show(bool light)
    {
        if (!Enabled || ModalPopup.IsOpen(Window))
            return;
        _ = RefreshCustomFilesAsync();
        var popup = new Flyout(close => PickerContent = new WallpaperPicker(this, light, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        };
        popup.Closing += e =>
        {
            if (!Enabled)
                return;
            if (RenameOpen || PickerContent?.Busy == true)
                e.Cancel = true;
        };
        _picker = popup;
        popup.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(840f));
        popup.Automation(new()
        {
            Name = Resources.WallpaperHeading,
            AutomationId = "wallpaper.picker",
            IsDialog = true
        });
        ModalPopup.TryShow(Window, popup);
    }

    internal void ShowRename(bool light)
    {
        if (!Enabled || _picker?.IsOpen != true || RenameOpen)
            return;
        var popup = new Flyout(close =>
            {
                var content = new WallpaperRenamePopup(this, light, close);
                return content;
            })
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false,
        };
        _rename = popup;
        popup.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(480f));
        popup.Automation(new()
        {
            Name = Resources.WallpaperRename,
            AutomationId = "wallpaper.rename.dialog",
            IsDialog = true
        });
        popup.ShowAt(Window, new Point(Window.ClientSize.Width * .5f, Window.ClientSize.Height * .5f));
    }

    internal void Commit(bool light, WallpaperItem item)
    {
        if (!Enabled)
            return;
        Store.Commit(light, item);
        RefreshCurrent();
        Readability.Invalidate();
        Changed?.Invoke();
    }

    internal void Rename(bool light, string name)
    {
        if (!Enabled)
            return;
        Store.Rename(light, name);
        RefreshCurrent();
        Changed?.Invoke();
    }

    internal void DeleteCustom(bool light)
    {
        if (!Enabled)
            return;
        Store.DeleteCustom(light);
        RefreshCurrent();
        Readability.Invalidate();
        Changed?.Invoke();
    }

    internal void Reset(bool light)
    {
        try
        {
            Commit(light, WallpaperStore.BuiltIns(light)[0]);
        }
        catch (Exception)
        {
            LauncherToast.ShowError(Window, Resources.WallpaperSaveError);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshCurrent();
        if (!Enabled)
        {
            _rename?.Hide();
            _picker?.Hide();
        }
        else
        {
            _picker?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(840f));
            _rename?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(0f).MaxWidth(480f));
        }

        Readability.Invalidate();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _languageEffect.Dispose();
        Window.ActualThemeChanged -= OnThemeChanged;
        _rename?.Hide();
        _picker?.Hide();
        Readability.Dispose();
    }
}