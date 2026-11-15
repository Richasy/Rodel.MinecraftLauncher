// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

[View]
internal readonly partial record struct InstanceShortcut(
    LauncherAppearance Theme,
    InstanceSummary Item,
    bool Selected,
    Action Activate,
    bool Last,
    ButtonStyle Appearance,
    Func<IFlyout?> ContextMenu)
{
    internal const float RowHeight = 79f;
    internal const float RowGap = 8f;

    /// <summary>Builds the selectable instance row with a subtitle derived from its instance metadata.</summary>
    public Ui Body => LauncherControls.Command(
Theme, Grid(
            [GridLength.Pixels(42f), GridLength.Star()],
            [GridLength.Auto],
            [
                new Rodel.MinecraftLauncher.App.Features.Instances.InstanceIconView(Item.IconData).VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text((string.IsNullOrEmpty(Item.Name) ? Resources.UnreadableInstance : Item.Name))
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .FontSize(14f).FontWeight(700)
                        .HAlign(HorizontalAlignment.Left),
                    Text(Item.Unreadable ? Resources.MetadataUnreadable : Item.Subtitle(Resources.VersionNotConfigured, Resources.Vanilla))
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .FontSize(11f)
                        .HAlign(HorizontalAlignment.Left),
                ])
                    .Spacing(7f)
                    .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Center)
                    .Margin(new Thickness(12f, 0f, 0f, 0f))
                    .Cell(1, 0),
            ]),
        Activate)
        .Style(Appearance)
        .AsSelected(Selected)
        .ContextFlyout(ContextMenu)
        .Automation(new()
        {
            Name = (string.IsNullOrEmpty(Item.Name) ? Resources.UnreadableInstance : Item.Name),
            AutomationId = $"instance.{Item.Id}",
            HelpText = Selected ? Resources.CurrentInstance : Resources.SwitchInstance,
        })
        .MinHeight(RowHeight);
}