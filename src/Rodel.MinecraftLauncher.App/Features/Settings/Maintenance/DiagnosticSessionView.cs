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

internal sealed record DiagnosticSessionItem(DiagnosticSession Session, bool Selected);

[View]
internal readonly partial record struct DiagnosticSessionView(LauncherAppearance Theme, DiagnosticSessionItem Item)
{
    public Ui Body => Overlay([
        new Controls.LauncherMaterialSurface(Item.Selected ? Theme.Materials.Acrylic : Colors.Transparent).Margin(1f),
        Border(Column([
            Text(Item.Session.Time)
                .FontSize(14f).Foreground(Theme.Ink)
                .Trim(TextTrimming.CharacterEllipsis).MaxLines(1).TooltipWhenTrimmed(),
            Text(Item.Session.Status)
                .FontSize(12f).Foreground(Theme.Muted)
                .Wrap(),
        ])
            .Spacing(5f))
        .Padding(12f, 10f)
        .BorderBrush(Item.Selected ? Theme.Edge : Colors.Transparent)
        .BorderThickness(1f),
    ]);
}