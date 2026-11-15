// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Theming;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

internal sealed record SetBevelConfiguration(ButtonView View, bool Primary, bool Selected) : WidgetMutation;

/// <summary>Paints the same square surface in raised and inset states without moving layout or hit bounds.</summary>
public sealed class BevelWidget : Decorator
{
    private ButtonView _view;
    private bool _pressed;
    private ContentForegroundCascade _foreground;
    /// <summary>Whether the surface uses the primary action's thicker rim.</summary>
    public bool Primary { get; private set; }
    /// <summary>Whether the persistent selected state keeps the surface recessed.</summary>
    public bool Selected { get; private set; }
    private bool HasFrame => (_view.BorderThickness ?? new Thickness(1f)) != Thickness.Zero;
    /// <summary>Whether the current style paints a raised or recessed material.</summary>
    public bool HasDepth => HasFrame || (_view.Palette.Normal.Background.Literal is { A: 0 } && _view.Palette.Normal.Foreground.Literal == LauncherTheme.Ink && (Background.AsSolid ?? Background.AsMix?.Resolve(Theme.Resolve().Colors)) is { A: > 0 });
    /// <summary>Whether the enabled resting surface includes a decorative corner highlight.</summary>
    public bool ShowsCornerHighlight => !LauncherTheme.HighContrast && HasDepth && _view.IsEnabled && !Selected && !_pressed;
    /// <summary>The current interaction background, including any active color transition.</summary>
    public Brush Background { get; private set; }
    /// <summary>The current interaction border color.</summary>
    public DynamicColor BorderFace { get; private set; }
    private Thickness Chrome
    {
        get
        {
            var padding = _view.Padding ?? new Thickness(12f, 8f, 12f, 8f);
            var inset = Primary
                ? 2f
                : HasFrame ? 1f : 0f;
            return new Thickness(padding.Left + inset, padding.Top + inset, padding.Right + inset, padding.Bottom + inset);
        }
    }

    /// <summary>Wraps native button content in the shared material and foreground scope.</summary>
    public BevelWidget(
        Widget content,
        ButtonView view,
        bool primary = false,
        bool selected = false)
    {
        _view = view;
        Primary = primary;
        Selected = selected;
        SetChild(content);
        IsForegroundScopeBoundary = true;
        _foreground = new ContentForegroundCascade(content);
        Configure(view);
    }

    /// <summary>Updates authored styling and content alignment without replacing native content.</summary>
    public void Configure(ButtonView view)
    {
        _view = view;
        if (Child is { } child)
        {
            child.SetLayoutProps(child.IntrinsicLayoutProps with
            {
                HorizontalAlignment = view.HorizontalContentAlignment ?? HorizontalAlignment.Center,
                VerticalAlignment = view.VerticalContentAlignment ?? VerticalAlignment.Center,
            });
        }

        var rest = view.IsEnabled ? view.Palette.Normal : view.Palette.Disabled;
        SetBackground(rest.Background);
        SetForeground(rest.Foreground);
        SetBorder(rest.Border, rest.Elevation);
        Invalidate(WidgetDirty.Layout | WidgetDirty.Paint);
    }

    /// <summary>Updates the interaction background and its potentially changing material depth.</summary>
    public void SetBackground(Brush background)
    {
        var hadDepth = HasDepth;
        Background = background;
        Invalidate(hadDepth == HasDepth ? WidgetDirty.Paint : WidgetDirty.Layout | WidgetDirty.Paint);
    }

    /// <summary>Applies the foreground to hosted content independently of interaction geometry.</summary>
    public void SetForeground(DynamicColor foreground)
    {
        _foreground.Apply(foreground);
        Invalidate(WidgetDirty.Paint);
    }

    /// <summary>Tracks native pointer and keyboard press state, relocating only the internal content face.</summary>
    public void SetInteractionState(AnimatedIconInteractionState state)
    {
        var pressed = _view.IsEnabled && state == AnimatedIconInteractionState.Pressed;
        if (_pressed == pressed)
            return;
        _pressed = pressed;
        Invalidate(WidgetDirty.Layout | WidgetDirty.Paint);
    }

    /// <summary>Updates the border color while material geometry remains owned by this decorator.</summary>
    public void SetBorder(DynamicColor border, BorderElevation? _)
    {
        BorderFace = border;
        Invalidate(WidgetDirty.Paint);
    }

    /// <inheritdoc/>
    public override void SetChild(Widget? child)
    {
        base.SetChild(child);
        _foreground = new ContentForegroundCascade(child);
    }

    /// <inheritdoc/>
    public override void ApplyReconcilePatch(WidgetPatch patch)
    {
        foreach (var mutation in patch.Mutations)
        {
            if (mutation is SetBevelConfiguration config)
            {
                Primary = config.Primary;
                Selected = config.Selected;
                Configure(config.View);
            }
            else
            {
                base.ApplyReconcilePatch(new([mutation]));
            }
        }
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context)
    {
        var chrome = Chrome;
        var size = Child?.Measure(BoxConstraints.Loose(constraints.Deflate(chrome).Max), context) ?? default;
        return new Size(size.Width + chrome.Horizontal, Math.Max(32f, size.Height + chrome.Vertical));
    }

    protected override void ArrangeOverride(Rect finalBounds) => Child?.Arrange(finalBounds.Deflate(BevelGeometry.CenterContent(
                Chrome,
                HasDepth,
                _pressed || Selected,
                Primary,
                _view.IsEnabled)));

    protected override void PaintPresentable(ISceneRenderer renderer) => PaintSurface(
        renderer,
        Bounds,
        Background,
        BorderFace,
        HasDepth,
        HasFrame,
        _pressed || Selected,
        Primary,
        _view.IsEnabled,
        Selected);

