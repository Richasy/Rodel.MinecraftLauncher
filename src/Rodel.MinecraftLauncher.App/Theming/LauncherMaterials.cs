// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Cached brushes and surface materials selected by the active theme without freezing its colors.</summary>
internal static class LauncherMaterials
{
    internal static Brush Acrylic => LauncherAppearance.Capture().Materials.Acrylic;
    internal static DropShadow Shadow => LauncherAppearance.Capture().Materials.Shadow;
    internal static Brush Shade => LauncherAppearance.Capture().Materials.Shade;
    internal static Brush HorizontalShade => LauncherAppearance.Capture().Materials.HorizontalShade;
    internal static Brush FaceGradient => LauncherAppearance.Capture().Materials.FaceGradient;
    internal static Brush InsetGradient => LauncherAppearance.Capture().Materials.InsetGradient;
    internal static Brush SelectionInsetGradient => LauncherAppearance.Capture().Materials.SelectionInsetGradient;
    internal static Controls.Buttons.LauncherSegmentedMaterial SegmentedMaterial => LauncherAppearance.Capture().Materials.SegmentedMaterial;
}