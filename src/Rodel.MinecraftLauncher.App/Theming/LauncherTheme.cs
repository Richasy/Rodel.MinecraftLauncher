// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Semantic colors and system-palette overrides, resolved in the active reactive theme scope.</summary>
internal static class LauncherTheme
{
    internal static readonly Color LightInsetShadow = LauncherAppearance.LightInsetShadow;
    internal static readonly Color DarkInsetShadow = LauncherAppearance.DarkInsetShadow;
    internal static readonly Color DarkSelectionInsetShadow = LauncherAppearance.DarkSelectionInsetShadow;
    internal static ThemeColors Palette => LauncherAppearance.Capture().Palette;
    internal static bool HighContrast => LauncherAppearance.Capture().HighContrast;
    internal static bool Light => LauncherAppearance.Capture().Light;
    internal static SystemHighContrastColors HC => LauncherAppearance.Capture().HC;
    internal static Color Ink => LauncherAppearance.Capture().Ink;
    internal static Color ProgressAccent => LauncherAppearance.Capture().ProgressAccent;
    internal static Color SuccessBackground => LauncherAppearance.Capture().SuccessBackground;
    internal static Color SuccessForeground => LauncherAppearance.Capture().SuccessForeground;
    internal static Color Error => LauncherAppearance.Capture().Error;
    internal static Color Warning => LauncherAppearance.Capture().Warning;
    internal static Color Muted => LauncherAppearance.Capture().Muted;
    internal static Color Quiet => LauncherAppearance.Capture().Quiet;
    /// <summary>Gets low-emphasis empty-list placeholder ink, retaining full system contrast when required.</summary>
    internal static Color EmptyPlaceholder => LauncherAppearance.Capture().EmptyPlaceholder;
    internal static Color Edge => LauncherAppearance.Capture().Edge;
    internal static Color Focus => LauncherAppearance.Capture().Focus;
    internal static Color Background => LauncherAppearance.Capture().Background;
    internal static Color ControlBackground => LauncherAppearance.Capture().ControlBackground;
    /// <summary>Separates the player's chat messages with a flat reading surface, without selection or button depth.</summary>
    internal static Color AiUserMessageBackground => LauncherAppearance.Capture().AiUserMessageBackground;
    internal static Color SelectionBackground => LauncherAppearance.Capture().SelectionBackground;
    internal static Color SelectionForeground => LauncherAppearance.Capture().SelectionForeground;
    internal static Color PrimaryBackground => LauncherAppearance.Capture().PrimaryBackground;
    internal static Color ShadowColor => LauncherAppearance.Capture().ShadowColor;
    internal static Color RaisedLight => LauncherAppearance.Capture().RaisedLight;
    internal static Color RaisedDark => LauncherAppearance.Capture().RaisedDark;
    internal static Color InsetDark => LauncherAppearance.Capture().InsetDark;
    internal static Color InsetLight => LauncherAppearance.Capture().InsetLight;
    internal static Color SelectionInsetDark => LauncherAppearance.Capture().SelectionInsetDark;
    internal static Color SelectionInsetLight => LauncherAppearance.Capture().SelectionInsetLight;
    internal static Color PageShade => LauncherAppearance.Capture().PageShade;
    internal static Color SceneInk => LauncherAppearance.Capture().SceneInk;
    /// <summary>Gets secondary wallpaper text ink without competing with the selected target heading.</summary>
    internal static Color SceneMuted => LauncherAppearance.Capture().SceneMuted;
    /// <summary>Gets subdued wallpaper context and configuration ink, retaining system ink in high contrast.</summary>
    internal static Color SceneQuiet => LauncherAppearance.Capture().SceneQuiet;
    /// <summary>Gets the theme's text-on-accent ink, preserving highlight text in high contrast.</summary>
    internal static Color PrimaryForeground => LauncherAppearance.Capture().PrimaryForeground;

    internal static void Configure(Theme theme) => LauncherAppearance.Configure(theme);
}