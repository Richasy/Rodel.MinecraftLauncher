// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class ImageCacheSection : Control
{
    private readonly AppImageCache _cache;
    private readonly Window _window;

    internal ImageCacheSection(AppImageCache cache, Window window)
    {
        _cache = cache;
        _window = window;
        Error = string.Empty;
        Compact = window.ClientSize.Width < 1100f;
    }

    private partial bool Compact { get; set; }
    private partial long? Bytes { get; set; }
    private partial bool Busy { get; set; }
    private partial bool Clearing { get; set; }
    private partial bool Cleared { get; set; }
    private partial string Error { get; set; }
    private bool _disposed;
    private string SizeText => string.Format(Resources.ImageCacheSize, Busy && !Clearing
            ? Resources.ImageCacheMeasuring
            : Bytes is { } bytes ? AppImageCache.FormatSize(bytes) : Resources.ImageCacheUnmeasured);
    private string ErrorText => string.Format(Clearing ? Resources.ImageCacheClearFailed : Resources.ImageCacheMeasureFailed, Error);

    private Ui Body => Column([
        Grid(
            Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Auto],
            Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto],
            [
                Column([
                    Text(Resources.ImageCacheHeading)
                        .FontSize(14f).Foreground(LauncherTheme.Ink),
                    Text(Resources.ImageCacheDescription)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap(),
                    Text(SizeText)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Wrap()
                        .Automation(new()
                        {
                            Name = SizeText,
                            AutomationId = "settings.cache.size",
                            LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                        }),
                ])
                    .Spacing(6f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                LauncherControls.OutlineCommand(Busy && Clearing ? Resources.ImageCacheClearing : Resources.ImageCacheClear, () => _ = ClearAsync())
                    .Enabled(!Busy && Bytes is not 0)
                    .MinHeight(40f)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = Resources.ImageCacheClear,
                        AutomationId = "settings.cache.clear",
                        HelpText = _cache.DirectoryPath
                    })
                    .Cell(Compact ? 0 : 1, Compact ? 1 : 0),
            ])
            .ColumnSpacing(16f).RowSpacing(10f),
        Text(ErrorText)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(Error.Length > 0)
            .Automation(new()
            {
                Name = ErrorText,
                AutomationId = "settings.cache.error",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
        Text(Resources.ImageCacheCleared)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(Cleared)
            .Automation(new()
            {
                Name = Resources.ImageCacheCleared,
                AutomationId = "settings.cache.cleared",
                LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
            }),
    ])
        .Spacing(10f);

    internal async Task RefreshAsync()
    {
        if (Busy || _disposed)
            return;
        Busy = true;
        Clearing = false;
        Cleared = false;
        Error = string.Empty;
        Bytes = null;
        try
        {
            var size = await _cache.GetSizeAsync();
            if (!_disposed)
                Bytes = size;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_disposed)
                Error = exception.Message;
        }
        finally
        {
            if (!_disposed)
                Busy = false;
        }
    }

    private async Task ClearAsync()
    {
        if (Busy || _disposed)
            return;
        Busy = true;
        Clearing = true;
        Cleared = false;
        Error = string.Empty;
        try
        {
            var remaining = await _cache.ClearAsync();
            if (_disposed)
                return;
            Bytes = remaining;
            if (remaining != 0)
                throw new IOException(string.Format(Resources.ImageCacheRemaining, AppImageCache.FormatSize(remaining)));
            Cleared = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_disposed)
                Error = exception.Message;
        }
        finally
        {
            if (!_disposed)
                Busy = false;
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs args) => Compact = args.Size.Width < 1100f;

    protected override IDisposable? OnMounted()
    {
        _window.SizeChanged += OnSizeChanged;
        return null;
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}