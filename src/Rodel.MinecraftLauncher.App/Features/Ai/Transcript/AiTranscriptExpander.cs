// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shares the chat's non-animated disclosure header for thinking and grouped tool activity.</summary>
[View]
internal readonly partial record struct AiTranscriptExpander(
    LauncherAppearance Theme,
    string Title,
    string Icon,
    string Status,
    bool Expanded,
    Action Toggle,
    string Id,
    string Elapsed = "")
{
    internal static string Duration(TimeSpan? elapsed) => elapsed is not { } time
        ? string.Empty
        : time.TotalSeconds < 60 ? string.Format(Resources.AiElapsedSeconds, time.TotalSeconds.ToString("0.0")) : string.Format(Resources.AiElapsedMinutes, (int)time.TotalMinutes, time.Seconds);

    /// <summary>Retains the same title, status and chevron geometry in every chat disclosure.</summary>
    public Ui Body => LauncherControls.OutlineCommand(
Theme, Grid(
            [GridLength.Auto, GridLength.Star(), GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [GridLength.Auto],
            [
                LauncherIcons.Line(Icon, 16f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Text(Title)
                    .FontSize(12f).Foreground(Theme.Ink)
                    .Wrap()
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                Text(Status)
                    .FontSize(11f).Foreground(Theme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Visible(Status.Length > 0)
                    .Cell(2, 0),
                Text(Elapsed)
                    .FontSize(11f).Foreground(Theme.Quiet)
                    .VAlign(VerticalAlignment.Center)
                    .Visible(Elapsed.Length > 0)
                    .Cell(3, 0)
                    .Automation(new()
                    {
                        Name = Elapsed,
                        AutomationId = Id + ".elapsed"
                    }),
                LauncherIcons.Line(Expanded ? "chevron-up" : "chevron-down", 14f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(4, 0),
            ])
            .ColumnSpacing(10f),
        Toggle)
        .Style(Theme.Buttons.AiTextAction.Padding(12f, 10f)
            .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .Automation(new()
        {
            Name = Title,
            AutomationId = Id,
            HelpText = Expanded ? Resources.AiHideDetails : Resources.AiDetails
        });
}