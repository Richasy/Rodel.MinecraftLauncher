// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

internal sealed partial class SkinEditorToolOptions(SkinEditorState editor, Action close) : Control
{
    private readonly SkinBrushSlider _slider = new(editor);

    private Action Run(Action action) => () =>
    {
        close();
        action();
    };

    private Ui Body => Column([
        Row([
            Text(editor.ToolName)
                .FontSize(16f).FontWeight(600)
                .Weight(1f)
                .VAlign(VerticalAlignment.Center),
            Text(string.Format(Resources.SkinSelectionCount, editor.Selection?.Count ?? 0))
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .VAlign(VerticalAlignment.Center)
                .Visible(editor.Selection is not null),
        ])
            .Spacing(12f),
        Text(string.Format(Resources.SkinBrushSize, editor.BrushSize))
            .FontSize(13f)
            .Visible(editor.Tool != 4),
        _slider.Visible(editor.Tool != 4),
        Border(Rectangle(editor.ForegroundColor)
                .Width((float)editor.BrushSize * 8f).Height((float)editor.BrushSize * 8f)
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
            .Height(56f)
            .Background(LauncherTheme.ControlBackground)
            .Visible(editor.Tool != 4),
        Text(string.Format(Resources.SkinFootprint, editor.BrushSize))
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Visible(editor.Tool != 4),
        Column([
            Grid(
                [GridLength.Star(), GridLength.Star()],
                [GridLength.Auto, GridLength.Auto],
                [
                    new SkinTransferCommand(
LauncherAppearance.Capture(),                        Resources.SkinCopySelection,
                        "copy",
                        Run(editor.Controller.Transfers.Copy),
                        editor.Controller.Transfers.CanCopy,
                        "skin.selection.copy",
                        "Ctrl+C").Cell(0, 0),
                    new SkinTransferCommand(
LauncherAppearance.Capture(),                        Resources.SkinPasteSelection,
                        "clipboard",
                        Run(editor.Controller.Transfers.Paste),
                        editor.Controller.Transfers.CanPaste,
                        "skin.selection.paste",
                        "Ctrl+V").Cell(1, 0),
                    new SkinTransferCommand(
LauncherAppearance.Capture(),                        Resources.SkinCutSelection,
                        "cut",
                        Run(() => _ = editor.Controller.Transfers.DeleteAsync(true)),
                        editor.Controller.Transfers.CanChange,
                        "skin.selection.cut",
                        "Ctrl+X").Cell(0, 1),
                    new SkinTransferCommand(
LauncherAppearance.Capture(),                        Resources.SkinDeleteSelection,
                        "delete",
                        Run(() => _ = editor.Controller.Transfers.DeleteAsync()),
                        editor.Controller.Transfers.CanChange,
                        "skin.selection.delete",
                        "Delete").Cell(1, 1),
                ])
                .ColumnSpacing(8f).RowSpacing(8f),
            Border(Column([
                    Text(Resources.SkinSelectionFill)
                        .FontSize(12f),
                    TextBox(editor.SelectionFill, value => editor.SelectionFill = value)
                        .Style(LauncherStyles.Input.ContentFontSize(12f))
                        .MinHeight(32f)
                        .Automation(new()
                        {
                            Name = Resources.SkinSelectionFill,
                            AutomationId = "skin.selection.fill"
                        }),
                    Text(Resources.SkinTransferOpaque)
                        .FontSize(11f).Foreground(LauncherTheme.Error)
                        .Wrap()
                        .Visible(!editor.Controller.Transfers.ValidFill),
                ])
                    .Spacing(8f))
                .Padding(12f)
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Visible((editor.Tool == 4) && editor.Controller.Transfers.NeedsFill),
            new SkinTransferCommand(
LauncherAppearance.Capture(),                Resources.SkinClearSelection,
                "skin-selection",
                Run(editor.Controller.Input.ClearSelection),
                editor.Selection is not null && editor.Controller.Files.Available,
                "skin.selection.clear",
                "Ctrl+D"),
        ])
            .Spacing(12f)
            .Visible(!editor.Pasting && ((editor.Tool == 4) || editor.Selection is not null)),
        Grid(
            [GridLength.Star(), GridLength.Star()],
            [GridLength.Auto],
            [
                new SkinTransferCommand(
LauncherAppearance.Capture(),                    Resources.SkinPlaceSelection,
                    "check",
                    Run(editor.Controller.Transfers.ConfirmPaste),
                    !editor.Busy && !editor.Selecting,
                    "skin.selection.place",
                    "Enter",
                    true).Cell(0, 0),
                new SkinTransferCommand(
LauncherAppearance.Capture(),                    Resources.DialogCancel,
                    "close",
                    Run(editor.Controller.Transfers.CancelPaste),
                    !editor.Busy,
                    "skin.selection.cancel",
                    "Esc").Cell(1, 0),
            ])
            .ColumnSpacing(8f)
            .Visible(editor.Pasting),
    ])
        .Spacing(12f)
        .Width(240f);
}