// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Shares the painted rim widths and the corresponding unobscured face center.</summary>
internal static class BevelGeometry
{
    internal static Thickness Edges(bool inset, bool primary, bool enabled) => LauncherAppearance.Capture().Bevel.Edges(inset, primary, enabled);
    internal static Sprout.Controls.ControlContentOffsets ContentOffsets(bool primary = false) => LauncherAppearance.Capture().Bevel.ContentOffsets(primary);
    internal static Point Offset(bool inset, bool primary, bool enabled) => LauncherAppearance.Capture().Bevel.Offset(inset, primary, enabled);
    internal static Point FaceCenter(Thickness edges) => LauncherBevelGeometry.FaceCenter(edges);
    internal static Thickness CenterContent(Thickness chrome, bool hasDepth, bool inset, bool primary, bool enabled) => LauncherAppearance.Capture().Bevel.CenterContent(chrome, hasDepth, inset, primary, enabled);
}