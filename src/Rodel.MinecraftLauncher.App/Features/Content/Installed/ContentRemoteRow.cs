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
internal readonly partial record struct ContentRemoteRow(LauncherAppearance Theme, ContentRowState Data, ContentPage Page)
{
    internal ContentSearchHit Item => Data.Remote!;
    internal Action Add
    {
        get
        {
            var data = Data;
            var page = Page;
            return () =>
            {
                if (data.Installed)
                    page.ViewInstalled(data.Kind);
                else
                    page.ShowInstall(data.Remote!);
            };
        }
    }

    public Ui Body => Border(Grid(
            [GridLength.Pixels(42f), GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                new ContentRemoteIconView(Item.IconUrl, Resources.ContentIconUnavailable, Data.Appearance.Muted, Data.Appearance.Edge).VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text(Item.Name)
                        .FontSize(14f).FontWeight(600).Foreground(Data.Appearance.Ink)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                    Text(Item.Kind.Title())
                        .FontSize(11f).Foreground(Data.Appearance.Muted),
                    Text(Item.Description)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                ])
                    .Spacing(5f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                LauncherControls.OutlineCommand(Theme,Data.Installed ? Resources.ContentViewInstalled : Resources.ContentAdd, Add)
                    .Style(Data.Appearance.Command)
                    .Enabled(Data.CanChange)
                    .VAlign(VerticalAlignment.Center)
                    .Automation(new()
                    {
                        Name = string.Format(Resources.InstanceManageAction, Data.Installed ? Resources.ContentViewInstalled : Resources.ContentAdd, Item.Name),
                        AutomationId = "content.online.add." + Item.Id
                    })
                    .Cell(2, 0),
            ])
            .ColumnSpacing(16f)
            .Padding(20f, 14f))
        .MinHeight(76f)
        .BorderBrush(Data.Appearance.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}