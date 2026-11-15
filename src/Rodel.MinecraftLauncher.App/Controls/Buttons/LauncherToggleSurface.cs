// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

internal static class LauncherToggleSurface
{
    internal static void Paint(ISceneRenderer renderer, ToggleSwitchSurface surface)
    {
        renderer.FillRectangle(surface.Track, surface.TrackFill);
        Frame(renderer, surface.Track, surface.TrackStroke, surface.TrackStroke);
        if (!LauncherTheme.HighContrast && surface.IsEnabled)
            Frame(renderer, surface.Track.Inflate(-1f), LauncherTheme.InsetDark, LauncherTheme.InsetLight);
        renderer.FillRectangle(surface.Knob, surface.KnobFill);
        if (!LauncherTheme.HighContrast && surface.IsEnabled)
            Frame(renderer, surface.Knob, LauncherTheme.RaisedLight, LauncherTheme.RaisedDark);
    }

    private static void Frame(
        ISceneRenderer renderer,
        Rect bounds,
        Brush top,
        Brush bottom)
    {
        renderer.FillRectangle(Rect.FromXywh(bounds.Left, bounds.Top, bounds.Width, 1f), top);
        renderer.FillRectangle(Rect.FromXywh(bounds.Left, bounds.Top, 1f, bounds.Height), top);
        renderer.FillRectangle(Rect.FromXywh(bounds.Left, bounds.Bottom - 1f, bounds.Width, 1f), bottom);
        renderer.FillRectangle(Rect.FromXywh(bounds.Right - 1f, bounds.Top, 1f, bounds.Height), bottom);
    }
}