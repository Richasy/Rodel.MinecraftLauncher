// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class ContentMaintenanceSection : Control
{
    private readonly LauncherClient _client;
    private readonly HomeController _home;
    private readonly Window _window;
    private readonly LauncherSettingsCard _cleanup;
    private readonly LauncherSettingsCard _images;
    private readonly LauncherSettingsGroup _group;
    private readonly ImageCacheSection _imageCache;

    internal ContentMaintenanceSection(
        LauncherClient client,
        HomeController home,
        Window window,
        ImageCacheSection images)
    {
        _client = client;
        _home = home;
        _window = window;
        _imageCache = images;
        _cleanup = new(Resources.CacheCleanupTitle, new CacheCleanupSetting(this), window, () => Resources.CacheCleanupTitle);
        _images = new(Resources.ImageCacheHeading, images, window, () => Resources.ImageCacheHeading);
        _group = new(
            Resources.SettingsCacheGroup,
            Resources.SettingsCacheHelp,
            string.Empty,
            null,
            null,
            expanded: true,
            window: window,
            localizedHeader: () => Resources.SettingsCacheGroup,
            localizedDescription: () => Resources.SettingsCacheHelp,
            items: () => [new(_cleanup), new(_images)]);
    }
    private Flyout? _popup;
    private PopupFocusRecovery? _focus;
    private bool _disposed;
    private partial long? Bytes { get; set; }
    private partial bool Measuring { get; set; }
    internal partial bool Compact { get; set; }
    internal string Size => string.Format(Resources.ImageCacheSize, Measuring
            ? Resources.ImageCacheMeasuring
            : Bytes is { } bytes ? AppImageCache.FormatSize(bytes) : Resources.ImageCacheUnmeasured);

    private Ui Body => Border(_group);

    internal void Expand() => _group.Expand();

    internal void ShowCleanup()
    {
        if (ModalPopup.IsOpen(_window))
            return;
        var returnFocus = _window.FocusedElement;
        CacheCleanupDialog? content = null;
        _popup = new Flyout(close => content = new(_client, _home, _window, close))
        {
            Placement = PopupPlacement.Full,
            IsModal = true,
            IsLightDismissEnabled = false
        }.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup).Automation(new()
        {
            Name = Resources.CacheCleanupTitle,
            AutomationId = "settings.cleanup.popup",
            IsDialog = true
        });
        _popup.Closing += e =>
        {
            e.Cancel = content?.CanClose == false;
            if (e.Cancel)
                content?.RequestClose();
        };
        _popup.Closed += () =>
        {
            _popup = null;
            _focus?.Request(this, returnFocus);
            _ = RefreshAsync();
        };
        ModalPopup.TryShow(_window, _popup);
    }

    internal async Task RefreshAsync()
    {
        await Task.WhenAll(RefreshDownloadCacheAsync(), _imageCache.RefreshAsync());
    }

    private async Task RefreshDownloadCacheAsync()
    {
        if (Measuring || _disposed)
            return;
        Measuring = true;
        try
        {
            var status = await Task.Run(() => _client.Maintenance.CacheStatus());
            if (!_disposed)
                Bytes = status.Bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_disposed)
                Bytes = null;
        }
        finally
        {
            if (!_disposed)
                Measuring = false;
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Compact = args.Size.Width < 1100f;

    protected override IDisposable? OnMounted()
    {
        _focus = new(_window);
        Compact = _window.ClientSize.Width < 1100f;
        _window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _window.SizeChanged -= OnSizeChanged;
        _focus?.Dispose();
        base.OnDispose();
    }
}

internal sealed partial class CacheCleanupSetting(ContentMaintenanceSection owner) : Control
{
    private Ui Body => Grid(
        owner.Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Auto],
        owner.Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto],
        [
            Column([
                Text(Resources.CacheCleanupTitle)
                    .FontSize(14f).Foreground(LauncherTheme.Ink),
                Text(Resources.CacheCleanupDescription)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
                Text(owner.Size)
                    .FontSize(12f).Foreground(LauncherTheme.Muted),
            ])
                .Spacing(6f)
                .VAlign(VerticalAlignment.Center)
                .Cell(0, 0),
            LauncherControls.OutlineCommand(Resources.CacheCleanupManage, () => owner.ShowCleanup())
                .MinHeight(40f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    Name = Resources.CacheCleanupManage,
                    AutomationId = "settings.cleanup.open"
                })
                .Cell(owner.Compact ? 0 : 1, owner.Compact ? 1 : 0),
        ])
        .ColumnSpacing(16f).RowSpacing(10f);
}