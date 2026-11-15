// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed partial class HomeServerStatusDot : Control
{
    internal HomeServerStatusDot(Color fill, Color edge)
    {
        Fill = fill;
        Edge = edge;
    }

    private partial Color Fill { get; set; }
    private partial Color Edge { get; set; }

    private Ui Body => Border()
        .Background(Fill)
        .BorderBrush(Edge)
        .BorderThickness(1f)
        .CornerRadius(4f)
        .Width(8f).Height(8f);

    public override void ReconcileFrom(Control source)
    {
        if (source is HomeServerStatusDot other)
        {
            Fill = other.Fill;
            Edge = other.Edge;
        }
    }
}