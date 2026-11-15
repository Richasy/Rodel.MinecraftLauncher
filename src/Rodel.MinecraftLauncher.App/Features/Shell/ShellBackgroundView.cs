// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Wallpapers;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Shell;

[View]
internal readonly partial record struct ShellBackgroundView(
    string Page,
    bool Light,
    bool HighContrast,
    ImageSource World,
    ImageAlignment Alignment,
    Brush Background,
    Brush Shade,
    Brush HorizontalShade,
    Brush PageShade,
    WallpaperService Wallpapers,
    Size Viewport)
{
    public Ui Body => Overlay([
        Rectangle(Background),
        new WallpaperScene(
            Wallpapers,
            World,
            Viewport,
            Alignment,
            Light).Visible(Page == "home" && !HighContrast),
        Image(World)
            .Stretch(ImageStretch.UniformToFill)
            .Alignment(Alignment)
            .Blur(Page == "home"
                ? 0f
                : Light ? 30f : 40f)
            .Visible(Page != "home" && !HighContrast),
        Rectangle(Shade)
            .Visible(Page != "home" && !HighContrast),
        Rectangle(HorizontalShade)
            .Visible(Page != "home" && !HighContrast),
        Rectangle(PageShade)
            .Visible(Page != "home" && !HighContrast),
    ]);
}