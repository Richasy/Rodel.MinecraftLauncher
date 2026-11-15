// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Pure menu styles derived from a retained launcher appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct LauncherMenuStyles(LauncherAppearance Theme)
{
    internal static FlyoutStyle Flyout => default(FlyoutStyle).CornerRadius(0f);
    internal static ToolTipStyle ToolTip => default(ToolTipStyle).CornerRadius(0f).Padding(10f, 6f, 10f, 6f).MaxWidth(400f).FontSize(12f);
    internal FlyoutStyle ErrorPopup => Theme.HighContrast ? default(FlyoutStyle).CornerRadius(0f).Padding(0f).MaxWidth(600f).NoShadow() : default(FlyoutStyle).CornerRadius(0f).Padding(0f).MaxWidth(600f).Shadow(Theme.ShadowColor, 6f, new(0f, 4f));
    internal ContentDialogStyle Confirmation => default(ContentDialogStyle).CornerRadius(0f).MaxWidth(440f).FontFamily(LauncherStyles.UiFontFamily).Background(Theme.Materials.Acrylic).Border(Theme.Edge).TitleForeground(Theme.Ink).ContentOverlay(Theme.ControlBackground).Divider(Theme.Edge);

    internal static readonly Action<ISceneRenderer, Sprout.Controls.ComboBoxSurface> ChoiceSurface = Controls.Buttons.LauncherChoiceSurface.Paint;
    internal static readonly Action<ISceneRenderer, Sprout.Controls.ComboBoxSurface> CriticalChoiceSurface = Controls.Buttons.LauncherChoiceSurface.PaintCritical;
    internal TextBoxStyle ChoiceEditor => LauncherStyles.Input.ContentFontSize(13f).ContentPadding(new Thickness(14f, 10f, 0f, 10f)).ColorPalette(TextBoxPalette.Default with
    {
        Background = Colors.Transparent,
        BackgroundFocused = Colors.Transparent,
        BackgroundPointerOver = Colors.Transparent,
        BackgroundDisabled = Colors.Transparent,
        BorderDisabled = Colors.Transparent,
        Foreground = Theme.Ink,
        PlaceholderForeground = Theme.Muted,
        RestingElevation = ElevationProfile.TextField(Colors.Transparent, Colors.Transparent),
        FocusedElevation = ElevationProfile.TextField(Colors.Transparent, Colors.Transparent),
    });
    internal MenuFlyoutStyle CommandMenu => default(MenuFlyoutStyle).CornerRadius(0f).Background(Theme.Materials.Acrylic).Border(Theme.Edge).BorderThickness(1f).Shadow(Theme.ShadowColor, 6f, new(0f, 4f)).FontFamily(LauncherStyles.UiFontFamily).FontSize(13f).Palette(Sprout.Controls.MenuPalette.FromTheme(Theme.Palette) with
    {
        PressedBackground = Theme.ButtonPalette.Create(false, false).Pressed.Background,
        PressedContentOffset = Theme.HighContrast ? default : Controls.Buttons.BevelGeometry.FaceCenter(new Thickness(1f, 3f, 1f, 1f)),
        PressedInsetShadow = Theme.HighContrast ? Colors.Transparent : Theme.InsetDark,
        PressedInsetHighlight = Theme.HighContrast ? Colors.Transparent : Theme.InsetLight,
        ItemCornerRadius = 0f,
    }).ItemMinHeight(40f);
    /// <summary>Uses the shared neutral colors for both halves without overriding split-button geometry.</summary>
    internal SplitButtonStyle SplitCommand => SplitCommandFor(false);
    /// <summary>Uses the same split-button geometry with accent colors on both primary and dropdown halves.</summary>
    internal SplitButtonStyle AccentSplitCommand => SplitCommandFor(true);

    private SplitButtonStyle SplitCommandFor(bool primary)
    {
        var palette = Theme.ButtonPalette.Create(primary, false);
        return default(SplitButtonStyle).Palette(new Sprout.Controls.SplitButtonPalette(
                palette.Normal,
                palette.PointerOver,
                palette.Pressed,
                palette.Disabled,
                primary ? Theme.PrimaryForeground.MultiplyAlpha(.3f) : Theme.Edge)
        {
            ContentOffsets = Theme.Bevel.ContentOffsets(primary),
            Elevation = palette.Normal.Elevation is { } elevation ? ElevationProfile.Edges(palette.Normal.Border, elevation.TopEdge, elevation.BottomEdge) : null,
        });
    }

    internal static ButtonStyle ChoiceFace => default(ButtonStyle).CornerRadius(CornerRadius.Zero).Padding(new Thickness(14f, 10f, 0f, 10f)).BorderThickness(new Thickness(1f, 1f, 1f, 2f)).LabelStyle(LauncherStyles.Text.FontSize(13f));
    internal FlyoutStyle ChoiceMenu => default(FlyoutStyle).CornerRadius(0f).Background(Theme.Materials.Acrylic).Border(Theme.Edge).BorderThickness(1f).Padding(0f).Shadow(Theme.ShadowColor, 6f, new(0f, 4f));
    internal Sprout.Controls.ComboBoxPalette Choice => Sprout.Controls.ComboBoxPalette.FromTheme(Theme.Palette) with
    {
        Faceplate = ChoicePalette,
        ContentOffsets = Theme.Bevel.ContentOffsets(),
        FaceplateInsetShadow = Theme.HighContrast ? Colors.Transparent : Theme.InsetDark,
        FaceplateInsetHighlight = Theme.HighContrast ? Colors.Transparent : Theme.InsetLight,
        ChevronForeground = Theme.Ink,
        PlaceholderForeground = Theme.Muted,
        ItemNormal = new(Theme.ControlBackground.MultiplyAlpha(0f), Theme.Ink),
        ItemPointerOver = Theme.HighContrast
            ? new(Theme.HC.SystemColorHighlightColor, Theme.HC.SystemColorHighlightTextColor)
            : new(Theme.ControlBackground, Theme.Ink),
        ItemPressed = Theme.HighContrast
            ? new(Theme.HC.SystemColorWindowTextColor, Theme.HC.SystemColorWindowColor)
            : RecessedChoiceItem,
        ItemSelected = RecessedChoiceItem,
        ItemSelectedPointerOver = Theme.HighContrast
            ? new(Theme.HC.SystemColorWindowTextColor, Theme.HC.SystemColorWindowColor)
            : RecessedChoiceItem,
        ItemPillFill = Colors.Transparent,
    };
    private Sprout.Controls.ComboBoxItemVisual RecessedChoiceItem => new(Theme.SelectionBackground, Theme.SelectionForeground)
    {
        ContentOffset = Theme.HighContrast ? default : Controls.Buttons.BevelGeometry.FaceCenter(new Thickness(1f, 3f, 1f, 1f)),
        InsetShadow = Theme.HighContrast ? Colors.Transparent : Theme.SelectionInsetDark,
        InsetHighlight = Theme.HighContrast ? Colors.Transparent : Theme.SelectionInsetLight,
    };

    private ButtonPalette ChoicePalette
    {
        get
        {
            var palette = Theme.ButtonPalette.Create(false, false);
            return Theme.HighContrast ? palette : palette with
            {
                Normal = palette.Normal with
                {
                    Elevation = null
                },
                PointerOver = palette.PointerOver with
                {
                    Elevation = null
                },
                Pressed = palette.Pressed with
                {
                    Background = Theme.SelectionBackground,
                    Foreground = Theme.SelectionForeground,
                    Elevation = null,
                },
            };
        }
    }
}