// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed partial class WallpaperRenamePopup : Control
{
    private readonly WallpaperService _service;
    private readonly bool _light;
    private readonly Action _close;
    internal readonly WallpaperNameInput Input;

    internal WallpaperRenamePopup(WallpaperService service, bool light, Action close)
    {
        _service = service;
        _light = light;
        _close = close;
        Name = service.Store.Custom(light)?.Name ?? string.Empty;
        Appearance = WallpaperAppearance.Capture();
        Viewport = service.Window.ClientSize;
        Input = new(service.Window, value => Name = value, value =>
            {
                Name = value;
                Save();
            })
        {
            Value = Name,
            Appearance = Appearance.Input
        };
    }

    public partial string Name { get; set; }
    public partial WallpaperAppearance Appearance { get; set; }
    public partial Size Viewport { get; set; }

    private Ui Body => Column([
        Border(Text(Resources.WallpaperRename)
                .FontSize(19f).FontWeight(600).Foreground(Appearance.Ink))
            .Padding(new Thickness(24f, 20f, 24f, 16f))
            .BorderBrush(Appearance.Edge)
            .BorderThickness(0f, 0f, 0f, 1f),
        Column([
            Text(Resources.WallpaperName)
                .FontSize(12f).Foreground(Appearance.Ink),
            Input.MinHeight(40f)
                .Automation(new()
                {
                    Name = Resources.WallpaperName,
                    AutomationId = "wallpaper.rename.name"
                }),
        ])
            .Spacing(8f)
            .Padding(new Thickness(24f, 20f, 24f, 20f)),
        Border(Grid(
                [GridLength.Star(), GridLength.Star()],
                [GridLength.Auto],
                [
                    LauncherControls.Command(Resources.WallpaperSave, () => Save())
                        .Style(Appearance.Primary)
                        .MinHeight(40f)
                        .Enabled(Name.Trim()
                            .Length is> 0 and <= 80)
                        .Automation(new()
                        {
                            Name = Resources.WallpaperSave,
                            AutomationId = "wallpaper.rename.save"
                        })
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(Resources.WallpaperCancel, _close)
                        .Style(Appearance.Secondary)
                        .MinHeight(40f)
                        .Automation(new()
                        {
                            Name = Resources.WallpaperCancel,
                            AutomationId = "wallpaper.rename.cancel"
                        })
                        .Cell(1, 0),
                ])
                .ColumnSpacing(12f))
            .Padding(new Thickness(24f, 16f, 24f, 20f))
            .BorderBrush(Appearance.Edge)
            .BorderThickness(0f, 1f, 0f, 0f),
    ])
        .Width(Math.Min(480f, Viewport.Width - 48f));

    private void Save()
    {
        if (!_service.Enabled || Name.Trim().Length is 0 or > 80)
            return;
        try
        {
            _service.Rename(_light, Name);
            _close();
        }
        catch (Exception)
        {
            LauncherToast.ShowError(_service.Window, Resources.WallpaperRenameError);
        }
    }

    private void Refresh()
    {
        Appearance = WallpaperAppearance.Capture();
        Input.Appearance = Appearance.Input;
    }

    private void OnSizeChanged(object? sender, WindowSizeChangedEventArgs e) => Viewport = e.Size;

    protected override IDisposable? OnMounted()
    {
        _service.Changed += Refresh;
        _service.Window.SizeChanged += OnSizeChanged;
        return PopupKeyboardClose.Bind(_service.Window, _close, () => _service.Enabled);
    }

    protected override void OnDispose()
    {
        _service.Changed -= Refresh;
        _service.Window.SizeChanged -= OnSizeChanged;
        base.OnDispose();
    }
}