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
internal readonly partial record struct ContentInstalledSearchRow(ContentRowState Data)
{
    internal ContentItem Item => Data.Local!;

    public Ui Body => Border(Grid(
            [GridLength.Pixels(42f), GridLength.Star(), GridLength.Auto],
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
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                    Text(Data.Kind.Title())
                        .FontSize(11f).Foreground(Data.Appearance.Muted),
                    Text(Item.FileName)
                        .FontSize(11f).Foreground(Data.Appearance.Muted)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                ])
                    .Spacing(5f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
                Text(Resources.ContentInstalled)
                    .FontSize(12f).Foreground(Data.Appearance.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(2, 0),
            ])
            .ColumnSpacing(16f)
            .Padding(20f, 14f))
        .MinHeight(76f)
        .BorderBrush(Data.Appearance.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}