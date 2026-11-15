// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Widgets;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Controls.Graphics;

public sealed class LauncherLineIcon(
    PathData data,
    float size,
    bool filled,
    Brush? foreground = null) : Control
{
    private readonly PathData _data = data;
    private readonly float _size = size;
    private readonly bool _filled = filled;
    private readonly Brush? _foreground = foreground;

    protected override void OnMount()
    {
        SetProjection(new LineIconWidget(_data, _size, _filled, _foreground), null, null);
    }

    public override void ReconcileFrom(Control source)
    {
        if (source is LauncherLineIcon icon && Render is LineIconWidget widget)
        {
            widget.Configure(icon._data, icon._size, icon._filled, icon._foreground);
        }
    }
}

public sealed class LineIconWidget(
    PathData data,
    float size,
    bool filled,
    Brush? foreground = null) : Widget, IInheritsForeground
{
    private PathData _data = data;
    private float _size = size;
    private bool _filled = filled;
    private Brush? _foreground = foreground;
    private Brush? _inherited;
    public bool InheritsForeground => true;

    public void Configure(
        PathData data,
        float size,
        bool filled,
        Brush? foreground = null)
    {
        if ((_data == data) && (_size == size) && (_filled == filled) && (_foreground == foreground))
        {
            return;
        }

        _data = data;
        _size = size;
        _filled = filled;
        _foreground = foreground;
        Invalidate(WidgetDirty.Layout | WidgetDirty.Paint);
    }

    public void SetInheritedForeground(Brush? foreground)
    {
        _inherited = foreground;
        Invalidate(WidgetDirty.Paint);
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => new(_size, _size);

    protected override void PaintPresentable(ISceneRenderer renderer)
    {
        renderer.PushTransform(Matrix3x2.CreateScale(_size / 24f) * Matrix3x2.CreateTranslation(Bounds.Left, Bounds.Top));
        var ink = _foreground ?? _inherited ?? Brush.Theme(ThemeColorToken.TextFillColorPrimary);
        if (_filled)
        {
            renderer.FillPath(_data, ink);
        }
        else
        {
            renderer.StrokePath(_data, ink, 1.7f, StrokeStyle.Solid with
            {
                StartCap = PenLineCap.Round,
                EndCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            });
        }

        renderer.PopTransform();
    }
}