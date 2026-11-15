// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record JavaCatalogItem(string Component, string Label, bool Selected);

[View]
internal readonly partial record struct JavaCatalogRow(LauncherAppearance Theme, JavaCatalogItem Item)
{
    public Ui Body => Overlay([
        new Controls.LauncherMaterialSurface(Item.Selected ? Theme.Materials.Acrylic : Colors.Transparent).Margin(1f),
        Border(Column([
            Text(Item.Label)
                .FontSize(14f).Foreground(Theme.Ink)
                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
            Text(Item.Component)
                .FontSize(11f).Foreground(Theme.Muted)
                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                .FlowDirection(FlowDirection.LeftToRight),
        ])
            .Spacing(5f))
        .Padding(12f, 10f)
        .BorderBrush(Item.Selected ? Theme.Edge : Colors.Transparent)
        .BorderThickness(1f),
    ]);
}