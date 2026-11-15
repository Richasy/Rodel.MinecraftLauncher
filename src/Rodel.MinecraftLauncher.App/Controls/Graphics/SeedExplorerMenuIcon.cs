// Copyright (c) Richasy. All rights reserved.

using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Graphics;

/// <summary>Identifies seed exploration with a folded map and an overlapping location pin.</summary>
internal sealed class SeedExplorerMenuIcon : IconElement
{
    private static readonly PathData Geometry = PathData.Parse(
        "M2 5 L8 3 L14 5 L21 3 V8 M21 18 V19 L14 21 L8 19 L2 21 V5 M8 3 V19 M14 5 V9 M14 18 V21 " +
        "M23 12 A4 4 0 1 0 15 12 C15 14 19 18 19 18 C19 18 23 14 23 12 Z M19 11 V12");

    /// <inheritdoc/>
    protected override Widget BuildRenderCore(Brush? inheritedForeground)
    {
        var icon = new LineIconWidget(Geometry, 20f, false);
        icon.SetInheritedForeground(ResolveForeground(inheritedForeground));
        return icon;
    }
}