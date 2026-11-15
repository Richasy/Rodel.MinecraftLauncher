// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ScreenshotFooter(
    LauncherAppearance Theme,
    string Count,
    int VisibleCount,
    int SelectedCount,
    bool Selecting,
    bool Busy,
    bool HasReport,
    Action SelectAll,
    Action Clear,
    Action Copy,
    Action Export,
    Action Recycle,
    Action Report)
{
    internal bool CanSelectAll => SelectedCount < VisibleCount;

    /// <summary>Shows selection-only actions without taking space from the gallery when no selection mode is active.</summary>
    public Ui Body => Column([
        Row([
            Text(Count).FontSize(11f).Foreground(Theme.Muted).Weight(1f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = Count, AutomationId = "screenshot.count" }),
            LauncherControls.OutlineCommand(Theme, Resources.ContentOperationDetails, Report)
                .Style(Theme.Buttons.DialogHeaderButton)
                .VAlign(VerticalAlignment.Center)
                .Visible(HasReport).Enabled(!Busy)
                .Automation(new() { Name = Resources.ContentOperationDetails, AutomationId = "screenshot.report" }),
        ]).Spacing(8f).MinHeight(32f).Visible(!Selecting),
        Row([
            Text(string.Format(Resources.ScreenshotSelectedCount, SelectedCount))
                .FontSize(11f).Foreground(Theme.Muted).Weight(1f)
                .VAlign(VerticalAlignment.Center)
                .Automation(new() { Name = string.Format(Resources.ScreenshotSelectedCount, SelectedCount), AutomationId = "screenshot.selected.count" }),
            Row([
                LauncherControls.OutlineCommand(Theme, Row([LauncherIcons.Line("copy", 18f)]), Copy)
                    .Style(Theme.Buttons.FlatIconButton).Width(36f).Height(32f).Enabled(!Busy && SelectedCount == 1)
                    .ToolTip(Resources.ScreenshotCopyFiles)
                    .Automation(new() { Name = Resources.ScreenshotCopyFiles, AutomationId = "screenshot.copy" }),
                LauncherControls.OutlineCommand(Theme, Row([LauncherIcons.Line("export", 18f)]), Export)
                    .Style(Theme.Buttons.FlatIconButton).Width(36f).Height(32f).Enabled(!Busy)
                    .ToolTip(Resources.ScreenshotExport)
                    .Automation(new() { Name = Resources.ScreenshotExport, AutomationId = "screenshot.export" }),
                LauncherControls.OutlineCommand(Theme, Row([LauncherIcons.Line("delete", 18f)]), Recycle)
                    .Style(Theme.Buttons.CriticalIcon).Width(36f).Height(32f).Enabled(!Busy)
                    .ToolTip(Resources.ScreenshotDeleteTitle)
                    .Automation(new() { Name = Resources.ScreenshotDeleteTitle, AutomationId = "screenshot.recycle" }),
            ]).Spacing(8f).Visible(SelectedCount > 0),
            LauncherControls.OutlineCommand(Theme,
                    CanSelectAll ? Resources.ScreenshotSelectAll : Resources.ScreenshotDeselectAll,
                    CanSelectAll ? SelectAll : Clear)
                .Style(Theme.Buttons.DialogHeaderButton).Enabled(!Busy)
                .VAlign(VerticalAlignment.Center)
                .Automation(new()
                {
                    Name = CanSelectAll ? Resources.ScreenshotSelectAll : Resources.ScreenshotDeselectAll,
                    AutomationId = CanSelectAll ? "screenshot.select.all" : "screenshot.select.clear"
                }),
        ])
            .Spacing(8f)
            .MinHeight(32f)
            .Visible(Selecting),
    ]).Spacing(4f);
}