// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Displays one history projection and routes selection and menu intents through its stable key.</summary>
internal sealed partial class AiHistoryRow : Control
{
    private readonly Action<string> _select;
    private readonly Func<string, IFlyout?> _menu;
    private readonly float _width;
    private partial AiHistoryItem Item { get; set; }

    internal AiHistoryRow(
        AiHistoryItem item,
        Action<string> select,
        Func<string, IFlyout?> menu,
        float width)
    {
        Item = item;
        _select = select;
        _menu = menu;
        _width = width;
    }

    private string Title => Item.Name.Length == 0 ? Resources.AiNewChat : Item.Name;
    private string Context => Item.InstanceName is { } name
        ? name.Length == 0 ? Resources.UnreadableInstance : name
        : Item.Repair ? Item.InstanceId ?? Resources.UnreadableInstance : Resources.AiNoInstance;
    private bool Selected => Item.Selected;
    private string Badge => Item.Badge switch
    {
        AiHistoryBadge.NeedsInput => Resources.AiChatNeedsInput,
        AiHistoryBadge.Responding => Resources.AiChatResponding,
        AiHistoryBadge.Queued => Resources.AiChatQueued,
        AiHistoryBadge.Unread => Resources.AiChatUnread,
        _ => string.Empty,
    };
    private Color BadgeInk => Selected && LauncherTheme.HighContrast
        ? LauncherTheme.SelectionForeground
        : Item.NeedsInput ? LauncherTheme.Warning : LauncherTheme.Ink;

    internal void Update(AiHistoryItem item) => Item = item;

    private void Select() => _select(Item.Key);

    private IFlyout? ContextMenu() => _menu(Item.Key);

    private Ui Body => LauncherControls.Command(
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto, GridLength.Auto],
            [
                Text(Title)
                    .FontSize(12f)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Automation(new()
                    {
                        Name = Title,
                        AutomationId = "ai.history.title." + Item.ConversationId,
                    })
                    .Cell(0, 0),
                Border(Text(Badge)
                        .FontSize(9f).Foreground(BadgeInk)
                        .Automation(new()
                        {
                            Name = Badge,
                            AutomationId = "ai.history.badge." + Item.ConversationId,
                        }))
                    .Padding(5f, 3f)
                    .Background(LauncherTheme.HighContrast ? Colors.Transparent : LauncherTheme.ControlBackground)
                    .BorderBrush(BadgeInk)
                    .BorderThickness(LauncherTheme.HighContrast ? 1f : 0f)
                    .VAlign(VerticalAlignment.Center)
                    .Visible(Badge.Length > 0)
                    .Cell(1, 0),
                Text(Context)
                    .FontSize(10f).Foreground(Selected && LauncherTheme.HighContrast ? LauncherTheme.SelectionForeground : LauncherTheme.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Visible(Item.InstanceId is not null)
                    .MaxWidth(_width - 24f)
                    .Automation(new()
                    {
                        Name = Context,
                        AutomationId = "ai.history.context." + Item.ConversationId,
                    })
                    .Cell(0, 1),
            ])
            .ColumnSpacing(Badge.Length > 0 ? 6f : 0f).RowSpacing(Item.InstanceId is null ? 0f : 6f),
        Select)
        .Style(LauncherButtons.Navigation(Selected)
            .Padding(Selected ? 11f : 12f, 8f)
            .ContentAlignment(HorizontalAlignment.Stretch, VerticalAlignment.Center))
        .AsSelected(Selected)
        .ContextFlyout(ContextMenu)
        .MinHeight(Item.InstanceId is null ? 40f : 56f).Width(_width).MaxWidth(_width)
        .Automation(new()
        {
            Name = Title,
            HelpText = Badge,
            AutomationId = "ai.history.item." + Item.ConversationId,
        });
}