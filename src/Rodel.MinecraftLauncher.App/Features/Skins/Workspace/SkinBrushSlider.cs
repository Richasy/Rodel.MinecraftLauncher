// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reactive;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Retains the native slider's keyboard, pointer and range semantics under square launcher chrome.</summary>
internal sealed partial class SkinBrushSlider(SkinEditorState editor) : Control
{
    private readonly SkinBrushSliderFace _face = new(() => (float)(editor.BrushSize - 1) / 4);
    private static SliderPalette Invisible => new(
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        Colors.Transparent,
        Colors.Transparent);

    private Ui Body => Overlay([
        _face,
        Slider(editor.BrushSize, value => editor.BrushSize = Math.Round(value))
            .Minimum(1).Maximum(5).SmallChange(1).LargeChange(1)
            .Palette(Invisible)
            .Automation(new()
            {
                Name = Resources.SkinToolSize,
                AutomationId = "skin.tool.size"
            }),
    ])
        .Height(28f);
}

internal sealed class SkinBrushSliderFace(Func<float> ratio) : Control
{
    private readonly SkinBrushSliderWidget _widget = new();

    protected override void OnMount() => SetProjection(_widget, null, null);

    protected override IDisposable? OnMounted() => new Effect(() => _widget.Configure(
            ratio(),
            LauncherTheme.Edge,
            LauncherTheme.PrimaryBackground,
            LauncherTheme.SelectionBackground,
            LauncherTheme.RaisedLight,
            LauncherTheme.RaisedDark));
}

/// <summary>Paints square track and beveled thumb using the native slider's exact ten-DIP endpoint inset.</summary>
internal sealed class SkinBrushSliderWidget : Widget
{
    private float _ratio;
    private Color _edge, _accent, _face, _light, _shadow;

    internal void Configure(
        float ratio,
        Color edge,
        Color accent,
        Color face,
        Color light,
        Color shadow)
    {
        _ratio = ratio;
        _edge = edge;
        _accent = accent;
        _face = face;
        _light = light;
        _shadow = shadow;
        Invalidate(WidgetDirty.Paint);
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => new(120f, 28f);

    protected override void PaintPresentable(ISceneRenderer renderer)
    {
        var x = Bounds.Left + 10f;
        var y = Bounds.Top + (Bounds.Height / 2f);
        var travel = Math.Max(0f, Bounds.Width - 20f);
        renderer.FillRectangle(Rect.FromXywh(x, y - 2f, travel, 4f), _edge);
        renderer.FillRectangle(Rect.FromXywh(x, y - 2f, travel * _ratio, 4f), _accent);
        var thumb = Rect.FromXywh(x + (travel * _ratio) - 7f, y - 10f, 14f, 20f);
        renderer.FillRectangle(thumb, _edge);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 1f, thumb.Top + 1f, 12f, 18f), _face);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 1f, thumb.Top + 1f, 12f, 1f), _light);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 1f, thumb.Bottom - 2f, 12f, 1f), _shadow);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 6f, thumb.Top + 6f, 2f, 8f), _accent);
    }
}