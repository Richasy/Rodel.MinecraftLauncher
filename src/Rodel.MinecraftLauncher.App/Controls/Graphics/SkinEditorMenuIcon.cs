// Copyright (c) Richasy. All rights reserved.

using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Graphics;

/// <summary>Identifies skin editing with a square-neck shirt and an overlapping pixel-editing pencil.</summary>
internal sealed class SkinEditorMenuIcon : IconElement
{
    private static readonly PathData Geometry = PathData.Parse("M8 3 H5 L1 7 L4 10 L6 8 V20 H10 M8 3 V5 H14 V3 H17 L21 7 L18 10 L16 8 V10 " + "M20 11 L23 14 L16 21 L12 22 L13 18 Z M18 13 L21 16");

    /// <inheritdoc/>
    protected override Widget BuildRenderCore(Brush? inheritedForeground)
    {
        var icon = new LineIconWidget(Geometry, 20f, false);
        icon.SetInheritedForeground(ResolveForeground(inheritedForeground));
        return icon;
    }
}