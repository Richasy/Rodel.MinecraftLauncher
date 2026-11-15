// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed partial class WallpaperSection : Control
{
    private readonly WallpaperService _service;

    internal WallpaperSection(WallpaperService service)
    {
        _service = service;
        LightItem = service.Store.Current(true);
        DarkItem = service.Store.Current(false);
        Enabled = service.Enabled;
        Appearance = WallpaperAppearance.Capture();
    }

    public partial WallpaperItem LightItem { get; set; }
    public partial WallpaperItem DarkItem { get; set; }
    public partial bool Enabled { get; set; }
    public partial WallpaperAppearance Appearance { get; set; }

    private Ui Body => Column([
        Text(Resources.WallpaperHeading)
            .FontSize(14f).Foreground(LauncherTheme.Ink),
        Border(Text(Resources.WallpaperHighContrast)
                .FontSize(12f).Foreground(LauncherTheme.Ink)
                .Wrap()
                .Align(TextAlignment.Center)
                .VAlign(VerticalAlignment.Center))
            .Padding(24f)
            .MinHeight(120f)
            .BorderBrush(LauncherTheme.Edge)
            .BorderThickness(1f)
            .Visible(!Enabled),
        Grid(
            [GridLength.Star(), GridLength.Star()],
            [GridLength.Auto],
            [
                new WallpaperSlotView(
                    LauncherAppearance.Capture(),
                    LightItem,
                    true,
                    Enabled,
                    Appearance,
                    () => _service.Show(true),
                    () => _service.Reset(true)).Cell(0, 0),
                new WallpaperSlotView(
                    LauncherAppearance.Capture(),
                    DarkItem,
                    false,
                    Enabled,
                    Appearance,
                    () => _service.Show(false),
                    () => _service.Reset(false)).Cell(1, 0),
            ])
            .ColumnSpacing(12f)
            .Visible(Enabled),
    ])
        .Spacing(12f)
        .Margin(new Thickness(0f, 8f, 0f, 8f));

    private void Refresh()
    {
        LightItem = _service.Store.Current(true);
        DarkItem = _service.Store.Current(false);
        Enabled = _service.Enabled;
        Appearance = WallpaperAppearance.Capture();
    }

    protected override IDisposable? OnMounted()
    {
        _service.Changed += Refresh;
        Refresh();
        return null;
    }

    protected override void OnDispose()
    {
        _service.Changed -= Refresh;
        base.OnDispose();
    }
}