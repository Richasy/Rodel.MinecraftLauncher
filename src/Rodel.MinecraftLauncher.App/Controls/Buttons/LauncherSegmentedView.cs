// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

public sealed record LauncherSegmentedMaterial(
    Brush Track,
    Brush Selection,
    Brush Edge,
    Brush Face,
    Brush RaisedLight,
    Brush RaisedDark,
    Brush InsetDark,
    Brush InsetLight,
    Color Shadow,
    bool HighContrast);

[InteractiveDecorator]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Sprout calls decorator projection hooks on the view instance.")]
public readonly partial record struct LauncherSegmentedView(LauncherSegmentedMaterial Material)
{
    public Widget? Content { get; init; }

    public LauncherSegmentedWidget BuildChrome(Widget content) => new(content, Material);

    public InteractionNode BuildInteractionNode(ElementId id, Rect bounds, Widget? render) => new LauncherSegmentedInteraction(id, bounds, render as LauncherSegmentedWidget);

    public void SyncInteraction(InteractionNode node)
    {
        if (node is LauncherSegmentedInteraction interaction)
            interaction.Update(Material);
    }
}

internal sealed class LauncherSegmentedInteraction(ElementId id, Rect bounds, LauncherSegmentedWidget? chrome) : InteractionNode(id, bounds, HitTestBehavior.DeferToChild)
{
    internal void Update(LauncherSegmentedMaterial material) => chrome?.Update(material);
}

public sealed class LauncherSegmentedWidget : Decorator
{
    private LauncherSegmentedPresenter _presenter;
    private LauncherSegmentedMaterial _material;

    public LauncherSegmentedWidget(Widget content, LauncherSegmentedMaterial material)
    {
        SetChild(content);
        _material = material;
        _presenter = new(material);
    }

    /// <summary>Replaces the presentation scope when its material changes, retaining the selected control and input state.</summary>
    public void Update(LauncherSegmentedMaterial material)
    {
        if (_material == material)
            return;
        _material = material;
        _presenter = new(material);
        Invalidate(WidgetDirty.Layout | WidgetDirty.Paint);
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context)
    {
        var scoped = new LayoutContext(
            context.TextMeasurer,
            context.Scale,
            context.Images,
            context.Animator,
            context.IsRightToLeft,
            context.Language,
            context.TextScaleFactor,
            context.IsTextScaleFactorEnabled,
            context.DefaultFontFamily,
            context.Grid,
            _presenter)
        {
            Now = context.Now,
            ReduceMotion = context.ReduceMotion,
            KeepAnimating = context.KeepAnimating,
            RequestRelayout = context.RequestRelayout,
            RequestFullFrame = context.RequestFullFrame,
        };
        return Child?.Measure(constraints, scoped) ?? default;
    }
}

internal sealed class LauncherSegmentedPresenter(LauncherSegmentedMaterial material) : IControlPresenter
{
    public bool CanPresent(Widget widget) => widget is ISelectionChrome;

    /// <summary>Records the actual material primitives so damage planning can inspect their sampling dependencies.</summary>
    public bool TryRecord(Widget widget, ISceneRenderer renderer) => TryPaint(widget, renderer);

    public bool TryPaint(Widget widget, ISceneRenderer renderer)
    {
        if (widget is not ISelectionChrome selection)
            return false;
        var bounds = widget.Bounds;
        renderer.FillRectangle(bounds, material.Track);
        renderer.StrokeRoundedRectangle(
            bounds,
            CornerRadius.Zero,
            material.Edge,
            1f,
            StrokeStyle.Solid);
        if (!material.HighContrast)
            Frame(renderer, bounds.Inflate(-1f), material.InsetDark, material.InsetLight);
        if (selection.SelectedRegion is { } selected)
        {
            if (!material.HighContrast)
            {
                renderer.DrawShadow(
                    selected,
                    CornerRadius.Zero,
                    material.Shadow,
                    2f,
                    new(0f, 1f));
            }

            renderer.FillRectangle(selected, material.Selection);
            if (!material.HighContrast)
            {
                renderer.FillRectangle(selected, material.Face);
                Frame(renderer, selected.Inflate(-1f), material.RaisedLight, material.RaisedDark);
            }

            renderer.StrokeRoundedRectangle(
                selected,
                CornerRadius.Zero,
                material.Edge,
                1f,
                StrokeStyle.Solid);
        }

        return true;
    }

    private static void Frame(
        ISceneRenderer renderer,
        Rect bounds,
        Brush top,
        Brush bottom)
    {
        renderer.FillRectangle(Rect.FromXywh(bounds.Left, bounds.Top, bounds.Width, 1f), top);
        renderer.FillRectangle(Rect.FromXywh(bounds.Left, bounds.Top, 1f, bounds.Height), top);
        renderer.FillRectangle(Rect.FromXywh(bounds.Left, bounds.Bottom - 1f, bounds.Width, 1f), bottom);
        renderer.FillRectangle(Rect.FromXywh(bounds.Right - 1f, bounds.Top, 1f, bounds.Height), bottom);
    }
}