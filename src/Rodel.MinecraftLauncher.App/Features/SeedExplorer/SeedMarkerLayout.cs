// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Places the bounded visible marker set at map coordinates while ItemsRepeater owns native input, focus and recycling.</summary>
internal sealed class SeedMarkerLayout(SeedMarker[] markers, float width, float height, float? size = null) : VirtualizingLayout
{
    private SeedMarker[] _markers = markers;
    private float _width = width, _height = height;
    private float? _size = size;
    /// <inheritdoc/>
    public override Size Measure(VirtualizingLayoutContext context, BoxConstraints constraints) => new(_width, _height);
    /// <inheritdoc/>
    public override void Arrange(VirtualizingLayoutContext context, Rect finalBounds)
    {
        for (var index = 0; index < context.ItemCount; index++)
        {
            if (index >= _markers.Length) { context.RecycleElement(index); continue; }

            var marker = _markers[index];
            var size = _size ?? marker.Size;
            var element = context.GetOrCreateElementAt(index);
            element.Measure(new(new(size, size), new(size, size)), context.LayoutContext);
            var rect = Rect.FromXywh(marker.X + marker.OffsetX - (size / 2), marker.Y + marker.OffsetY - (size / 2), size, size);
            element.Arrange(Rect.FromXywh(finalBounds.Left + rect.Left, finalBounds.Top + rect.Top, rect.Width, rect.Height));
        }
    }
    /// <inheritdoc/>
    public override bool ReconcileFrom(VirtualizingLayout source)
    {
        if (source is not SeedMarkerLayout other) return false;
        var changed = _width != other._width || _height != other._height || _size != other._size
            || !_markers.Select(marker => (marker.Id, marker.X, marker.Y, marker.Size, marker.OffsetX, marker.OffsetY))
                .SequenceEqual(other._markers.Select(marker => (marker.Id, marker.X, marker.Y, marker.Size, marker.OffsetX, marker.OffsetY)));
        _markers = other._markers; _width = other._width; _height = other._height; _size = other._size;
        if (changed) InvalidateMeasure();
        return true;
    }
}