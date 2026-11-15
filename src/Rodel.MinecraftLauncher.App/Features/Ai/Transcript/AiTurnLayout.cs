// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Virtualizes whole turns using retained per-key heights and stable unseen slots, rather than rescaling history from a running average.</summary>
internal sealed class AiTurnLayout(float spacing) : VirtualizingLayout
{
    private readonly Dictionary<int, float> _heights = [];
    private const float UnmeasuredHeight = 160f;

    private float Height(VirtualizingLayoutContext context, int index) => _heights.GetValueOrDefault(context.KeyAt(index), UnmeasuredHeight);

    private float Prefix(VirtualizingLayoutContext context, int end)
    {
        var height = 0f;
        for (var index = 0; index < end; index++)
            height += Height(context, index) + spacing;
        return height;
    }

    /// <inheritdoc/>
    public override Size Measure(VirtualizingLayoutContext context, BoxConstraints constraints) => new(float.IsFinite(constraints.Max.Width) ? constraints.Max.Width : 0f, MathF.Max(0f, Prefix(context, context.ItemCount) - spacing));

    /// <inheritdoc/>
    public override void Arrange(VirtualizingLayoutContext context, Rect finalBounds)
    {
        var start = context.RealizeAll ? 0f : context.RealizationRect.Top;
        var end = context.RealizeAll ? float.PositiveInfinity : context.RealizationRect.Bottom;
        var first = 0;
        var top = 0f;
        while (first < context.ItemCount && top + Height(context, first) + spacing <= start)
        {
            top += Height(context, first) + spacing;
            first++;
        }

        var changed = false;
        for (var index = first; index < context.ItemCount && top <= end; index++)
        {
            var element = context.GetOrCreateElementAt(index);
            var desired = element.Measure(new BoxConstraints(Size.Empty, new Size(finalBounds.Width, float.PositiveInfinity)), context.LayoutContext);
            var height = MathF.Max(1f, desired.Height);
            var key = context.KeyAt(index);
            if (!_heights.TryGetValue(key, out var before) || MathF.Abs(before - height) > .1f)
            {
                _heights[key] = height;
                changed = true;
            }

            element.Arrange(Rect.FromXywh(finalBounds.Left, finalBounds.Top + top, finalBounds.Width, height));
            top += height + spacing;
        }
        // Slots before this window keep their cached heights. Newly measured content changes only the suffix,
        // never the visible turn's origin or the unrelated unseen slots.
        if (changed)
            InvalidateMeasure();
    }

    /// <inheritdoc/>
    public override bool ReconcileFrom(VirtualizingLayout source) => source is AiTurnLayout next && next.Spacing == spacing;
    private float Spacing => spacing;
}