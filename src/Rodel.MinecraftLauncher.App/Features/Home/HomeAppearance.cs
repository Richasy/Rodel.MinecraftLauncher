// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal readonly record struct HomeAppearance(
    Color Ink,
    Color SceneInk,
    Color SceneMuted,
    Color SceneQuiet,
    Color Muted,
    Color Quiet,
    Color Placeholder,
    Color Edge,
    Color Error,
    Color Progress,
    Brush Surface,
    DropShadow Shadow,
    ButtonStyle Primary,
    ButtonStyle Command,
    ButtonStyle Icon,
    ButtonStyle SelectedRow,
    ButtonStyle NormalRow)
{
    internal static HomeAppearance Capture() => new(
        LauncherTheme.Ink,
        LauncherTheme.SceneInk,
        LauncherTheme.SceneMuted,
        LauncherTheme.SceneQuiet,
        LauncherTheme.Muted,
        LauncherTheme.Quiet,
        LauncherTheme.EmptyPlaceholder,
        LauncherTheme.Edge,
        LauncherTheme.Error,
        LauncherTheme.PrimaryBackground,
        LauncherMaterials.Acrylic,
        LauncherMaterials.Shadow,
        LauncherButtons.Primary,
        LauncherButtons.Button,
        LauncherButtons.IconButton,
        LauncherButtons.InstanceRow(true),
        LauncherButtons.InstanceRow(false));
}