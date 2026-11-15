// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Edits the selected custom swatch through the same native picker as the foreground popup.</summary>
internal sealed partial class SkinPaletteColorPopup(SkinPaletteDialog dialog, Action close) : Control
{
    private Ui Body => Column([
        Row([
            Text(Resources.SkinPaletteEditColor)
                .FontSize(14f).FontWeight(600)
                .Weight(1f),
            Button(
                Row([
                    LauncherIcons.Line("delete", 16f)
                ]),
                () =>
                {
                    if (dialog.Remove())
                        close();
                })
                .Style(LauncherButtons.CriticalIcon.Padding(0f))
                .Width(24f).Height(24f)
                .Enabled(dialog.Palettes.CanWrite)
                .ToolTip(Resources.SkinPaletteRemoveColor)
                .Automation(new()
                {
                    Name = Resources.SkinPaletteRemoveColor,
                    AutomationId = "skin.palette.color.remove"
                }),
            LauncherControls.OutlineCommand(
                Row([
                    LauncherIcons.Line("close", 16f)
                ]),
                close)
                .Style(LauncherButtons.PopupCloseButton)
                .Width(24f).Height(24f)
                .ToolTip(Resources.SkinCloseColor)
                .Automation(new()
                {
                    Name = Resources.SkinCloseColor,
                    AutomationId = "skin.palette.picker.close"
                }),
        ])
            .Spacing(8f),
        new SkinColorPickerFields(
LauncherAppearance.Capture(),            dialog.SelectedColor,
            dialog.Hex,
            dialog.ValidHex,
            value => dialog.SetColor(value),
            value => dialog.SetHex(value),
            Resources.SkinPaletteInvalidColor,
            "skin.palette.color.hsv",
            "skin.palette.picker.hex"),
        Text(dialog.Palettes.Error)
            .FontSize(11f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Visible(dialog.Palettes.Error.Length > 0),
    ])
        .Width(240f)
        .Spacing(12f);
}