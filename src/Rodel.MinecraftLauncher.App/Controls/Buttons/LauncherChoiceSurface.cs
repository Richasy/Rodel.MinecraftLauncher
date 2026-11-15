// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Controls;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

internal static class LauncherChoiceSurface
{
    internal static void Paint(ISceneRenderer renderer, ComboBoxSurface surface) => Draw(renderer, surface, false);

    internal static void PaintCritical(ISceneRenderer renderer, ComboBoxSurface surface) => Draw(renderer, surface, true);

    private static void Draw(ISceneRenderer renderer, ComboBoxSurface surface, bool critical)
    {
        var palette = LauncherButtonPalette.Create(false, false);
        var background = surface.IsEnabled && surface.IsPressed ? palette.Pressed.Background : surface.Background;
        var border = !surface.IsEnabled
            ? palette.Disabled.Border
            : critical
            ? LauncherTheme.Error
            : palette.Normal.Border;
        BevelWidget.PaintSurface(
            renderer,
            surface.Bounds,
            background,
            border,
            true,
            true,
            surface.IsPressed,
            false,
            surface.IsEnabled);
    }
}