// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Menu, popup and dropdown appearances composed from the shared colors, materials and button palette.</summary>
internal static class LauncherMenus
{
    /// <summary>Re-evaluates themed flyout chrome against the actual owner before opening and during live theme changes.</summary>
    /// <param name="flyout">The retained popup whose content and input identity remain unchanged.</param>
    /// <param name="style">The feature's pure style composition, including its original geometry.</param>
    /// <returns>The same popup with a live owner-theme chrome resolver.</returns>
    internal static Sprout.Popups.Flyout LauncherStyle(this Sprout.Popups.Flyout flyout, Func<LauncherAppearance, FlyoutStyle> style)
    {
        flyout.ChromeResolver = theme => style(new LauncherAppearance(theme.Colors, theme.Mode))
            .ApplyTo(Sprout.Popups.PopupChrome.Flyout(theme));
        return flyout;
    }
    /// <summary>Resolves retained popup chrome from its actual owning window, including live changes while open.</summary>
    internal static Sprout.Popups.PopupChrome ResolvePopupChrome(Theme theme, float padding)
    {
        var appearance = new LauncherAppearance(theme.Colors, theme.Mode);
        return appearance.Menus.ErrorPopup.Background(appearance.Materials.Acrylic).Border(appearance.Edge).Padding(padding).ApplyTo(Sprout.Popups.PopupChrome.Flyout(theme));
    }

    internal static FlyoutStyle Flyout => LauncherMenuStyles.Flyout;
    internal static ToolTipStyle ToolTip => LauncherMenuStyles.ToolTip;
    internal static ContentDialogStyle Confirmation => LauncherAppearance.Capture().Menus.Confirmation;

    internal static readonly Action<ISceneRenderer, Sprout.Controls.ComboBoxSurface> ChoiceSurface = LauncherMenuStyles.ChoiceSurface;
    internal static readonly Action<ISceneRenderer, Sprout.Controls.ComboBoxSurface> CriticalChoiceSurface = LauncherMenuStyles.CriticalChoiceSurface;
    internal static TextBoxStyle ChoiceEditor => LauncherAppearance.Capture().Menus.ChoiceEditor;
    internal static MenuFlyoutStyle CommandMenu => LauncherAppearance.Capture().Menus.CommandMenu;
    /// <summary>Uses the shared neutral colors for both halves without overriding split-button geometry.</summary>
    internal static SplitButtonStyle SplitCommand => LauncherAppearance.Capture().Menus.SplitCommand;
    /// <summary>Uses the same split-button geometry with accent colors on both primary and dropdown halves.</summary>
    internal static SplitButtonStyle AccentSplitCommand => LauncherAppearance.Capture().Menus.AccentSplitCommand;
    internal static ButtonStyle ChoiceFace => LauncherMenuStyles.ChoiceFace;
    internal static FlyoutStyle ChoiceMenu => LauncherAppearance.Capture().Menus.ChoiceMenu;
    internal static Sprout.Controls.ComboBoxPalette Choice => LauncherAppearance.Capture().Menus.Choice;
}