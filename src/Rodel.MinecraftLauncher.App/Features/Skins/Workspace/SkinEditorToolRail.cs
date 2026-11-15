// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
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

/// <summary>Groups lightweight tool selectors separately from the bottom foreground-color entry.</summary>
internal sealed partial class SkinEditorToolRail(SkinEditorState editor) : Control
{
    private readonly Flyout _color = new(close => new SkinEditorColorPopup(editor, close))
    {
        Placement = PopupPlacement.RightEdgeAlignedBottom,
        ShowMode = FlyoutShowMode.Standard,
        ShouldConstrainToRootBounds = true,
    };

    private Ui Body => Overlay([
        new LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(new Thickness(0f, 0f, 1f, 0f)),
        Border(Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
            [
                LauncherControls.KeyboardGroup(Column([
                    new SkinEditorToolButton(LauncherAppearance.Capture(),
                        Resources.SkinBrush,
                        "skin-brush",
                        "skin.tool.0",
                        editor.Tool == 0,
                        () => editor.Tool = 0,
                        SkinEditorChrome.ToolPalette,
                        SkinEditorChrome.ToolStyle,
                        editor.ToolHeight),
                    new SkinEditorToolButton(LauncherAppearance.Capture(),
                        Resources.SkinEraser,
                        "skin-eraser",
                        "skin.tool.1",
                        editor.Tool == 1,
                        () => editor.Tool = 1,
                        SkinEditorChrome.ToolPalette,
                        SkinEditorChrome.ToolStyle,
                        editor.ToolHeight),
                    new SkinEditorToolButton(LauncherAppearance.Capture(),
                        Resources.SkinFill,
                        "skin-fill",
                        "skin.tool.2",
                        editor.Tool == 2,
                        () => editor.Tool = 2,
                        SkinEditorChrome.ToolPalette,
                        SkinEditorChrome.ToolStyle,
                        editor.ToolHeight),
                    new SkinEditorToolButton(LauncherAppearance.Capture(),
                        Resources.SkinPicker,
                        "skin-picker",
                        "skin.tool.3",
                        editor.Tool == 3,
                        () => editor.Tool = 3,
                        SkinEditorChrome.ToolPalette,
                        SkinEditorChrome.ToolStyle,
                        editor.ToolHeight),
                    Rectangle(LauncherTheme.Edge)
                        .Height(1f)
                        .Margin(new Thickness(6f, 10f, 6f, 10f)),
                    new SkinEditorToolButton(LauncherAppearance.Capture(),
                        Resources.SkinMirror,
                        "skin-mirror",
                        "skin.mirror",
                        editor.Mirror,
                        () => editor.Mirror = !editor.Mirror,
                        SkinEditorChrome.ToolPalette,
                        SkinEditorChrome.ToolStyle,
                        editor.ToolHeight),
                    new SkinEditorToolButton(LauncherAppearance.Capture(),
                        Resources.SkinSelection,
                        "skin-selection",
                        "skin.tool.4",
                        editor.Tool == 4,
                        () => editor.Tool = 4,
                        SkinEditorChrome.ToolPalette,
                        SkinEditorChrome.ToolStyle,
                        editor.ToolHeight),
                ])
                    .Spacing(4f))
                    .Cell(0, 0),
                Column([
                    DropDownButton(new SkinEditorColorChip(editor.ForegroundColor), _color.LauncherStyle(popupAppearance=>popupAppearance.Menus.ErrorPopup.Background(popupAppearance.Materials.Acrylic)
                                .Border(popupAppearance.Edge)
                                .Padding(16f)))
                        .Style(SkinEditorChrome.ColorTrigger.Padding(0f))
                        .Width(30f).Height(30f)
                        .HAlign(HorizontalAlignment.Center)
                        .Automation(new()
                        {
                            Name = Resources.SkinColor,
                            AutomationId = "skin.color.open"
                        }),
                    Text(Resources.SkinColorLabel)
                        .FontSize(9f).Foreground(LauncherTheme.Muted)
                        .HAlign(HorizontalAlignment.Center),
                ])
                    .Spacing(8f)
                    .Cell(0, 2),
            ]))
        .Padding(7f, 12f, 7f, 16f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 1f, 0f),
    ]);

    protected override void OnDispose()
    {
        _color.Hide();
        base.OnDispose();
    }
}

[View]
internal readonly partial record struct SkinEditorToolButton(
    LauncherAppearance Theme,
    string Label,
    string Icon,
    string Id,
    bool Selected,
    Action Choose,
    ToggleButtonPalette Palette,
    Sprout.Styling.ToggleButtonStyle Style,
    float Height,
    bool Enabled = true)
{
    internal Action<bool?> Toggled
    {
        get
        {
            var choose = Choose;
            return _ => choose();
        }
    }

    /// <summary>Displays one full-width tool target with native toggle semantics.</summary>
    public Ui Body => Row([
        LauncherControls.ToolToggle(Theme,
            Row([
                LauncherIcons.Line(Icon, 21f)
            ]),
            Selected,
            Toggled,
            Enabled,
            Palette,
            Style)
            .Height(Height)
            .Weight(1f)
            .ToolTip(Label)
            .Automation(new()
            {
                Name = Label,
                AutomationId = Id,
                AcceleratorKey = Icon switch
                {
                    "skin-brush" => "B",
                    "skin-eraser" => "E",
                    "skin-fill" => "G",
                    "skin-picker" => "I",
                    "skin-mirror" => "M",
                    "skin-selection" => "R",
                    _ => null
                }
            })
    ]);
}