// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

internal sealed record ServerRowData(
    ServerFavorite Favorite,
    string StatusTitle,
    string StatusDetail,
    string StatusError,
    string Instance,
    bool Compact,
    Action Connect,
    Action More,
    string ConnectLabel,
    string ConnectName,
    string MoreLabel,
    Brush Ink,
    Brush Muted,
    Brush Edge,
    bool RightToLeft,
    ImageSource? Icon);

[View]
internal readonly partial record struct ServerRow(LauncherAppearance Theme, ServerRowData Data)
{
    internal static GridLength[] Columns(bool compact) => compact ? [GridLength.Star(), GridLength.Pixels(112f)] : [GridLength.Star(1.3f), GridLength.Star(), GridLength.Star(.8f), GridLength.Pixels(112f)];
    internal GridLength[] RowDefinitions => Data.Compact ? [GridLength.Auto, GridLength.Auto, GridLength.Auto] : [GridLength.Auto];

    public Ui Body => Border(Grid(
            Columns(Data.Compact),
            RowDefinitions,
            [
                Grid(
                    [GridLength.Pixels(36f), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        new ServerIconView(Data.Icon).VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Column([
                            Text(Data.Favorite.Name)
                                .FontSize(14f).FontWeight(600).Foreground(Data.Ink)
                                .NoWrap()
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                            Flow(FlowDirection.LeftToRight, Text(Data.Favorite.Address)
                                    .FontSize(12f).Foreground(Data.Muted)
                                    .NoWrap()
                                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                    .Align(Data.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)),
                        ])
                            .Spacing(5f)
                            .MinContentWidth(0f)
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f)
                    .Cell(0, 0),
                Column([
                    Text(Data.StatusTitle)
                        .FontSize(12f).Foreground(Data.Ink)
                        .NoWrap()
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Automation(new()
                        {
                            Name = Data.StatusTitle,
                            HelpText = Data.StatusError
                        }),
                    Flow(FlowDirection.LeftToRight, Text(Data.StatusDetail)
                            .FontSize(11f).Foreground(Data.Muted)
                            .NoWrap()
                            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                            .Align(Data.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)),
                ])
                    .Spacing(5f)
                    .MinContentWidth(0f)
                    .Cell(Data.Compact ? 0 : 1, Data.Compact ? 1 : 0),
                Text(Data.Instance)
                    .FontSize(12f).Foreground(Data.Muted)
                    .NoWrap()
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .VAlign(VerticalAlignment.Center)
                    .Cell(Data.Compact ? 0 : 2, Data.Compact ? 2 : 0),
                Row([
                    LauncherControls.OutlineCommand(Theme,Data.ConnectLabel, Data.Connect)
                        .Automation(new()
                        {
                            Name = Data.ConnectName,
                            AutomationId = "servers.connect." + Data.Favorite.Id
                        }),
                    LauncherControls.Command(
Theme,                        Row([
                            LauncherIcons.Line("more", 18f)
                        ]),
                        Data.More)
                        .Style(Theme.Buttons.IconButton)
                        .Width(28f).Height(28f)
                        .Automation(new()
                        {
                            Name = Data.MoreLabel,
                            AutomationId = "servers.more." + Data.Favorite.Id
                        }),
                ])
                    .Spacing(8f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(Data.Compact ? 1 : 3, 0),
            ])
            .ColumnSpacing(20f).RowSpacing(Data.Compact ? 8f : 0f)
            .Padding(Data.Compact ? 16f : 24f, 16f))
        .BorderBrush(Data.Edge)
        .BorderThickness(0f, 1f, 0f, 0f);
}