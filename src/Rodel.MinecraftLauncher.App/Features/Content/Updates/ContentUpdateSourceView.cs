// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentUpdateSourceView(LauncherAppearance Theme, ContentSourceCandidate? Source, ImageSource? Icon, bool RightToLeft)
{
    /// <summary>Shows the real project icon and separates the display title from its smaller platform identifier.</summary>
    public Ui Body => Overlay([
        Text(Resources.ContentUpdatesChooseSource).FontSize(13f)
            .VAlign(VerticalAlignment.Center).Visible(Source is null),
        Grid([GridLength.Pixels(42f), GridLength.Star()], [GridLength.Auto], [
            Overlay([
                Image(Icon ?? InstanceIconView.Transparent)
                    .Width(42f).Height(42f).Stretch(ImageStretch.Uniform)
                    .Visible(Icon is not null),
                Border(Viewbox(Sprout.Markup.Path(LauncherIcons.Geometry("instances"))
                            .Stroke(Theme.Muted, 1.7f, StrokeStyle.Solid with
                            {
                                StartCap = PenLineCap.Round,
                                EndCap = PenLineCap.Round,
                                LineJoin = PenLineJoin.Round,
                            }).Width(24f).Height(24f))
                        .Width(28f).Height(28f)
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                    .BorderBrush(Theme.Edge).BorderThickness(1f)
                    .Width(42f).Height(42f)
                    .Visible(Icon is null),
            ])
                .VAlign(VerticalAlignment.Center).Cell(0, 0),
            Column([
                Text(Source?.Title ?? string.Empty)
                    .FontSize(14f)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                Flow(FlowDirection.LeftToRight, Text(Source?.Slug ?? Source?.ProjectId ?? string.Empty)
                    .FontSize(11f).Foreground(Theme.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Align(RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)),
            ]).Spacing(2f).VAlign(VerticalAlignment.Center).Cell(1, 0),
        ]).ColumnSpacing(12f).Visible(Source is not null),
    ]);
}