// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentLocalRow(ContentRowState Data, ContentPage Page)
{
    public Ui Body => new ContentLocalRowControl(Data, Page);
}

internal sealed partial class ContentLocalRowControl : Control
{
    private readonly ContentPage _page;

    internal ContentLocalRowControl(ContentRowState data, ContentPage page)
    {
        Data = data;
        _page = page;
    }

    private partial ContentRowState Data { get; set; }
    private ContentItem Item => Data.Local!;
    private bool Selection => Data.Kind != ContentKind.Mods;
    private bool Checked => Selection ? Item.InUse == true : Item.Enabled;
    private string Status => Selection
        ? Item.InUse is null
        ? Resources.ContentSelectionUnavailable
        : Item.InUse == true ? Resources.ContentInUse : Resources.ContentNotInUse
        : Item.Enabled ? Resources.ContentEnabled : Resources.ContentDisabled;

    private Ui Body => Border(Grid(
            ContentListLayout.Columns(Data.Compact, _page.Window.TextScaleFactor),
            [GridLength.Auto],
            [
                new ContentLocalIcon(
                    Item,
                    Data.Appearance.Muted,
                    Data.Appearance.Edge).VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text(Item.Name)
                        .FontSize(14f).FontWeight(600).Foreground(Data.Appearance.Ink)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Automation(new()
                        {
                            Name = Item.Name,
                            AutomationId = "content.name." + Item.FileName
                        }),
                    Text(Item.FileName)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Visible(Item.Name != Item.FileName),
                    Text(Resources.ContentMetadataWarning)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Wrap()
                        .Visible(Item.MetadataWarning),
                    Text(Resources.ContentUpdatesPinned)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Visible(Item.Pinned),
                    Text(Item.Channel == "beta" ? Resources.ContentUpdatesBeta : Resources.ContentUpdatesAlpha)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Visible(Item.Channel is "beta" or "alpha"),
                    Text(Item.Priority is { } priority ? string.Format(Resources.ContentResourcePriority, priority) : string.Empty)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Visible(Item.Priority is not null),
                ])
                    .Spacing(4f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                Text(Item.Version)
                    .FontSize(11f).Foreground(Data.Appearance.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .FlowDirection(FlowDirection.DetectFromContent)
                    .Align(Data.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)
                    .Automation(new()
                    {
                        Name = Item.Version,
                        AutomationId = "content.version." + Item.FileName
                    })
                    .Visible(!Data.Compact)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(2, 0),
                Text(Status)
                    .FontSize(11f).Foreground(Data.Appearance.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Automation(new()
                    {
                        Name = Status,
                        AutomationId = "content.state." + Item.FileName
                    })
                    .VAlign(VerticalAlignment.Center)
                    .Cell(Data.Compact ? 2 : 3, 0),
                ToggleSwitch(Checked, value => _page.SetEnabled(Data.InstanceId, Data.Kind, Item, value))
                    .NoStatusText()
                    .Palette(Data.Appearance.Toggle)
                    .OnSurfacePaint(LauncherToggleSurface.Paint)
                    .Enabled(Data.CanChange && (!Selection || Item.InUse.HasValue))
                    .Automation(new()
                    {
                        Name = string.Format(Selection ? Resources.ContentUsePack : Resources.ContentToggleName, Item.Name),
                        AutomationId = "content.toggle." + Item.FileName
                    })
                    .Width(40f).Height(32f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(Data.Compact ? 3 : 4, 0),
                Border(LauncherControls.Command(
                        Row([
                            LauncherIcons.Line("more", 18f)
                        ]),
                        () => _page.ShowItemMenu(Data))
                        .Style(LauncherButtons.IconButton.Padding(0f))
                        .Width(32f).Height(32f)
                        .ToolTip(Resources.ContentUpdatesMore)
                        .Automation(new()
                        {
                            Name = string.Format(Resources.InstanceManageAction, Resources.ContentUpdatesMore, Item.Name),
                            AutomationId = "content.more." + Item.FileName
                        }))
                    .Width(32f).Height(32f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(Data.Compact ? 4 : 5, 0),
            ])
            .ColumnSpacing(ContentListLayout.ColumnSpacing)
            .Padding(ContentListLayout.HorizontalPadding, 14f))
        .MinHeight(78f)
        .BorderBrush(Data.Appearance.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);

    public override void ReconcileFrom(Control source)
    {
        if (source is ContentLocalRowControl other)
            Data = other.Data;
    }
}