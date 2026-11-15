// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Controls;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

internal static class LauncherSplitButtonSurface
{
    internal static void Paint(ISceneRenderer renderer, SplitButtonSurface surface) => Paint(renderer, surface, false);

    internal static void PaintAccent(ISceneRenderer renderer, SplitButtonSurface surface) => Paint(renderer, surface, true);

    private static void Paint(ISceneRenderer renderer, SplitButtonSurface surface, bool primary)
    {
        var palette = LauncherButtonPalette.Create(primary, false);
        PaintHalf(surface.Layout.Primary with { Right = surface.Layout.Secondary.Left }, surface.PrimaryBackground, surface.IsPrimaryPressed);
        PaintHalf(surface.Layout.Secondary, surface.SecondaryBackground, surface.IsSecondaryPressed);
        void PaintHalf(Rect bounds, Brush background, bool pressed)
        {
            var inset = surface.IsEnabled && pressed;
            var border = !surface.IsEnabled
                ? palette.Disabled.Border
                : inset ? palette.Pressed.Border : palette.Normal.Border;
            BevelWidget.PaintSurface(
                renderer,
                bounds,
                background,
                border,
                true,
                true,
                inset,
                primary,
                surface.IsEnabled);
        }
    }
}