    internal static void PaintSurface(
        ISceneRenderer renderer,
        Rect box,
        Brush background,
        DynamicColor border,
        bool hasDepth,
        bool hasFrame,
        bool inset,
        bool primary,
        bool enabled,
        bool selected = false)
    {
        if (LauncherTheme.HighContrast)
        {
            renderer.FillRectangle(box, background);
            if (hasFrame || (background.AsSolid ?? background.AsMix?.Resolve(Theme.Resolve().Colors)) is { A: > 0 })
            {
                renderer.StrokeRoundedRectangle(
                    box,
                    CornerRadius.Zero,
                    border,
                    1f,
                    StrokeStyle.Solid);
            }

            return;
        }

        if (hasDepth)
        {
            renderer.DrawShadow(
                box,
                CornerRadius.Zero,
                LauncherTheme.ShadowColor.MultiplyAlpha(inset ? .36f : 1f),
                inset ? 1f : 2f,
                new(0f, inset ? 1f : 3f));
        }

        renderer.FillRectangle(box, background);
        if (!hasDepth)
            return;
        renderer.FillRectangle(box, inset
                ? selected ? LauncherMaterials.SelectionInsetGradient : LauncherMaterials.InsetGradient
                : LauncherMaterials.FaceGradient);
        renderer.StrokeRoundedRectangle(
            box,
            CornerRadius.Zero,
            border,
            1f,
            StrokeStyle.Solid);
        var face = box.Deflate(new Thickness(primary ? 2f : 1f));
        var edges = BevelGeometry.Edges(inset, primary, enabled);
        if (LauncherTheme.Light)
        {
            var top = inset
                ? LauncherTheme.InsetDark
                : primary ? BevelColors.PrimaryLightTop : LauncherTheme.RaisedLight;
            var bottom = inset
                ? LauncherTheme.InsetLight
                : primary ? BevelColors.PrimaryLightBottom : LauncherTheme.RaisedDark;
            if (!enabled)
            {
                top = LauncherTheme.RaisedLight.MultiplyAlpha(.35f);
                bottom = LauncherTheme.RaisedDark.MultiplyAlpha(.35f);
            }

            Edge(
                renderer,
                face,
                top,
                top.MultiplyAlpha(.7f),
                bottom,
                bottom.MultiplyAlpha(.7f),
                edges.Top,
                edges.Left,
                edges.Bottom,
                edges.Right);
            if (enabled && !inset)
            {
                var highlight = primary ? BevelColors.PrimaryCornerHighlight : LauncherTheme.RaisedLight;
                renderer.FillRectangle(Rect.FromXywh(box.Left + 4f, box.Top + 4f, 5f, 1f), highlight);
                renderer.FillRectangle(Rect.FromXywh(box.Left + 4f, box.Top + 4f, 1f, 5f), highlight);
            }

            return;
        }

        if (!enabled)
        {
            Edge(
                renderer,
                face,
                BevelColors.DisabledTop,
                BevelColors.DisabledLeft,
                BevelColors.DisabledBottom,
                BevelColors.DisabledRight,
                edges.Top,
                edges.Left,
                edges.Bottom,
                edges.Right);
        }
        else if (inset)
        {
            Edge(
                renderer,
                face,
                primary
                    ? BevelColors.PrimaryInsetTop
                    : selected ? LauncherTheme.SelectionInsetDark : LauncherTheme.InsetDark,
                primary
                    ? BevelColors.PrimaryInsetLeft
                    : selected ? LauncherTheme.SelectionInsetDark.MultiplyAlpha(.85f) : BevelColors.InsetLeft,
                primary
                    ? BevelColors.PrimaryInsetBottom
                    : selected ? LauncherTheme.SelectionInsetLight : LauncherTheme.InsetLight,
                primary
                    ? BevelColors.PrimaryInsetRight
                    : selected ? LauncherTheme.SelectionInsetLight.MultiplyAlpha(.65f) : BevelColors.InsetRight,
                edges.Top,
                edges.Left,
                edges.Bottom,
                edges.Right);
        }
        else
        {
            Edge(
                renderer,
                face,
                primary ? BevelColors.PrimaryRaisedTop : BevelColors.RaisedTop,
                primary ? BevelColors.PrimaryRaisedLeft : BevelColors.RaisedLeft,
                primary ? BevelColors.PrimaryRaisedBottom : LauncherTheme.RaisedDark,
                primary ? BevelColors.PrimaryRaisedRight : BevelColors.RaisedRight,
                edges.Top,
                edges.Left,
                edges.Bottom,
                edges.Right);
        }

        if (enabled && !inset)
        {
            var highlight = primary ? BevelColors.PrimaryCornerHighlight : LauncherTheme.RaisedLight;
            renderer.FillRectangle(Rect.FromXywh(box.Left + 4f, box.Top + 4f, 5f, 1f), highlight);
            renderer.FillRectangle(Rect.FromXywh(box.Left + 4f, box.Top + 4f, 1f, 5f), highlight);
        }
    }

    private static void Edge(
        ISceneRenderer renderer,
        Rect box,
        Color top,
        Color left,
        Color bottom,
        Color right,
        float topWidth,
        float leftWidth,
        float bottomWidth,
        float rightWidth)
    {
        renderer.FillRectangle(Rect.FromXywh(box.Left, box.Top, box.Width, topWidth), top);
        renderer.FillRectangle(Rect.FromXywh(box.Left, box.Top, leftWidth, box.Height), left);
        renderer.FillRectangle(Rect.FromXywh(box.Left, box.Bottom - bottomWidth, box.Width, bottomWidth), bottom);
        renderer.FillRectangle(Rect.FromXywh(box.Right - rightWidth, box.Top, rightWidth, box.Height), right);
    }
}