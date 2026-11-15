// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Pure face geometry derived from the retained contrast and mode.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct LauncherBevelGeometry(LauncherAppearance Theme)
{
    internal Thickness Edges(bool inset, bool primary, bool enabled) => Theme.Light ? inset ? new Thickness(3f, 3f, 1f, 1f) : new Thickness(1f) : !enabled ? new Thickness(1f, 1f, 1f, 2f) : inset ? new Thickness(3f, 4f, 1f, 1f) : new Thickness(1f, 1f, 1f, primary ? 3f : 2f);
    internal Sprout.Controls.ControlContentOffsets ContentOffsets(bool primary = false) => new(Offset(false, primary, true), Offset(true, primary, true), Offset(false, primary, false));
    internal Point Offset(bool inset, bool primary, bool enabled) => Theme.HighContrast ? default : FaceCenter(Edges(inset, primary, enabled));
    internal static Point FaceCenter(Thickness edges) => new((edges.Left - edges.Right) / 2f, (edges.Top - edges.Bottom) / 2f);
    internal Thickness CenterContent(Thickness chrome, bool hasDepth, bool inset, bool primary, bool enabled)
    {
        if (Theme.HighContrast || !hasDepth)
            return chrome;
        var edges = Edges(inset, primary, enabled);
        var offset = FaceCenter(edges);
        var x = offset.X;
        var y = offset.Y;
        // Preserve total chrome, desired size and hit bounds; only the internal face center changes.
        return new Thickness(chrome.Left + x, chrome.Top + y, chrome.Right - x, chrome.Bottom - y);
    }
}