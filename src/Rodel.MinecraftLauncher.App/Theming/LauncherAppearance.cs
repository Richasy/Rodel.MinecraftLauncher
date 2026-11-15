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

/// <summary>An immutable palette and mode captured by a tracked owner; view reconciliation compares these retained values.</summary>
/// <param name = "Palette">The immutable semantic colors observed by the tracked owner.</param>
/// <param name = "Mode">The same owner's resolved light, dark or high-contrast mode.</param>
internal readonly record struct LauncherAppearance(ThemeColors Palette, ThemeMode Mode)
{
    /// <summary>Observes the active reactive owner so its next evaluation supplies a new immutable view input.</summary>
    internal static LauncherAppearance Capture()
    {
        var theme = Sprout.Theming.Theme.Resolve();
        return new(theme.Colors, theme.Mode);
    }

    internal static readonly Color LightInsetShadow = Color.FromHex("#73A78042");
    internal static readonly Color DarkInsetShadow = Color.FromHex("#D2040F13");
    internal static readonly Color DarkSelectionInsetShadow = Color.FromHex("#E6233134");
    private static readonly Color LightPrimary = Color.FromHex("#5B8730");
    private static readonly Color DarkPrimary = Color.FromHex("#76AC47");
    internal bool HighContrast => Palette.HighContrast is not null;
    internal bool Light => Mode == ThemeMode.Light;
    internal SystemHighContrastColors HC => Palette.HighContrast!.Value;
    internal Color Ink => Palette.TextFillColorPrimary;
    internal Color ProgressAccent => HighContrast ? Ink : Light ? LightPrimary : DarkPrimary;
    internal Color SuccessBackground => Palette.SystemFillColorSuccessBackground;
    internal Color SuccessForeground => Palette.SystemFillColorSuccess;
    internal Color Error => HighContrast ? HC.SystemColorWindowTextColor : Light ? Color.FromHex("#A13A27") : Color.FromHex("#EF9F85");
    internal Color Warning => HighContrast ? HC.SystemColorWindowTextColor : Light ? Color.FromHex("#8A5A16") : Color.FromHex("#F0C47A");
    internal Color Muted => HighContrast ? HC.SystemColorWindowTextColor : Palette.TextFillColorSecondary;
    internal Color Quiet => HighContrast ? HC.SystemColorWindowTextColor : Palette.TextFillColorTertiary;
    /// <summary>Gets low-emphasis empty-list placeholder ink, retaining full system contrast when required.</summary>
    internal Color EmptyPlaceholder => HighContrast ? Ink : Quiet.MultiplyAlpha(.65f);
    internal Color Edge => HighContrast ? HC.SystemColorWindowTextColor : Light ? Color.FromHex("#5F9B722F") : Color.FromHex("#A06E827F");
    internal Color Focus => HighContrast ? HC.SystemColorWindowTextColor : Light ? Color.FromHex("#8B581D") : Color.FromHex("#F4DE9B");
    internal Color Background => Palette.SolidBackgroundFillColorBase;
    internal Color ControlBackground => HighContrast ? HC.SystemColorButtonFaceColor : Light ? Color.FromHex("#D7FFFCEF") : Color.FromHex("#8C182225");
    /// <summary>Separates the player's chat messages with a flat reading surface, without selection or button depth.</summary>
    internal Color AiUserMessageBackground => HighContrast ? Background : Light ? Color.FromHex("#D7EBE5D2") : Color.FromHex("#A53E4E51");
    internal Color SelectionBackground => HighContrast ? HC.SystemColorHighlightColor : Light ? Color.FromHex("#E7C991") : Color.FromHex("#3C4A4D");
    internal Color SelectionForeground => HighContrast ? HC.SystemColorHighlightTextColor : Light ? Color.FromHex("#463116") : Ink;
    internal Color PrimaryBackground => HighContrast ? HC.SystemColorHighlightColor : Light ? LightPrimary : DarkPrimary;
    internal Color ShadowColor => HighContrast ? Colors.Transparent : Light ? Color.FromHex("#5A694C1F") : Color.FromHex("#96040C0F");
    internal Color RaisedLight => Light ? Color.FromHex("#E6FFFFF9") : Color.FromHex("#6EE2E6CF");
    internal Color RaisedDark => Light ? Color.FromHex("#5A8C6A34") : Color.FromHex("#96020C0F");
    internal Color InsetDark => Light ? LightInsetShadow : DarkInsetShadow;
    internal Color InsetLight => Light ? Color.FromHex("#E6FFF5D8") : Color.FromHex("#37BDCCB4");
    internal Color SelectionInsetDark => Light ? LightInsetShadow : DarkSelectionInsetShadow;
    internal Color SelectionInsetLight => Light ? InsetLight : Color.FromHex("#55859A97");
    internal Color PageShade => HighContrast ? Background : Light ? Color.FromHex("#B0FFFAEC") : Color.FromHex("#B909161B");
    internal Color SceneInk => HighContrast ? Ink : Light ? Color.FromHex("#27231C") : Color.FromHex("#F6F2E8");
    /// <summary>Gets secondary wallpaper text ink without competing with the selected target heading.</summary>
    internal Color SceneMuted => HighContrast ? Ink : SceneInk.MultiplyAlpha(.76f);
    /// <summary>Gets subdued wallpaper context and configuration ink, retaining system ink in high contrast.</summary>
    internal Color SceneQuiet => HighContrast ? Ink : SceneInk.MultiplyAlpha(.56f);
    /// <summary>Gets the theme's text-on-accent ink, preserving highlight text in high contrast.</summary>
    internal Color PrimaryForeground => HighContrast ? HC.SystemColorHighlightTextColor : Palette.TextOnAccentFillColorPrimary;

    internal static void Configure(Theme theme) => theme.SetOverride(c => c.HighContrast is not null ? c : c with
    {
        TextFillColorPrimary = !c.IsLight ? Color.FromHex("#F4F2E9") : Color.FromHex("#382E20"),
        TextFillColorSecondary = !c.IsLight ? Color.FromHex("#C3C8C8") : Color.FromHex("#6B5C48"),
        TextFillColorTertiary = !c.IsLight ? Color.FromHex("#9AA8AA") : Color.FromHex("#75644D"),
        SolidBackgroundFillColorBase = !c.IsLight ? Color.FromHex("#10181C") : Color.FromHex("#EDE8DB"),
        SystemFillColorSuccess = c.IsLight ? LightPrimary : DarkPrimary,
    });
    internal LauncherTextStyles Styles => new(this);
    internal LauncherButtonStyles Buttons => new(this);
    internal LauncherInteractionPalette ButtonPalette => new(this);
    internal LauncherMenuStyles Menus => new(this);
    internal LauncherSurfaceMaterials Materials => new(this);
    internal LauncherBevelGeometry Bevel => new(this);
}