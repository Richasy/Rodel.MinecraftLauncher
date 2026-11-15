// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiHistoryPanel(
    Action newConversation,
    Action<string> select,
    Func<string, IFlyout?> menu,
    bool drawer = false) : Control
{
    private readonly AiHistoryItems _items = new(select, menu, drawer ? 260f : 184f, drawer);

    internal void SetItems(AiHistoryItem[] items) => _items.SetItems(items);

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            LauncherControls.Command(
                Row([
                    LauncherIcons.Line("plus", 16f),
                    Text(Resources.AiNewChat),
                ])
                    .Spacing(8f),
                newConversation)
                .Margin(drawer ? new Thickness(12f, 12f, 12f, 0f) : default)
                .Automation(new()
                {
                    Name = Resources.AiNewChat,
                    AutomationId = "ai.history.new",
                })
                .Cell(0, 0),
            ScrollView(_items)
                .Automation(new()
                {
                    Name = Resources.AiRecentChats,
                    AutomationId = "ai.history.list",
                })
                .Cell(0, 1),
        ])
        .RowSpacing(14f)
        .Width(drawer ? 284f : 184f).MaxHeight(drawer ? 420f : float.PositiveInfinity);
}

internal sealed partial class AiHistoryItems(Action<string> select, Func<string, IFlyout?> menu, float width, bool inset) : Control
{
    private AiHistoryItem[] _snapshot = [];
    private partial AiHistoryItem[] Items { get; set; }
    private AiHistoryItem[] Current => Items ?? [];

    internal void SetItems(AiHistoryItem[] items)
    {
        if (_snapshot.SequenceEqual(items))
            return;
        _snapshot = items;
        Items = items;
    }

    private Ui Body => Column([
        Text(Resources.AiHistoryEmpty)
            .FontSize(11f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(Current.Length == 0),
        ItemsRepeater(
            Current,
            static item => item.Key,
            item => new AiHistoryRow(item, select, menu, width),
            static (row, item) => row.Update(item),
            new StackLayout
            {
                EstimatedItemExtent = 40f,
                Spacing = 4f,
            }).KeyboardNavigation(),
    ])
        .Spacing(4f)
        .Padding(inset ? new Thickness(12f, 0f, 12f, 12f) : default);
}