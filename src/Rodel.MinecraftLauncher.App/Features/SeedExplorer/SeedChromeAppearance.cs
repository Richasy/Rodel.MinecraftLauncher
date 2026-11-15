// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Seed explorer styles computed from the owning view's immutable appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct SeedChromeAppearance(LauncherAppearance Theme)
{
    internal const float FieldHeight = 42f;
    internal ButtonStyle Quiet => Theme.Buttons.Button.Palette(Theme.ButtonPalette.Quiet)
        .BorderThickness(0f).Padding(8f, 4f).LabelStyle(LauncherStyles.Text.FontSize(12f));

    internal ButtonStyle ListRow => Quiet.ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center);
    internal ButtonStyle Icon => Quiet.Padding(0f).ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);

    internal ButtonStyle MapTool
    {
        get
        {
            if (Theme.HighContrast)
                return Icon;
            var palette = Theme.ButtonPalette.Create(false, false);
            return Icon.Palette(palette with
            {
                Normal = palette.Normal with
                {
                    Background = Theme.Ink.MultiplyAlpha(0f),
                    Border = Colors.Transparent,
                    Elevation = null
                },
                PointerOver = palette.PointerOver with
                {
                    Background = Theme.Ink.MultiplyAlpha(.12f),
                    Border = Colors.Transparent,
                    Elevation = null
                },
                Pressed = palette.Pressed with
                {
                    Background = Theme.Ink.MultiplyAlpha(.20f),
                    Foreground = Theme.Ink,
                    Border = Colors.Transparent,
                    Elevation = null
                },
                Disabled = palette.Disabled with
                {
                    Background = Theme.Ink.MultiplyAlpha(0f),
                    Border = Colors.Transparent,
                    Elevation = null
                },
            });
        }
    }

    internal DropDownButtonStyle DropDown => default(DropDownButtonStyle).Button(Quiet).Padding(8f, 4f).CornerRadius(0f);
    internal DropDownButtonStyle IconDropDown => DropDown.Button(Icon).ChevronVisible(false).Padding(0f);

    internal ButtonStyle Inventory(bool selected)
    {
        var palette = Theme.ButtonPalette.Create(false, false);
        var normal = palette.Normal with
        {
            Background = selected ? Theme.SelectionBackground : Theme.ControlBackground,
            Border = selected ? Theme.Ink : Theme.Edge,
            Elevation = null
        };
        return Quiet.Palette(palette with
        {
            Normal = normal,
            Disabled = normal with { Foreground = Theme.Muted },
            PointerOver = palette.PointerOver with { Elevation = null },
            Pressed = palette.Pressed with { Elevation = null }
        }).Padding(0f).BorderThickness(1f).CornerRadius(0f);
    }

    internal ButtonStyle ScenePin(bool selected)
    {
        var palette = Theme.ButtonPalette.Create(false, false);
        if (Theme.HighContrast)
            return Icon.Palette(Theme.ButtonPalette.Create(false, selected)).Padding(0f).BorderThickness(1f).CornerRadius(0f);
        var dark = Color.FromHex("#172322");
        return Icon.Palette(palette with
        {
            Normal = palette.Normal with
            {
                Background = dark.MultiplyAlpha(selected ? .80f : .58f),
                Foreground = Color.FromHex("#ffffff"),
                Border = selected ? Theme.ProgressAccent : palette.PointerOver.Border.Resolve(Theme.Palette).MultiplyAlpha(0f),
                Elevation = null
            },
            PointerOver = palette.PointerOver with
            {
                Background = dark.MultiplyAlpha(.86f),
                Foreground = Color.FromHex("#ffffff"),
                Elevation = null
            },
            Pressed = palette.Pressed with
            {
                Background = dark.MultiplyAlpha(.94f),
                Foreground = Color.FromHex("#ffffff"),
                Elevation = null
            },
        }).Padding(0f).BorderThickness(1f).CornerRadius(0f);
    }

    internal ButtonStyle Pin(bool selected)
    {
        var palette = Theme.ButtonPalette.Create(false, false);
        if (Theme.HighContrast)
            return Theme.Buttons.Button.Palette(Theme.ButtonPalette.Create(false, selected)).Padding(2f).CornerRadius(0f);
        var dark = Color.FromHex("#172322");
        return Theme.Buttons.Button.Palette(palette with
        {
            Normal = palette.Normal with
            {
                Background = dark.MultiplyAlpha(selected ? .30f : .08f),
                Border = Theme.Ink.MultiplyAlpha(selected ? 1f : 0f),
                Elevation = null
            },
            PointerOver = palette.PointerOver with
            {
                Background = dark.MultiplyAlpha(.52f),
                Border = Theme.Ink,
                Elevation = null
            },
            Pressed = palette.Pressed with
            {
                Background = dark.MultiplyAlpha(.65f),
                Border = Theme.Ink,
                Elevation = null
            },
        }).Padding(2f).CornerRadius(0f);
    }

    internal ButtonStyle OriginPin(bool selected)
    {
        if (Theme.HighContrast)
            return Pin(selected).BorderThickness(1f).Padding(1f);
        var palette = Theme.ButtonPalette.Create(false, selected);
        var dark = Color.FromHex("#172322");
        return Pin(selected).Palette(palette with
        {
            Normal = palette.Normal with
            {
                Background = dark.MultiplyAlpha(.86f),
                Border = SeedAppearance.OriginColorFor(Theme),
                Elevation = null,
            },
            PointerOver = palette.PointerOver with
            {
                Background = dark.MultiplyAlpha(.96f),
                Border = SeedAppearance.OriginColorFor(Theme),
                Elevation = null
            },
            Pressed = palette.Pressed with
            {
                Background = dark,
                Border = SeedAppearance.OriginColorFor(Theme),
                Elevation = null
            },
        }).BorderThickness(1f).Padding(1f);
    }
}