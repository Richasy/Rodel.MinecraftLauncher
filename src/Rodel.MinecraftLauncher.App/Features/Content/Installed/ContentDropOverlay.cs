// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentDropOverlay(
    string Title,
    string Scope,
    Color Background,
    Color Focus,
    Color Ink,
    Color Muted)
{
    public Ui Body => Overlay([
        Border()
            .Background(Background),
        new DropFrame(Focus),
        Column([
            LauncherIcons.Line("download", 36f, Focus)
                .HAlign(HorizontalAlignment.Center),
            Text(Title)
                .FontSize(20f).FontWeight(600).Foreground(Ink)
                .Wrap()
                .Align(TextAlignment.Center)
                .Automation(new()
                {
                    Name = Title,
                    AutomationId = "content.drop.hint",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
            Text(Scope)
                .FontSize(13f).Foreground(Muted)
                .Wrap()
                .Align(TextAlignment.Center),
        ])
            .Spacing(16f)
            .Padding(24f)
            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center),
    ])
        .Margin(8f);
}

internal sealed class DropFrame(Color color) : Control
{
    protected override void OnMount() => SetProjection(new DropFrameWidget(color), null, null);

    public override void ReconcileFrom(Control source)
    {
        if (source is DropFrame frame && Render is DropFrameWidget widget)
            widget.Configure(frame.Color);
    }

    private Color Color => color;
}

internal sealed class DropFrameWidget(Color color) : Widget
{
    private Color _color = color;

    internal void Configure(Color value)
    {
        if (_color == value)
            return;
        _color = value;
        Invalidate(WidgetDirty.Paint);
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => default;

    protected override void PaintPresentable(ISceneRenderer renderer)
    {
        for (var x = Bounds.Left; x < Bounds.Right; x += 14f)
        {
            var width = Math.Min(8f, Bounds.Right - x);
            renderer.FillRectangle(Rect.FromXywh(x, Bounds.Top, width, 2f), _color);
            renderer.FillRectangle(Rect.FromXywh(x, Bounds.Bottom - 2f, width, 2f), _color);
        }

        for (var y = Bounds.Top; y < Bounds.Bottom; y += 14f)
        {
            var height = Math.Min(8f, Bounds.Bottom - y);
            renderer.FillRectangle(Rect.FromXywh(Bounds.Left, y, 2f, height), _color);
            renderer.FillRectangle(Rect.FromXywh(Bounds.Right - 2f, y, 2f, height), _color);
        }
    }
}