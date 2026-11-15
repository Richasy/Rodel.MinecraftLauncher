// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Servers;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record HomeServerRow(
    string Id,
    string Name,
    string Address,
    bool Selected,
    bool Last,
    Action Select,
    ButtonStyle Appearance,
    bool RightToLeft,
    string SelectionHelp,
    ImageSource? Icon,
    Color StatusColor,
    Color StatusOutline,
    string StatusName,
    string StatusTooltip);

[View]
internal readonly partial record struct HomeServerShortcut(LauncherAppearance Theme, HomeServerRow Data)
{
    public Ui Body => Column([
        LauncherControls.Command(
Theme,            Overlay([
                Grid(
                    [GridLength.Pixels(42f), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        new ServerIconView(Data.Icon, 36f).VAlign(VerticalAlignment.Center)
                            .Cell(0, 0),
                        Column([
                            Text(Data.Name)
                                .FontSize(14f).FontWeight(700)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                .Margin(new Thickness(0f, 0f, 12f, 0f)),
                            Flow(FlowDirection.LeftToRight, Text(Data.Address)
                                    .FontSize(11f)
                                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                    .Align(Data.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)),
                        ])
                            .Spacing(7f)
                            .MinContentWidth(0f)
                            .VAlign(VerticalAlignment.Center)
                            .Margin(new Thickness(12f, 0f, 0f, 0f))
                            .Cell(1, 0),
                    ]),
                Flow(FlowDirection.LeftToRight, new HomeServerStatusDot(Data.StatusColor, Data.StatusOutline).ToolTip(Data.StatusTooltip)
                        .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Top)
                        .Automation(new()
                        {
                            Name = Data.StatusName,
                            AutomationId = "home.server.status." + Data.Id,
                            HelpText = Data.StatusTooltip
                        })),
            ]),
            Data.Select)
            .Style(Data.Appearance)
            .AsSelected(Data.Selected)
            .MinHeight(InstanceShortcut.RowHeight)
            .Automation(new()
            {
                Name = Data.Name,
                AutomationId = "home.server." + Data.Id,
                HelpText = Data.SelectionHelp + Environment.NewLine + Data.StatusTooltip
            }),
    ]);
}