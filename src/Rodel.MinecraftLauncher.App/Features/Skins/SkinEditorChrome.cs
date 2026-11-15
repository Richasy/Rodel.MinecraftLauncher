// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Separates the editor's lightweight navigation, selection, property, and command roles using shared theme tokens.</summary>
internal static class SkinEditorChrome
{
    internal static SkinEditorAppearance For(LauncherAppearance theme) => new(theme);
    internal static ButtonPalette QuietPalette => SkinEditorChrome.For(LauncherAppearance.Capture()).QuietPalette;
    internal static ToggleButtonPalette ToolPalette => SkinEditorChrome.For(LauncherAppearance.Capture()).ToolPalette;
    internal static ToggleButtonPalette PropertyPalette => SkinEditorChrome.For(LauncherAppearance.Capture()).PropertyPalette;
    internal static ToggleButtonPalette TextTogglePalette => SkinEditorChrome.For(LauncherAppearance.Capture()).TextTogglePalette;
    internal static ToggleButtonStyle ToolStyle => SkinEditorAppearance.ToolStyle;
    internal static ToggleButtonStyle PropertyStyle => SkinEditorAppearance.PropertyStyle;
    internal static ToggleButtonStyle TextToggleStyle => SkinEditorAppearance.TextToggleStyle;
    internal static ButtonStyle QuietCommand => SkinEditorChrome.For(LauncherAppearance.Capture()).QuietCommand;
    internal static ButtonStyle QuietIconCommand => SkinEditorChrome.For(LauncherAppearance.Capture()).QuietIconCommand;
    internal static ButtonStyle CompactCommand => SkinEditorChrome.For(LauncherAppearance.Capture()).CompactCommand;
    internal static DropDownButtonStyle TitleTrigger => SkinEditorChrome.For(LauncherAppearance.Capture()).TitleTrigger;
    internal static DropDownButtonStyle ColorTrigger => SkinEditorChrome.For(LauncherAppearance.Capture()).ColorTrigger;

    internal static ToggleButtonPalette SwatchPalette(string hex) => SkinEditorChrome.For(LauncherAppearance.Capture()).SwatchPalette(hex);
}