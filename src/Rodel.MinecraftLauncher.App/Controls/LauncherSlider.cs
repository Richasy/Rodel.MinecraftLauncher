// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reactive;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls;

/// <summary>Retains the native slider's keyboard, pointer and range semantics under square launcher chrome.</summary>
internal sealed partial class LauncherSlider : Control
{
    private readonly LauncherSliderFace _face;

    internal LauncherSlider(
        double value,
        Action<double> changed,
        double minimum,
        double maximum,
        double smallChange,
        double largeChange,
        string name,
        string automationId,
        bool enabled = true)
    {
        Configuration = new(
            value,
            changed,
            minimum,
            maximum,
            smallChange,
            largeChange,
            name,
            automationId,
            enabled);
        _face = new(() => Configuration.Maximum <= Configuration.Minimum ? 0f : (float)((Configuration.Value - Configuration.Minimum) / (Configuration.Maximum - Configuration.Minimum)));
    }

    private partial LauncherSliderSettings Configuration { get; set; }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is LauncherSlider next)
            Configuration = next.Configuration;
    }

    private static SliderPalette Invisible => new(
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        new(Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent),
        Colors.Transparent,
        Colors.Transparent);

    private Ui Body => Overlay([
        _face,
        Slider(Configuration.Value, value => Configuration.Changed(value))
            .Minimum(Configuration.Minimum).Maximum(Configuration.Maximum).SmallChange(Configuration.SmallChange).LargeChange(Configuration.LargeChange)
            .Palette(Invisible)
            .Enabled(Configuration.Enabled)
            .Automation(new()
            {
                Name = Configuration.Name,
                AutomationId = Configuration.AutomationId
            }),
    ])
        .MinHeight(28f);
}

/// <summary>Retained native range configuration and localized automation identity.</summary>
/// <param name = "Value">The current selected value.</param>
/// <param name = "Changed">Receives native pointer, keyboard and automation changes.</param>
/// <param name = "Minimum">The inclusive range start.</param>
/// <param name = "Maximum">The inclusive range end.</param>
/// <param name = "SmallChange">The native small-step increment.</param>
/// <param name = "LargeChange">The native page-step increment.</param>
/// <param name = "Name">The localized accessible label.</param>
/// <param name = "AutomationId">The stable language-independent identifier.</param>
/// <param name = "Enabled">Whether native input is available.</param>
internal readonly record struct LauncherSliderSettings(
    double Value,
    Action<double> Changed,
    double Minimum,
    double Maximum,
    double SmallChange,
    double LargeChange,
    string Name,
    string AutomationId,
    bool Enabled);

internal sealed class LauncherSliderFace(Func<float> ratio) : Control
{
    private readonly LauncherSliderWidget _widget = new();

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
internal sealed class LauncherSliderWidget : Widget
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
        var thumbX = x + (travel * (IsRightToLeft ? 1f - _ratio : _ratio));
        renderer.FillRectangle(Rect.FromXywh(IsRightToLeft ? thumbX : x, y - 2f, travel * _ratio, 4f), _accent);
        var thumb = Rect.FromXywh(thumbX - 7f, y - 10f, 14f, 20f);
        renderer.FillRectangle(thumb, _edge);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 1f, thumb.Top + 1f, 12f, 18f), _face);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 1f, thumb.Top + 1f, 12f, 1f), _light);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 1f, thumb.Bottom - 2f, 12f, 1f), _shadow);
        renderer.FillRectangle(Rect.FromXywh(thumb.Left + 6f, thumb.Top + 6f, 2f, 8f), _accent);
    }
}