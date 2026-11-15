// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

[View]
internal readonly partial record struct LaunchStatusView(
    LauncherAppearance Theme,
    string Title,
    string Detail,
    string Error,
    bool HasError,
    bool Critical,
    Action ExpandError,
    Brush Surface,
    Color Ink,
    Color Edge,
    ButtonStyle ActionStyle,
    bool Preparing,
    double? FilePercent,
    Color Progress,
    Color Track,
    bool RightToLeft,
    bool Original,
    Action? Cancel = null,
    bool CanCancel = false)
{
    internal string Summary => Error.Length > 0 ? string.Format(Resources.LaunchErrorDetail, Error) : Detail;

    public Ui Body => Border(Border(Column([
                Grid(
                    [GridLength.Auto, GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        LauncherIcons.Line(Critical ? "error" : "info", 20f, Critical ? Edge : Ink)
                            .Visible(HasError)
                            .VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Flow(FlowDirection.LeftToRight, new SelectableText(Text(Title)
                                    .FlowDirection(Original || !RightToLeft ? FlowDirection.LeftToRight : FlowDirection.RightToLeft)
                                    .FontSize(14f).FontWeight(700).Foreground(Ink)
                                    .Wrap()
                                    .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)).HAlign(HorizontalAlignment.Stretch).VAlign(VerticalAlignment.Center)
                                .Automation(new()
                                {
                                    Name = Title,
                                    AutomationId = "home.launch.status",
                                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                                }))
                            .Cell(1, 0),
                        Row([
                            LauncherControls.Command(
Theme,                                Row([
                                    LauncherIcons.Line("expand", 16f)
                                ]),
                                ExpandError)
                                .Style(ActionStyle)
                                .Width(28f).Height(28f)
                                .Automation(new()
                                {
                                    Name = Resources.ExpandLaunchError,
                                    AutomationId = "home.launch.expand",
                                })
                                .Visible(HasError),
                            LauncherControls.OutlineCommand(
Theme,                                Row([
                                    LauncherIcons.Line("close", 16f)
                                ]),
                                Cancel)
                                .Style(Theme.Buttons.FlatIconButton)
                                .Width(28f).Height(28f)
                                .Enabled(CanCancel)
                                .Visible(Preparing && Cancel is not null)
                                .ToolTip(Resources.CancelLaunch)
                                .Automation(new()
                                {
                                    Name = Resources.CancelLaunch,
                                    AutomationId = "home.launch.cancel"
                                }),
                        ])
                            .Spacing(4f)
                            .Visible(HasError || (Preparing && Cancel is not null))
                            .Cell(2, 0),
                    ])
                    .ColumnSpacing(HasError ? 8f : 0f),
                Flow(FlowDirection.LeftToRight, new SelectableText(Text(Summary)
                            .FlowDirection(Original || !RightToLeft ? FlowDirection.LeftToRight : FlowDirection.RightToLeft)
                            .FontSize(12f)
                            .Wrap()
                            .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)
                            .Trim(TextTrimming.CharacterEllipsis)
                            .Foreground(Ink)).ShowTooltipWhenTrimmed()
                        .HAlign(HorizontalAlignment.Stretch)
                        .MaxHeight(72f)
                        .Automation(new()
                        {
                            Name = Summary,
                            AutomationId = "home.launch.detail"
                        })),
                ProgressBar()
                    .Indeterminate(FilePercent is null)
                    .Value(FilePercent ?? 0d)
                    .Thickness(3f)
                    .TrackThickness(3f)
                    .Foreground(Progress)
                    .Track(Track)
                    .Automation(new()
                    {
                        Name = Resources.LaunchPreparing,
                        AutomationId = "home.launch.progress",
                    })
                    .Visible(Preparing),
            ])
                .Spacing(8f))
            .Padding(12f)
            .Background(Critical ? Edge.MultiplyAlpha(.12f) : Colors.Transparent))
        .Background(Surface)
        .BorderBrush(Edge)
        .BorderThickness(1f);
}