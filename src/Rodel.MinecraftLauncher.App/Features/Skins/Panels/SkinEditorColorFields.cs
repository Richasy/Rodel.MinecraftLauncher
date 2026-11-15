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
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Shows the foreground value and the selected built-in or locally editable palette.</summary>
internal sealed partial class SkinEditorColorFields(SkinEditorState editor) : Control
{
    private Ui Body => Column([
        Text(Resources.SkinColorLabel)
            .FontSize(13f).FontWeight(600),
        Grid(
            [GridLength.Auto, GridLength.Star()],
            [GridLength.Auto],
            [
                Border(Rectangle(editor.ForegroundColor))
                    .Padding(3f)
                    .Width(35f).Height(34f)
                    .VAlign(VerticalAlignment.Center)
                    .BorderBrush(LauncherTheme.Edge)
                    .BorderThickness(1f)
                    .Cell(0, 0),
                Flow(FlowDirection.LeftToRight, TextBox(editor.Foreground, value => editor.Foreground = value)
                        .Style(LauncherStyles.Input.ContentFontSize(12f))
                        .MinHeight(34f)
                        .Enabled(!editor.TransparentInk)
                        .Automation(new()
                        {
                            Name = Resources.SkinHexColor,
                            AutomationId = "skin.color.hex"
                        }))
                    .Cell(1, 0),
            ])
            .ColumnSpacing(9f),
        Text(Resources.SkinColorInvalid)
            .Foreground(LauncherTheme.Error).FontSize(11f)
            .Wrap()
            .Visible(!editor.ValidColor),
        new LauncherCheckBox(
            LauncherAppearance.Capture(),
            Resources.SkinTransparentColor,
            editor.TransparentInk,
            editor.ToggleTransparentInk,
            true,
            "skin.color.transparent",
            FontSize: 12f),
        Grid(
            [GridLength.Star(), GridLength.Auto, GridLength.Auto],
            [GridLength.Auto],
            [
                LauncherControls.Choice(editor.Palettes.HasPalette ? [..editor.Palettes.Items.Select(SkinPaletteController.DisplayName)] : [Resources.SkinPaletteEmpty])
                    .SelectedIndex(editor.Palettes.HasPalette ? editor.Palettes.SelectedIndex : 0)
                    .OnSelectionChanged(editor.Palettes.Select)
                    .Enabled(editor.Palettes.HasPalette)
                    .FaceplateStyle(LauncherMenus.ChoiceFace.Padding(8f, 4f)
                        .LabelStyle(LauncherStyles.Text.FontSize(11f)))
                    .ItemHeight(28f)
                    .MinHeight(28f)
                    .Automation(new()
                    {
                        Name = Resources.SkinPalette,
                        AutomationId = "skin.palette"
                    })
                    .Cell(0, 0),
                new SkinPaletteCreateButton(editor)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                Button(
                    Row([
                        LauncherIcons.Line("more", 15f)
                    ]),
                    () => editor.Palettes.Show())
                    .Style(SkinEditorChrome.QuietIconCommand)
                    .Width(28f).Height(28f)
                    .Enabled(editor.Palettes.HasPalette)
                    .VAlign(VerticalAlignment.Center)
                    .ToolTip(Resources.SkinPaletteManage)
                    .Automation(new()
                    {
                        Name = Resources.SkinPaletteManage,
                        AutomationId = "skin.palette.manage"
                    })
                    .Cell(2, 0),
            ])
            .ColumnSpacing(7f),
        new SkinPaletteSwatches(
LauncherAppearance.Capture(),            editor.Palettes.Current.Colors,
            editor.Foreground,
            -1,
            editor.InspectorWidth - (2f * editor.SectionPadding),
            editor.Palettes.CanWrite && editor.ValidColor,
            editor.Palettes.IsBuiltIn ? Resources.SkinPaletteCopyAdd : Resources.SkinPaletteAdd,
            "skin.palette.swatch.",
            editor.Palettes.Pick,
            AddForeground).Visible(editor.Palettes.HasPalette),
        Text(editor.Palettes.Error)
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(editor.Palettes.Error.Length > 0),
    ])
        .Spacing(12f);

    private void AddForeground()
    {
        if (!editor.ValidColor)
            return;
        if (editor.Palettes.IsBuiltIn)
            editor.Palettes.Show(copyAndAdd: true);
        else
            editor.Palettes.Run(store => store.AddColor(editor.Foreground));
    }
}