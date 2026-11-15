// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Theming;

/// <summary>Pure command styles derived from a retained launcher appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct LauncherButtonStyles(LauncherAppearance Theme)
{
    internal static ButtonStyle Geometry => default(ButtonStyle).CornerRadius(CornerRadius.Zero).Padding(12f, 8f).BorderThickness(1f).LabelStyle(LauncherStyles.Text)
        .FocusVisual(new FocusVisualStyle(null, 2f, 1f));
    internal ButtonStyle Button => Geometry.Palette(Theme.ButtonPalette.Create(false, false)).FocusVisual(new FocusVisualStyle(null, 2f, 1f, Theme.Focus, Theme.Background));

    /// <summary>Retains the normal command geometry while marking an error-inspection action with error surfaces and ink.</summary>
    internal ButtonStyle ErrorButton
    {
        get
        {
            var palette = Theme.ButtonPalette.Create(false, false);
            return Theme.HighContrast ? Button : Button.Palette(palette with
            {
                Normal = new(Theme.Error.MultiplyAlpha(.12f), Theme.Error, Theme.Error.MultiplyAlpha(.65f)),
                PointerOver = new(Theme.Error.MultiplyAlpha(.2f), Theme.Error, Theme.Error),
                Pressed = new(Theme.Error.MultiplyAlpha(.28f), Theme.Error, Theme.Error),
            });
        }
    }

    /// <summary>Gets navigation colors and geometry; render with LauncherControls.Command and AsSelected to retain the shared inset surface.</summary>
    /// <param name = "selected">Whether the navigation command represents the current selection.</param>
    /// <returns>Appearance for the shared bevel command; not a standalone flat selected-button skin.</returns>
    internal ButtonStyle Navigation(bool selected)
    {
        var palette = Theme.ButtonPalette.Create(false, selected);
        if (!selected && !Theme.HighContrast)
        {
            var quiet = Theme.ButtonPalette.Quiet;
            palette = palette with
            {
                Normal = palette.Normal with
                {
                    Background = quiet.Normal.Background,
                    Foreground = Theme.Ink,
                    Border = palette.PointerOver.Border.Resolve(Theme.Palette).MultiplyAlpha(0f),
                },
                Disabled = quiet.Disabled,
            };
        }

        return Button.Palette(palette).Padding(0f).BorderThickness(selected ? 1f : 0f);
    }

    /// <summary>Gives flat AI actions distinct hover/press fills even when the surrounding surface shares the control background hue.</summary>
    internal ButtonStyle AiTextAction
    {
        get
        {
            var style = OutlineButton.BorderThickness(0f).Padding(8f, 6f);
            if (Theme.HighContrast)
                return style;
            return style.Palette(new ButtonPalette(new(Theme.SelectionBackground.MultiplyAlpha(0f), Theme.Ink, Colors.Transparent), new(Theme.SelectionBackground.MultiplyAlpha(.65f), Theme.Ink, Colors.Transparent), new(Theme.SelectionBackground, Theme.Ink, Colors.Transparent), new(Theme.SelectionBackground.MultiplyAlpha(0f), Theme.Quiet, Colors.Transparent)));
        }
    }

    internal ButtonStyle InstanceTitle
    {
        get
        {
            var normal = new ButtonVisualState(Theme.HighContrast ? Theme.HC.SystemColorButtonFaceColor : Colors.Transparent, Theme.HighContrast ? Theme.HC.SystemColorButtonTextColor : Theme.Muted, Colors.Transparent);
            return Button.Palette(new ButtonPalette(normal, normal, normal with { Foreground = Theme.Focus }, normal with { Foreground = Theme.Quiet })).Padding(0f).BorderThickness(0f).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center);
        }
    }

    internal ButtonStyle InstanceRow(bool selected)
    {
        var palette = Theme.ButtonPalette.Create(false, selected);
        if (!Theme.HighContrast && !Theme.Light)
        {
            palette = palette with
            {
                Normal = palette.Normal with
                {
                    Background = selected ? Theme.SelectionBackground : Color.FromHex("#80101C20"),
                    Border = selected ? Theme.Edge : Color.FromHex("#3374877D"),
                },
            };
        }

        return Button.Palette(palette).Padding(12f, 13f).BorderThickness(1f).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center);
    }

    internal ButtonStyle Accent => Button.Palette(Theme.ButtonPalette.Create(true, false)).ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);
    internal ButtonStyle CriticalIcon => Theme.HighContrast ? IconButton : IconButton.Palette(new(new(Theme.Error.MultiplyAlpha(0f), Theme.Error, Theme.Error.MultiplyAlpha(0f)), new(Theme.Error.MultiplyAlpha(.12f), Theme.Error, Theme.Error.MultiplyAlpha(.35f)), new(Theme.Error.MultiplyAlpha(.2f), Theme.Error, Theme.Error), new(Theme.Error.MultiplyAlpha(0f), Theme.Quiet, Theme.Error.MultiplyAlpha(0f))));
    internal ButtonStyle DialogPrimary => Accent.LabelStyle(LauncherStyles.Text.FontSize(12f));
    internal ButtonStyle DialogSecondary => OutlineButton.LabelStyle(LauncherStyles.Text.FontSize(12f));
    internal ButtonStyle DialogHeaderButton => OutlineButton.Padding(8f, 4f).LabelStyle(LauncherStyles.Text.FontSize(12f)).ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);
    /// <summary>Centers file and folder picker icons without text padding or a bevel decorator.</summary>
    internal ButtonStyle BrowseButton => OutlineButton.Padding(0f).ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);

    /// <summary>Provides a transparent, borderless and centered surface for secondary icon actions, retaining keyboard focus cues.</summary>
    internal ButtonStyle FlatIconButton
    {
        get
        {
            var palette = Theme.ButtonPalette.Quiet;
            if (!Theme.HighContrast)
            {
                palette = palette with
                {
                    Normal = palette.Normal with { Foreground = Theme.Muted },
                    Disabled = palette.Disabled with { Foreground = Theme.Quiet },
                };
            }

            return Button.Padding(0f).BorderThickness(0f).Palette(palette).ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>Uses the shared flat icon surface for popup header close actions.</summary>
    internal ButtonStyle PopupCloseButton => FlatIconButton;

    /// <summary>Applies the shared quiet transition colors to direct text links, retaining native hyperlink semantics.</summary>
    internal HyperlinkButtonStyle Hyperlink(bool secondary = false)
    {
        var quiet = Theme.ButtonPalette.Quiet;
        var palette = Theme.HighContrast
            ? HyperlinkButtonPalette.FromTheme(Theme.Palette)
            : new HyperlinkButtonPalette(
                new(quiet.Normal.Background, secondary ? Theme.Muted : Theme.Ink),
                new(quiet.PointerOver.Background, quiet.PointerOver.Foreground),
                new(quiet.Pressed.Background, quiet.Pressed.Foreground),
                new(quiet.Disabled.Background, quiet.Disabled.Foreground));
        return default(HyperlinkButtonStyle)
            .Palette(palette)
            .CornerRadius(CornerRadius.Zero)
            .BorderThickness(0f)
            .Padding(4f, 5f)
            .LabelStyle(LauncherStyles.Text.FontSize(secondary ? 12f : 14f))
            .ContentAlignment(HorizontalAlignment.Left, VerticalAlignment.Center)
            .FocusVisual(new FocusVisualStyle(null, 2f, 1f, Theme.Focus, Theme.Background));
    }

    internal ButtonStyle Primary => Button.Palette(Theme.ButtonPalette.Create(true, false)).Padding(24f, 0f).BorderThickness(2f).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center);

    internal ButtonStyle Disclosure
    {
        get
        {
            var palette = Theme.HighContrast ? Theme.ButtonPalette.Create(false, false) : new ButtonPalette(new(Theme.ControlBackground.MultiplyAlpha(0f), Theme.Ink, Theme.Edge), new(Theme.ControlBackground.MultiplyAlpha(.55f), Theme.Ink, Theme.Edge), new(Theme.ControlBackground.MultiplyAlpha(.85f), Theme.Ink, Theme.Focus), new(Theme.ControlBackground.MultiplyAlpha(0f), Theme.Quiet, Theme.Edge));
            return Button.Palette(palette).Padding(12f, 10f).ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center);
        }
    }

    internal ButtonStyle OutlineButton
    {
        get
        {
            var palette = Theme.ButtonPalette.Create(false, false);
            if (!Theme.HighContrast)
            {
                palette = palette with
                {
                    // Preserve the hover hue at zero alpha: straight-color transitions from
                    // transparent black would briefly darken a light surface.
                    Normal = new(Theme.ControlBackground.MultiplyAlpha(0f), Theme.Ink, Theme.Edge),
                    PointerOver = new(Theme.ControlBackground, Theme.Ink, Theme.Focus),
                    Pressed = palette.Pressed with
                    {
                        Border = Theme.Focus,
                        Elevation = null
                    },
                    Disabled = new(Theme.ControlBackground.MultiplyAlpha(0f), Theme.Quiet, Theme.Edge),
                };
            }

            return Button.Palette(palette);
        }
    }

    internal ButtonStyle IconButton
    {
        get
        {
            var palette = Theme.ButtonPalette.Create(false, false);
            if (!Theme.HighContrast)
            {
                var quiet = Theme.ButtonPalette.Quiet;
                palette = palette with
                {
                    Normal = quiet.Normal with
                    {
                        Foreground = Theme.Muted,
                    },
                    Disabled = quiet.Disabled,
                };
            }

            return Button.Padding(5f).BorderThickness(Theme.HighContrast ? 1f : 0f).LabelStyle(LauncherStyles.Text.FontSize(11f)).Palette(palette);
        }
    }
}