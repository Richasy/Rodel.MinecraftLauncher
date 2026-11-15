// Copyright (c) Richasy. All rights reserved.

using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Pure interaction colors derived from a retained launcher appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct LauncherInteractionPalette(LauncherAppearance Theme)
{
    /// <summary>Keeps native list interaction, square geometry and contrast pairs shared by instance and content rows.</summary>
    internal ItemsViewItemChrome ListItems => new(
        Theme.HighContrast
            ? Create(false, false)
            : Create(false, false) with
            {
                Normal = new(Colors.Transparent, Theme.Ink, Colors.Transparent),
                PointerOver = new(ThemeColorToken.ItemContainerBackgroundPointerOver, Theme.Ink, Colors.Transparent),
                Pressed = new(ThemeColorToken.ItemContainerBackgroundPressed, Theme.Ink, Colors.Transparent),
            },
        Create(false, true),
        Theme.HighContrast ? Theme.SelectionForeground : Theme.PrimaryBackground,
        CornerRadius.Zero,
        0f);

    /// <summary>Keeps borderless controls on one hover hue while fading to transparency, without replacing system contrast pairs.</summary>
    internal ButtonPalette Quiet
    {
        get
        {
            var palette = Create(false, false);
            if (Theme.HighContrast)
                return palette;
            var transparent = palette.PointerOver.Background.Resolve(Theme.Palette).MultiplyAlpha(0f);
            return palette with
            {
                Normal = palette.Normal with { Background = transparent, Border = Colors.Transparent, Elevation = null },
                PointerOver = palette.PointerOver with { Border = Colors.Transparent, Elevation = null },
                Pressed = palette.Pressed with { Border = Colors.Transparent, Elevation = null },
                Disabled = palette.Disabled with { Background = transparent, Border = Colors.Transparent, Elevation = null },
            };
        }
    }

    internal ButtonPalette Create(bool primary, bool selected)
    {
        if (Theme.Palette.HighContrast is { } hc)
        {
            var highlight = new ButtonVisualState(hc.SystemColorHighlightColor, hc.SystemColorHighlightTextColor, hc.SystemColorButtonTextColor);
            var normal = new ButtonVisualState(hc.SystemColorButtonFaceColor, hc.SystemColorButtonTextColor, hc.SystemColorButtonTextColor);
            var inverted = new ButtonVisualState(hc.SystemColorWindowTextColor, hc.SystemColorWindowColor, hc.SystemColorWindowTextColor);
            return new(
                primary || selected ? highlight : normal,
                primary || selected ? inverted with { Border = hc.SystemColorHighlightColor } : highlight,
                primary || selected ? normal with { Border = hc.SystemColorHighlightColor } : inverted,
                new(hc.SystemColorButtonFaceColor, hc.SystemColorGrayTextColor, hc.SystemColorGrayTextColor));
        }

        var foreground = primary ? Theme.Palette.TextOnAccentFillColorPrimary : selected ? Theme.SelectionForeground : Theme.Ink;
        var normalFace = primary ? Theme.PrimaryBackground : selected ? Theme.SelectionBackground : Theme.ControlBackground;
        var hover = primary ? Theme.Light ? Color.FromHex("#699739") : Color.FromHex("#86BC55") : selected ? Theme.SelectionBackground : Theme.Light ? Color.FromHex("#FFF4D9") : Color.FromHex("#344346");
        var pressed = primary ? Theme.Light ? Color.FromHex("#29441C") : Color.FromHex("#304D25") : Theme.Light ? Color.FromHex("#C7A366") : selected ? Color.FromHex("#283639") : Color.FromHex("#0A191D");
        var pressedInk = primary ? Theme.Palette.TextOnAccentFillColorSecondary : Theme.Light ? Color.FromHex("#34240F") : Color.FromHex("#9BAEA7");
        var light = primary ? Color.FromHex("#BCDB8E") : Theme.RaisedLight;
        var dark = Theme.Light ? Color.FromHex("#96A78042") : Color.FromHex("#061115");
        var faceEdge = selected ? Theme.Light ? Color.FromHex("#B58E4A") : Theme.Edge : primary ? Color.FromHex("#476A31") : Theme.Edge;
        return new(new(normalFace, foreground, faceEdge, selected ? new(dark, light) : new(light, dark)), new(hover, foreground, Theme.Edge, new(light, dark)), new(pressed, pressedInk, dark, new(dark, light)), new(Theme.Light ? Color.FromHex("#F4EDDB") : Color.FromHex("#D224302B"), Theme.Light ? Color.FromHex("#938671") : Theme.Quiet, Theme.Light ? Color.FromHex("#B9AD94") : Color.FromHex("#3B4B42")));
    }
}