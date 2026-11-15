// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Semantics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>A square visual row inside MenuFlyout; the menu still owns input, focus and dismissal.</summary>
internal sealed class AccountMenuRow : Panel
{
    private readonly Widget _glyph;
    private readonly Widget _label;
    private readonly Widget _type;
    private readonly Widget? _status;
    private readonly InteractionNode _typeHit;
    private readonly bool _selected;
    private Brush? _hover;
    private LauncherAppearance _appearance;

    internal AccountMenuRow(
        Widget glyph,
        Widget type,
        Widget label,
        Widget? status,
        bool selected,
        InteractionNode typeHit)
    {
        _glyph = glyph;
        _type = type;
        _label = label;
        _status = status;
        _selected = selected;
        _typeHit = typeHit;
        AttachChild(0, glyph);
        AttachChild(1, label);
        AttachChild(2, type);
        if (status is not null)
            AttachChild(3, status);
    }

    internal void SetHighlight(Brush? hover)
    {
        _hover = hover;
        ApplyForegrounds();
        Invalidate(WidgetDirty.Paint);
    }

    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context)
    {
        var loose = BoxConstraints.Loose(constraints.Max);
        _glyph.Measure(BoxConstraints.Tight(new(28f, 28f)), context);
        _type.Measure(loose, context);
        _label.Measure(loose, context);
        _status?.Measure(loose, context);
        return new(92f + _label.DesiredSize.Width, Math.Max(48f, _label.DesiredSize.Height + (_status?.DesiredSize.Height ?? -4f) + 20f));
    }

    protected override void ArrangeOverride(Rect bounds)
    {
        var textWidth = Math.Max(0f, bounds.Width - 92f);
        _glyph.Arrange(Rect.FromXywh(IsRightToLeft ? bounds.Right - 40f : bounds.Left + 12f, bounds.Top + ((bounds.Height - 28f) / 2f), 28f, 28f));
        var textLeft = IsRightToLeft ? bounds.Left + 40f : bounds.Left + 52f;
        var height = _label.DesiredSize.Height + (_status is null ? 0 : _status.DesiredSize.Height + 4f);
        var top = bounds.Top + ((bounds.Height - height) / 2f);
        _label.Arrange(Rect.FromXywh(textLeft, top, textWidth, _label.DesiredSize.Height));
        _status?.Arrange(Rect.FromXywh(textLeft, top + _label.DesiredSize.Height + 4f, textWidth, _status.DesiredSize.Height));
        var typeBounds = Rect.FromXywh(IsRightToLeft ? bounds.Left + 12f : bounds.Right - 28f, bounds.Top + ((bounds.Height - 16f) / 2f), 16f, 16f);
        _type.Arrange(typeBounds);
        _typeHit.HitRegion = typeBounds;
    }

    protected override void PaintPresentable(ISceneRenderer renderer)
    {
        var box = Bounds.Deflate(new Thickness(4f, 2f, 4f, 2f));
        if (_appearance.HighContrast)
        {
            var states = new LauncherInteractionPalette(_appearance).Create(false, _selected);
            renderer.FillRectangle(box, (_hover is null ? states.Normal : states.PointerOver).Background);
        }
        else if (_selected)
        {
            renderer.FillRectangle(box, LauncherTheme.HighContrast ? LauncherTheme.SelectionBackground : LauncherTheme.ControlBackground);
            if (!LauncherTheme.HighContrast)
            {
                renderer.FillRectangle(box, LauncherMaterials.SelectionInsetGradient);
                renderer.FillRectangle(Rect.FromXywh(box.Left, box.Top, box.Width, 1f), LauncherTheme.InsetDark);
                renderer.FillRectangle(Rect.FromXywh(box.Left, box.Bottom - 1f, box.Width, 1f), LauncherTheme.InsetLight);
            }
        }
        else if (_hover is { } hover)
        {
            renderer.FillRectangle(box, hover);
        }
    }

    internal void ApplyAppearance(LauncherAppearance appearance)
    {
        _appearance = appearance;
        ApplyForegrounds();
        if (_glyph is AccountMenuAvatar avatar) avatar.ApplyAppearance(appearance);
        Invalidate(WidgetDirty.Paint);
    }

    private void ApplyForegrounds()
    {
        var appearance = _appearance;
        var states = new LauncherInteractionPalette(appearance).Create(false, _selected);
        Brush ink = appearance.HighContrast
            ? (_hover is null ? states.Normal : states.PointerOver).Foreground
            : appearance.Ink;
        if (_label is TextWidget label) label.SetForeground(ink);
        if (_type is LineIconWidget line) line.SetInheritedForeground(ink);
        if (_status is TextWidget status) status.SetForeground(appearance.HighContrast ? ink : appearance.Error);
    }
}

internal sealed class MicrosoftMenuGlyph : Widget
{
    protected override Size MeasureOverride(BoxConstraints constraints, LayoutContext context) => new(16f, 16f);

    protected override void PaintPresentable(ISceneRenderer renderer)
    {
        renderer.FillRectangle(Rect.FromXywh(Bounds.Left, Bounds.Top, 7f, 7f), new Color(242, 80, 34, 255));
        renderer.FillRectangle(Rect.FromXywh(Bounds.Left + 9f, Bounds.Top, 7f, 7f), new Color(127, 186, 0, 255));
        renderer.FillRectangle(Rect.FromXywh(Bounds.Left, Bounds.Top + 9f, 7f, 7f), new Color(0, 164, 239, 255));
        renderer.FillRectangle(Rect.FromXywh(Bounds.Left + 9f, Bounds.Top + 9f, 7f, 7f), new Color(255, 185, 0, 255));
    }
}