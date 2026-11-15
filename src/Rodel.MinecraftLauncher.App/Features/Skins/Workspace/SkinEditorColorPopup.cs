// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

[View]
internal readonly partial record struct SkinEditorColorChip(Color Value)
{
    /// <summary>Displays the foreground swatch without adding command padding.</summary>
    public Ui Body => Rectangle(Value)
        .Width(24f).Height(24f);
}

/// <summary>Uses Sprout's HSV/alpha picker to edit only the window's transient foreground color.</summary>
internal sealed partial class SkinEditorColorPopup(SkinEditorState editor, Action close) : Control
{
    private Ui Body => Column([
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Text(Resources.SkinColor)
                    .FontSize(14f).FontWeight(600)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Button(
                    Row([
                        LauncherIcons.Line("close", 14f)
                    ]),
                    close)
                    .Style(SkinEditorChrome.QuietIconCommand)
                    .Width(24f).Height(24f)
                    .ToolTip(Resources.SkinCloseColor)
                    .Automation(new()
                    {
                        Name = Resources.SkinCloseColor,
                        AutomationId = "skin.color.close"
                    })
                    .Cell(1, 0),
            ]),
        new SkinColorPickerFields(
            LauncherAppearance.Capture(),
            editor.ForegroundColor,
            editor.Foreground,
            editor.ValidColor,
            color => editor.SetColor(color),
            value => editor.Foreground = value,
            Resources.SkinColorInvalid,
            "skin.color.hsv",
            "skin.color.popup.hex",
            CanEdit: !editor.TransparentInk),
        new LauncherCheckBox(
            LauncherAppearance.Capture(),
            Resources.SkinTransparentColor,
            editor.TransparentInk,
            editor.ToggleTransparentInk,
            true,
            "skin.color.popup.transparent",
            FontSize: 12f),
    ])
        .Width(240f)
        .Spacing(12f);
}

/// <summary>Shares the native HSV/alpha picker, geometry and optional exact input across foreground and palette editing.</summary>
/// <param name = "Value">Last valid RGBA color.</param>
/// <param name = "Hex">Exact input, retaining incomplete or invalid text.</param>
/// <param name = "Valid">Whether the exact input can be committed.</param>
/// <param name = "Choose">Commits a real spectrum, hue or alpha change.</param>
/// <param name = "EditHex">Validates and commits an exact color when complete.</param>
/// <param name = "InvalidMessage">Localized format requirement for this editing surface.</param>
/// <param name = "PickerId">Stable identity for the native picker and its child ranges.</param>
/// <param name = "HexId">Stable identity for optional exact input.</param>
/// <param name = "CanEdit">Whether the native picker and exact input accept changes.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct SkinColorPickerFields(
    LauncherAppearance Theme,
    Color Value,
    string Hex,
    bool Valid,
    Action<Color> Choose,
    Action<string> EditHex,
    string InvalidMessage,
    string PickerId,
    string HexId,
    bool CanEdit = true)
{
    /// <summary>Preserves native spectrum, hue, alpha and keyboard behavior without duplicating a color editor.</summary>
    public Ui Body => Column([
        ColorPicker(Value)
            .Components(ColorSpectrumComponents.ValueSaturation)
            .SpectrumSize(240f)
            .SurfaceCornerRadius(0f)
            .SelectorCornerRadius(0f)
            .IsAlphaEnabled(true)
            .IsAlphaSliderVisible(true)
            .IsHexInputVisible(false)
            .IsColorChannelTextInputVisible(false)
            .IsColorPreviewVisible(false)
            .Enabled(CanEdit)
            .OnColorChanged(Choose)
            .Automation(new()
            {
                Name = Resources.SkinColorLabel,
                AutomationId = PickerId
            }),
        Flow(FlowDirection.LeftToRight, TextBox(Hex, EditHex)
                .Style(LauncherStyles.Input)
                .MinHeight(34f)
                .Enabled(CanEdit)
                .Automation(new()
                {
                    Name = Resources.SkinHexColor,
                    AutomationId = HexId
                })),
        Text(InvalidMessage)
            .FontSize(11f).Foreground(Theme.Error)
            .Wrap()
            .Visible(!Valid),
    ])
        .Width(240f)
        .Spacing(12f);
}