// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Button interaction colors and bevel states, including the system high-contrast path.</summary>
internal static class LauncherButtonPalette
{
    internal static ButtonPalette Create(bool primary, bool selected) => LauncherAppearance.Capture().ButtonPalette.Create(primary, selected);
}