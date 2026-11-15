// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Skin editor styles computed from the owning view's immutable appearance.</summary>
/// <param name="Theme">Immutable appearance captured by the owning reactive control.</param>
internal readonly record struct SkinEditorAppearance(LauncherAppearance Theme)
{
    internal ButtonPalette QuietPalette => Theme.ButtonPalette.Quiet;

    internal ToggleButtonPalette ToolPalette
    {
        get
        {
            var normal = Theme.ButtonPalette.Create(false, false);
            if (!Theme.HighContrast)
            {
                normal = normal with
                {
                    Normal = QuietPalette.Normal,
                    Disabled = QuietPalette.Disabled,
                };
            }

            var selected = Theme.ButtonPalette.Create(false, true);
            selected = selected with
            {
                PointerOver = selected.PointerOver with
                {
                    Elevation = selected.Normal.Elevation
                }
            };
            return new(States(normal), States(selected), States(normal));
        }
    }

    internal ToggleButtonPalette PropertyPalette => new(States(Theme.ButtonPalette.Create(false, false)), States(Theme.ButtonPalette.Create(false, true)), States(QuietPalette));
    internal ToggleButtonPalette TextTogglePalette => new(States(QuietPalette with { Normal = QuietPalette.Normal with { Foreground = Theme.Muted } }), States(QuietPalette), States(QuietPalette));
    internal static ToggleButtonStyle ToolStyle => default(ToggleButtonStyle).CornerRadius(0f).Padding(0f).LabelStyle(LauncherStyles.Text.FontSize(11f));
    internal static ToggleButtonStyle PropertyStyle => ToolStyle.Padding(8f, 4f);
    internal static ToggleButtonStyle TextToggleStyle => PropertyStyle.BorderThickness(0f);
    internal ButtonStyle QuietCommand => Theme.Buttons.Button.Palette(QuietPalette).BorderThickness(0f).Padding(4f).LabelStyle(LauncherStyles.Text.FontSize(11f));
    internal ButtonStyle QuietIconCommand => QuietCommand.Padding(0f).ContentAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);
    internal ButtonStyle CompactCommand => Theme.Buttons.Button.Padding(8f, 4f).LabelStyle(LauncherStyles.Text.FontSize(11f));
    internal DropDownButtonStyle TitleTrigger => default(DropDownButtonStyle).Button(QuietCommand.Palette(QuietPalette with { Pressed = Theme.ButtonPalette.Create(false, true).Pressed }).BorderThickness(1f)).Padding(8f, 4f).CornerRadius(0f);
    internal DropDownButtonStyle ColorTrigger => TitleTrigger.ChevronVisible(false).Padding(3f).BorderThickness(1f);

    internal ToggleButtonPalette SwatchPalette(string hex)
    {
        var color = new Color(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), hex.Length == 9 ? Convert.ToByte(hex.Substring(7, 2), 16) : (byte)255);
        var normal = new ToggleButtonVisualState(color, Theme.Ink, Theme.Edge);
        var selected = normal with
        {
            Border = Theme.Ink
        };
        return new(new(normal, selected, selected, normal), new(selected, selected, selected, selected), new(normal, normal, normal, normal));
    }

    private static ToggleButtonStatePalette States(ButtonPalette palette) => new(ConvertState(palette.Normal), ConvertState(palette.PointerOver), ConvertState(palette.Pressed), ConvertState(palette.Disabled));
    private static ToggleButtonVisualState ConvertState(ButtonVisualState state) => new(state.Background, state.Foreground, state.Border, state.Elevation is { } edge ? ElevationProfile.Edges(state.Border, edge.TopEdge, edge.BottomEdge) : null);
}