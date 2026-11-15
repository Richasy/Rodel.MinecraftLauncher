// Copyright (c) Richasy. All rights reserved.

using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Home;

/// <summary>Reveals a selected home row using its current measured bounds, including after text scaling or recycling.</summary>
internal sealed class HomeListSelectionReveal(ItemsRepeater repeater, ScrollView scroll) : IDisposable
{
    private readonly Dictionary<int, Widget> _realized = [];
    private bool _attached;

    internal void Attach()
    {
        repeater.Panel.ElementRealized += Realized;
        repeater.Panel.ElementRecycled += Recycled;
        _attached = true;
    }

    internal bool TryReveal(int index, int key)
    {
        if (!repeater.Panel.TryRealizeItemAt(index)
            || !_realized.TryGetValue(key, out var row)
            || row.Bounds.IsEmpty)
        {
            return false;
        }

        var bounds = row.Bounds;
        scroll.ScrollIntoView(Rect.FromXywh(
            bounds.Left - repeater.Render.Bounds.Left,
            bounds.Top - repeater.Render.Bounds.Top,
            bounds.Width,
            bounds.Height));
        return true;
    }

    private void Realized(int key, Widget row) => _realized[key] = row;

    private void Recycled(int key, Widget row)
    {
        if (_realized.TryGetValue(key, out var current) && ReferenceEquals(current, row))
            _realized.Remove(key);
    }

    /// <summary>Releases only this repeater's geometry observations; the repeater still owns and disposes its rows.</summary>
    public void Dispose()
    {
        if (_attached)
        {
            repeater.Panel.ElementRealized -= Realized;
            repeater.Panel.ElementRecycled -= Recycled;
            _attached = false;
        }

        _realized.Clear();
    }
}