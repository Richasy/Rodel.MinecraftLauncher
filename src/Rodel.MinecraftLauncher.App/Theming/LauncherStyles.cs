// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Shared typography, input and selection styles built from the active semantic colors.</summary>
internal static class LauncherStyles
{
    internal const string UiFontFamily = LauncherTextStyles.UiFontFamily;
    internal static ProgressBarStyle Progress => LauncherTextStyles.Progress;
    internal static TextStyle Text => LauncherTextStyles.Text;
    internal static TextBoxPalette InputPalette => LauncherTextStyles.InputPalette;

    internal const float InputHeight = LauncherTextStyles.InputHeight;
    internal const float InputFontSize = LauncherTextStyles.InputFontSize;
    internal static readonly Thickness InputPadding = LauncherTextStyles.InputPadding;
    /// <summary>Uses launcher surface, accent and focus colors for native single-choice options in every interaction state.</summary>
    internal static RadioButtonPalette RadioChoice => LauncherAppearance.Capture().Styles.RadioChoice;
    internal static TextBoxStyle Input => LauncherTextStyles.Input;
    internal static NumberBoxStyle NumberInput => LauncherAppearance.Capture().Styles.NumberInput;
    internal static SegmentedStyle Segmented => LauncherAppearance.Capture().Styles.Segmented;
    internal static ToggleSwitchPalette ContentToggle => LauncherAppearance.Capture().Styles.ContentToggle;
    internal static CheckBoxStyle Checkbox => LauncherAppearance.Capture().Styles.Checkbox;
}