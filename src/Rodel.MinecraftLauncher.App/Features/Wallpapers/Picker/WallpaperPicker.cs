// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed partial class WallpaperPicker : Control
{
    private readonly WallpaperService _service;
    private readonly bool _light;
    private readonly Action _close;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WallpaperGrid _grid;
    private bool _disposed;
    private FileDropHandler Drop => field ??= new(AcceptDrop, ReceiveDrop, value => Dropping = value);
    private partial bool Dropping { get; set; }
    internal readonly ScrollView Gallery;

    internal WallpaperPicker(WallpaperService service, bool light, Action close)
    {
        _service = service;
        _light = light;
        _close = close;
        Viewport = service.Window.ClientSize;
        Appearance = WallpaperAppearance.Capture();
        _grid = new(
            service.Store.Items(light),
            service.Store.Current(light).Id,
            TileWidth,
            false,
            Choose,
            Columns,
            Appearance,
            BuildMenu);
        Gallery = new(_grid);
    }

    /// <summary>Palette and localized text captured for the current popup.</summary>
    public partial WallpaperAppearance Appearance { get; set; }
    /// <summary>Disables selection while an imported image is being prepared.</summary>
    public partial bool Busy { get; set; }
    /// <summary>Available client area used to bound and reflow the gallery.</summary>
    public partial Size Viewport { get; set; }
    private int Columns => Viewport.Width < 1000 ? 2 : 3;
    private float Width => Math.Min(840f, Viewport.Width - 48f);
    private float TileWidth => (Width - 40f - ((Columns - 1) * 14f)) / Columns;

    private void SetBusy(bool busy)
    {
        Busy = busy;
        _grid.Busy = busy;
    }

    private Ui Body => Column([
        Row([
            Text(_light ? Resources.WallpaperChooseLight : Resources.WallpaperChooseDark)
                .FontSize(22f).FontWeight(600).Foreground(Appearance.Ink)
                .Weight(1f),
            LauncherControls.OutlineCommand(Resources.WallpaperImport, () => _ = ImportAsync())
                .Style(LauncherButtons.DialogHeaderButton)
                .Enabled(!Busy)
                .Automation(new()
                {
                    Name = Resources.WallpaperImport,
                    AutomationId = "wallpaper.import"
                }),
            LauncherControls.OutlineCommand(
                Row([
                    LauncherIcons.Line("close", 16f)
                ]),
                _close)
                .Style(LauncherButtons.PopupCloseButton)
                .Width(32f).Height(32f)
                .Enabled(!Busy)
                .ToolTip(Resources.CloseLaunchSettings)
                .Automation(new()
                {
                    Name = Resources.CloseLaunchSettings,
                    AutomationId = "wallpaper.close"
                }),
        ])
            .Spacing(12f)
            .Padding(new Thickness(20f, 20f, 20f, 16f)),
        DropTarget(Column([Gallery.MaxHeight(Math.Max(100f, Viewport.Height - 180f)), new FileDropHint(LauncherAppearance.Capture(),Dropping)]).Spacing(8f), Drop)
            .Margin(new Thickness(0f, 0f, 0f, 20f))
            .Automation(new()
            {
                Name = Resources.WallpaperHeading,
                AutomationId = "wallpaper.gallery"
            }),
    ])
        .Width(Width);

    private void Choose(string id)
    {
        if (Busy || _service.RenameOpen || !_service.Enabled)
            return;
        try
        {
            var item = _service.Store.Items(_light).First(entry => entry.Id == id);
            _service.Commit(_light, item);
            _close();
        }
        catch (Exception)
        {
            LauncherToast.ShowError(_service.Window, Resources.WallpaperSaveError);
        }
    }

    private bool AcceptDrop(IReadOnlyList<string> paths) => !Busy && !_service.RenameOpen && _service.Enabled
        && paths.Count == 1 && File.Exists(paths[0]) && System.IO.Path.GetExtension(paths[0]).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    private void ReceiveDrop(IReadOnlyList<string> paths) => _ = ImportAsync(paths[0]);

    private async Task ImportAsync(string? source = null)
    {
        if (Busy || _service.RenameOpen || !_service.Enabled || _service.Images is not { } images)
            return;
        SetBusy(true);
        var token = _lifetime.Token;
        WallpaperItem? imported = null;
        var committed = false;
        try
        {
            if (source is null)
            {
                var picker = _service.Window.CreateFileOpenPicker();
                foreach (var extension in new[]
                {
                ".png",
                ".jpg",
                ".jpeg",
                ".webp"
            }
                )
                {
                    picker.FileTypeFilter.Add(extension);
                }

                var file = await picker.PickSingleFileAsync();
                source = file?.Path;
            }

            if (source is null || token.IsCancellationRequested || !_service.Enabled)
                return;
            imported = await _service.Store.ImportAsync(source, images, token);
            token.ThrowIfCancellationRequested();
            if (!_service.Enabled)
                return;
            _service.Commit(_light, imported);
            committed = true;
            _close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                LauncherToast.ShowError(_service.Window, Resources.WallpaperImportError);
        }
        finally
        {
            if (!committed && imported is not null)
                WallpaperStore.TryDelete(imported.Path);
            if (!_disposed)
                SetBusy(false);
        }
    }

    private MenuFlyout BuildMenu(WallpaperItem item)
    {
        if (!item.Custom || Busy || _service.RenameOpen || !_service.Enabled)
            return null!;
        var rename = false;
        var menu = new MenuFlyout(new MenuFlyoutItem(Resources.WallpaperRename, () => rename = true)
        {
            Automation = new()
            {
                Name = Resources.WallpaperRename,
                AutomationId = "wallpaper.custom.rename"
            }
        }, new MenuFlyoutItem(Resources.WallpaperDelete, () => DeleteCustom())
        {
            Automation = new()
            {
                Name = Resources.WallpaperDelete,
                AutomationId = "wallpaper.custom.delete"
            }
        }).Style(LauncherMenus.CommandMenu).Automation(new()
        {
            Name = Resources.WallpaperCustom,
            AutomationId = "wallpaper.custom.menu"
        });
        menu.Closed += () =>
        {
            if (rename && !_disposed)
                _service.ShowRename(_light);
        };
        return menu;
    }

    private void DeleteCustom()
    {
        try
        {
            _service.DeleteCustom(_light);
            LauncherToast.Show(_service.Window, Resources.WallpaperDeleted);
        }
        catch (Exception)
        {
            LauncherToast.ShowError(_service.Window, Resources.WallpaperDeleteError);
        }
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e)
    {
        Viewport = e.Size;
        _grid.Columns = Columns;
        _grid.TileWidth = TileWidth;
    }

    private void OnChanged()
    {
        Appearance = WallpaperAppearance.Capture();
        _grid.Appearance = Appearance;
        _grid.Items = _service.Store.Items(_light);
        _grid.Candidate = _service.Store.Current(_light).Id;
    }

    protected override IDisposable? OnMounted()
    {
        _service.Window.SizeChanged += OnSizeChanged;
        _service.Changed += OnChanged;
        OnChanged();
        return PopupKeyboardClose.Bind(_service.Window, _close, () => !Busy && !_service.RenameOpen);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _service.Window.SizeChanged -= OnSizeChanged;
        _service.Changed -= OnChanged;
        base.OnDispose();
    }
}