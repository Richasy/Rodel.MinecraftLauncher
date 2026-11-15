// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Animation;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

[View]
internal readonly partial record struct AiWorkspaceView(
    LauncherAppearance Theme,
    AiPage Owner,
    bool InlineHistory,
    Brush Surface,
    Color Ink,
    Color Edge,
    ButtonStyle CloseButton,
    string ConversationTitle,
    bool Repair,
    bool Responding,
    bool ApprovalVisible,
    string ApprovalName)
{
    internal GridLength[] Columns => InlineHistory ? [GridLength.Pixels(184f), GridLength.Star()] : [GridLength.Star()];

    /// <summary>Builds the current localized AI presentation using the launcher control roles.</summary>
    public Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Pixels(LauncherLayout.PageHeaderHeight), GridLength.Star()],
        [
            Grid(
                [GridLength.Star(), GridLength.Auto],
                [GridLength.Auto],
                [
                    Text(Resources.NavAssistant)
                        .FontSize(32f).FontWeight(700).Foreground(Ink)
                        .Automation(new()
                        {
                            Name = Resources.NavAssistant,
                            AutomationId = "ai.heading"
                        })
                        .Cell(0, 0),
                    LauncherControls.OutlineCommand(
Theme,                        Row([
                            LauncherIcons.Line("settings", 20f)
                        ]),
                        Owner.OpenSettings)
                        .Style(CloseButton)
                        .Width(36f).Height(36f)
                        .ToolTip(Resources.AiSettingsOpen)
                        .Automation(new()
                        {
                            Name = Resources.AiSettingsOpen,
                            AutomationId = "ai.settings"
                        })
                        .Cell(1, 0),
                ])
                .ColumnSpacing(16f)
                .VAlign(VerticalAlignment.Bottom)
                .Cell(0, 0),
            Grid(
                Columns,
                [GridLength.Star()],
                [
                    Owner.History.Visible(InlineHistory)
                        .Cell(0, 0),
                    Overlay([
                        new Controls.LauncherMaterialSurface(Surface).Margin(1f),
                        Border(Grid(
                            [GridLength.Star()],
                            [GridLength.Auto, GridLength.Star(), GridLength.Auto],
                            [
                                Border(Grid(
                                        [GridLength.Auto, GridLength.Star()],
                                        [GridLength.Auto],
                                        [
                                            LauncherControls.OutlineCommand(
Theme,                                                Row([
                                                    LauncherIcons.Line("history", 18f)
                                                ]),
                                                Owner.ShowHistory)
                                                .Style(CloseButton)
                                                .Width(30f).Height(30f)
                                                .Visible(!InlineHistory)
                                                .ToolTip(Resources.AiHistoryOpen)
                                                .Automation(new()
                                                {
                                                    Name = Resources.AiHistoryOpen,
                                                    AutomationId = "ai.history.open"
                                                })
                                                .Cell(0, 0),
                                            Text(ConversationTitle)
                                                .FontSize(12f).Foreground(Ink)
                                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                                .VAlign(VerticalAlignment.Center)
                                                .Automation(new()
                                                {
                                                    Name = ConversationTitle,
                                                    AutomationId = "ai.conversation.title"
                                                })
                                                .Visible(!Repair)
                                                .Cell(1, 0),
                                            Owner.RepairBanner.Visible(Repair)
                                                .Cell(1, 0),
                                        ])
                                        .ColumnSpacing(8f))
                                    .Padding(20f, 10f)
                                    .BorderBrush(Edge)
                                    .BorderThickness(0f, 0f, 0f, 1f)
                                    .Cell(0, 0),
                                Owner.Transcript.Automation(new()
                                    {
                                        Name = Resources.AiTranscript,
                                        AutomationId = "ai.transcript"
                                    })
                                    .Cell(0, 1),
                                Border(Column([
                                        When(Responding, new AiResponseIndicator()),
                                        Owner.ApprovalBanner.Visible(ApprovalVisible)
                                            .Automation(new()
                                            {
                                                Name = string.Format(Resources.AiApprovalRequestFormat, ApprovalName),
                                                AutomationId = "ai.approval"
                                            }),
                                        Owner.Composer.Automation(new() { AutomationId = "ai.composer.surface" }),
                                    ])
                                        .Spacing(8f)
                                        .MaxWidth(LauncherLayout.AiConversationMaxWidth)
                                        .HAlign(HorizontalAlignment.Center))
                                    .Padding(20f, 0f, 20f, 16f)
                                    .Cell(0, 2),
                            ]))
                        .BorderBrush(Edge)
                        .BorderThickness(1f),
                    ]).Cell(InlineHistory ? 1 : 0, 0),
                ])
                .ColumnSpacing(InlineHistory ? 20f : 0f)
                .Cell(0, 1),
        ])
        .RowSpacing(LauncherLayout.PageHeaderGap);
}