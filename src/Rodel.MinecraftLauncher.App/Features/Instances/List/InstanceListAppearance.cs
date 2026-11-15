// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

/// <summary>Retains previous theme values so view reconciliation can observe theme changes.</summary>
internal readonly record struct InstanceListAppearance(
    bool HighContrast,
    Color Ink,
    Color Muted,
    Color Edge,
    Color Indicator,
    ButtonStyle Command,
    ButtonStyle Outline,
    ButtonStyle Icon,
    ButtonStyle Content)
{
    internal static InstanceListAppearance Capture() => new(
        LauncherTheme.HighContrast,
        LauncherTheme.Ink,
        LauncherTheme.Muted,
        LauncherTheme.Edge,
        LauncherTheme.HighContrast ? LauncherTheme.SelectionForeground : LauncherTheme.PrimaryBackground,
        LauncherButtons.Button,
        LauncherButtons.OutlineButton,
        LauncherButtons.IconButton,
        LauncherButtons.InstanceTitle);
}