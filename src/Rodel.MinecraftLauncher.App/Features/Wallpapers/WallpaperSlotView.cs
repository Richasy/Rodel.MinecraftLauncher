// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

[View]
internal readonly partial record struct WallpaperSlotView(
    LauncherAppearance Theme,
    WallpaperItem Item,
    bool Light,
    bool Enabled,
    WallpaperAppearance Appearance,
    Action Open,
    Action Reset)
{
    public Ui Body => Border(Column([
            Text(Light ? Resources.WallpaperLight : Resources.WallpaperDark)
                .FontSize(12f).Foreground(Appearance.Ink),
            Image(Item.Thumbnail)
                .Stretch(ImageStretch.Uniform)
                .Alignment(Item.Alignment)
                .MaxHeight(280f)
                .Visible(Enabled),
            Row([
                LauncherControls.Command(Theme,Resources.WallpaperChoose, Open)
                    .Style(Appearance.Command)
                    .Enabled(Enabled)
                    .MinHeight(36f)
                    .Weight(1f)
                    .Automation(new()
                    {
                        Name = Resources.WallpaperChoose,
                        AutomationId = Light ? "wallpaper.light.choose" : "wallpaper.dark.choose"
                    }),
                LauncherControls.Command(Theme,Resources.WallpaperReset, Reset)
                    .Style(Appearance.Command)
                    .Enabled(Enabled && Item.Id != WallpaperStore.DefaultId(Light))
                    .MinHeight(36f)
                    .Weight(1f)
                    .Automation(new()
                    {
                        Name = Resources.WallpaperReset,
                        AutomationId = Light ? "wallpaper.light.reset" : "wallpaper.dark.reset"
                    }),
            ])
                .Spacing(8f),
        ])
            .Spacing(10f))
        .Padding(12f)
        .BorderBrush(Appearance.Edge)
        .BorderThickness(1f);
}