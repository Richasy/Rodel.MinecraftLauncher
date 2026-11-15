// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Pure input and selection styles derived from a retained launcher appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct LauncherTextStyles(LauncherAppearance Theme)
{
    internal const string UiFontFamily = "Microsoft YaHei UI";
    internal static ProgressBarStyle Progress => default(ProgressBarStyle).Foreground(Brush.Theme(ThemeColorToken.SystemFillColorSuccess));
    internal static TextStyle Text => default(TextStyle).FontFamily(UiFontFamily).FontSize(14f);
    internal static TextBoxPalette InputPalette => TextBoxPalette.Default with
    {
        RestingElevation = ElevationProfile.TextField(ThemeColorToken.ControlStrokeColorDefault, ThemeColorToken.ControlStrokeColorDefault),
        FocusedElevation = ElevationProfile.TextField(ThemeColorToken.TextFillColorPrimary, ThemeColorToken.TextFillColorPrimary),
    };

    internal const float InputHeight = 40f;
    internal const float InputFontSize = 14f;
    internal static readonly Thickness InputPadding = new(11f, 6f, 11f, 6f);
    /// <summary>Uses launcher surface, accent and focus colors for native single-choice options in every interaction state.</summary>
    internal RadioButtonPalette RadioChoice
    {
        get
        {
            var off = new RadioButtonVisualState(Theme.ControlBackground, Theme.Edge, Colors.Transparent, Theme.Ink);
            var on = new RadioButtonVisualState(Theme.PrimaryBackground, Theme.PrimaryBackground, Theme.PrimaryForeground, Theme.Ink);
            var disabled = new RadioButtonVisualState(Theme.ControlBackground, Theme.Edge, Theme.Quiet, Theme.Quiet);
            return new(new(off, off with { OuterStroke = Theme.Focus }, off with { OuterStroke = Theme.Focus }, disabled), new(on, on with { OuterStroke = Theme.Focus }, on with { OuterStroke = Theme.Focus }, disabled));
        }
    }

    internal static TextBoxStyle Input => default(TextBoxStyle).ControlCornerRadius(CornerRadius.Zero).ContentFontSize(InputFontSize).ContentPadding(InputPadding).ColorPalette(InputPalette);
    internal NumberBoxStyle NumberInput => default(NumberBoxStyle).Palette(Sprout.Controls.NumberBoxPalette.FromTheme(Theme.Palette) with { TextBox = InputPalette });
    internal SegmentedStyle Segmented => default(SegmentedStyle).FontFamily(UiFontFamily).FontSize(12f).FontWeight(500).Palette(Sprout.Controls.SegmentedPalette.FromTheme(Theme.Palette) with
    {
        Track = Theme.HighContrast ? Theme.HC.SystemColorButtonFaceColor : Theme.InsetDark.MultiplyAlpha(.18f),
        Border = Theme.Edge,
        Pill = Colors.Transparent,
        PillDisabled = Colors.Transparent,
        PillBorder = Colors.Transparent,
        Foreground = Theme.HighContrast ? Theme.HC.SystemColorButtonTextColor : Theme.Muted,
        ForegroundPointerOver = Theme.HighContrast ? Theme.HC.SystemColorHighlightTextColor : Theme.Ink,
        ForegroundSelected = Theme.SelectionForeground,
        ForegroundPressed = Theme.HighContrast ? Theme.HC.SystemColorWindowColor : Theme.SelectionForeground,
        ForegroundDisabled = Theme.Quiet,
    });
    internal ToggleSwitchPalette ContentToggle => Theme.HighContrast ? ToggleSwitchPalette.FromTheme(Theme.Palette) : new(new(new(Theme.ControlBackground, Theme.ControlBackground, Theme.SelectionBackground, Theme.ControlBackground), new(Theme.Edge, Theme.Focus, Theme.Focus, Theme.Edge), new(Theme.Muted, Theme.Ink, Theme.Ink, Theme.Quiet), null), new(new(Theme.PrimaryBackground, Theme.PrimaryBackground, Theme.PrimaryBackground, Theme.ControlBackground), new(Theme.PrimaryBackground, Theme.Focus, Theme.Focus, Theme.Edge), new(Theme.PrimaryForeground, Theme.PrimaryForeground, Theme.PrimaryForeground, Theme.Quiet), null));
    internal CheckBoxStyle Checkbox => default(CheckBoxStyle).BoxSize(0f).BoxCornerRadius(0f).ContentSpacing(0f)
        .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center).Palette(CheckBoxPalette.FromTheme(Theme.Palette) with
        {
            Unchecked = CheckboxStates,
            Checked = CheckboxStates,
            Indeterminate = CheckboxStates,
        });
    private CheckBoxStatePalette CheckboxStates => new(new(Colors.Transparent, Colors.Transparent, Theme.PrimaryForeground, Theme.Ink), new(Colors.Transparent, Colors.Transparent, Theme.PrimaryForeground, Theme.Ink), new(Colors.Transparent, Colors.Transparent, Theme.PrimaryForeground, Theme.Ink), new(Colors.Transparent, Colors.Transparent, Theme.Quiet, Theme.Quiet));
}