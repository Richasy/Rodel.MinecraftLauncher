// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal readonly record struct WallpaperAppearance(
    Color Ink,
    Color Muted,
    Color SelectedInk,
    Color Edge,
    ButtonStyle Command,
    ButtonStyle Icon,
    ButtonStyle SelectedRow,
    ButtonStyle NormalRow,
    ButtonStyle Primary,
    ButtonStyle Secondary,
    TextBoxStyle Input)
{
    internal static WallpaperAppearance Capture() => new(
        LauncherTheme.Ink,
        LauncherTheme.Muted,
        LauncherTheme.SelectionForeground,
        LauncherTheme.Edge,
        LauncherButtons.Button,
        LauncherButtons.IconButton,
        LauncherButtons.InstanceRow(true).Padding(0f),
        LauncherButtons.InstanceRow(false).Padding(0f),
        LauncherButtons.DialogPrimary,
        LauncherButtons.DialogSecondary,
        LauncherStyles.Input);
}