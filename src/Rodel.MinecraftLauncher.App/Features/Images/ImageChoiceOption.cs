// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>One selectable picture with native toggle semantics and a stable automation identity.</summary>
[View]
internal readonly partial record struct ImageChoiceOption(
    LauncherAppearance Theme,
    ImagePreset Preset,
    bool Selected,
    Action Choose,
    float Edge,
    bool Enabled)
{
    internal Action<bool?> Toggle
    {
        get
        {
            var choose = Choose;
            return _ => choose();
        }
    }

    internal static ToggleButtonStatePalette States(ButtonPalette palette) => new(Convert(palette.Normal), Convert(palette.PointerOver), Convert(palette.Pressed), Convert(palette.Disabled));

    private static ToggleButtonVisualState Convert(ButtonVisualState state) => new(state.Background, state.Foreground, state.Border, state.Elevation is { } edge ? ElevationProfile.Edges(state.Border, edge.TopEdge, edge.BottomEdge) : null);

    /// <summary>Displays the preset without changing the saved image until the enclosing popup commits.</summary>
    public Ui Body => LauncherControls.ToolToggle(Theme,
        Overlay([
            Image(Preset.Source)
                .Stretch(ImageStretch.Uniform)
                .Width(Edge - 8f).Height(Edge - 8f),
            Border(Text("✓")
                    .FontSize(11f).Foreground(Theme.Ink))
                .Background(Theme.ControlBackground)
                .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom)
                .Visible(Selected),
        ]),
        Selected,
        Toggle,
        Enabled,
        new(States(Theme.ButtonPalette.Create(false, false)), States(Theme.ButtonPalette.Create(false, true)), States(Theme.ButtonPalette.Create(false, false))),
        default(ToggleButtonStyle).CornerRadius(0f)
            .Padding(4f))
        .Width(Edge).Height(Edge)
        .ToolTip(Preset.Name)
        .Automation(new()
        {
            Name = Preset.Name,
            AutomationId = "image.preset." + Preset.Id
        });
}