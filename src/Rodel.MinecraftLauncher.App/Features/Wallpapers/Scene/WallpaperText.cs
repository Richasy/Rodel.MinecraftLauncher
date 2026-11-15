// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

/// <summary>Retains normal text measurement and semantics; only contour paint is reactive.</summary>
internal sealed partial class WallpaperText : Control
{
    private readonly WallpaperReadability _owner;
    public partial string Value { get; set; }
    public partial float FontSize { get; set; }
    public partial Color Ink { get; set; }
    public partial bool Heading { get; set; }

    internal WallpaperText(
        string text,
        float size,
        Color ink,
        WallpaperReadability owner,
        bool heading = false)
    {
        Value = text;
        FontSize = size;
        Ink = ink;
        _owner = owner;
        Heading = heading;
    }

    public partial TextOutlinePaint Paint { get; set; }

    private Ui Body => Sprout.Markup.Overlay([
        Sprout.Markup.Text(Value)
            .FontSize(FontSize).Foreground(Ink).FontWeight(Heading ? 700 : 500)
            .LetterSpacing(Heading ? 3f : 0f)
            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
            .Outline(Paint)
    ]);
    internal TextWidget? Widget => FindText(ReconcileRender);

    private static TextWidget? FindText(Widget? widget)
    {
        if (widget is TextWidget text)
            return text;
        TextWidget? found = null;
        widget?.VisitChildren(child => found ??= FindText(child));
        return found;
    }

    public override void ReconcileFrom(Control source)
    {
        var next = (WallpaperText)source;
        Value = next.Value;
        FontSize = next.FontSize;
        Ink = next.Ink;
        Heading = next.Heading;
    }

    protected override IDisposable? OnMounted()
    {
        _owner.Register(this);
        return null;
    }

    protected override void OnDispose()
    {
        _owner.Unregister(this);
        base.OnDispose();
    }
}