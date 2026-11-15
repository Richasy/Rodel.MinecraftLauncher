// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed partial class WallpaperNameInput : Control
{
    private readonly Window _window;
    private TextBoxWidget? _widget;
    private TextFormat? _format;
    private float _height;
    private float _lineHeight;
    private Thickness _padding;
    private readonly Action<string> _change;
    private readonly Action<string> _submit;

    internal WallpaperNameInput(Window window, Action<string> change, Action<string> submit)
    {
        _window = window;
        _change = change;
        _submit = submit;
        Value = string.Empty;
    }

    public partial string Value { get; set; }
    public partial TextBoxStyle Appearance { get; set; }

    private Ui Body => Overlay([
        TextBox(Value, value =>
            {
                Value = value;
                _change(value);
            })
            .Style(Appearance)
            .SubmitOnEnter()
            .OnSubmitted(value =>
            {
                Value = value;
                _submit(value);
            })
            .Automation(new()
            {
                Name = Resources.WallpaperName,
                AutomationId = "wallpaper.rename.input"
            }),
    ]);

    private static TextBoxWidget? FindInput(Widget? widget)
    {
        if (widget is TextBoxWidget input)
            return input;
        TextBoxWidget? found = null;
        widget?.VisitChildren(child => found ??= FindInput(child));
        return found;
    }

    private void CenterLine()
    {
        _widget ??= FindInput(ReconcileRender);
        if (_widget is not { } widget || widget.Bounds.IsEmpty)
            return;
        var content = widget.Content;
        if (_format != content.Style || _height != widget.Bounds.Height)
        {
            _format = content.Style;
            _height = widget.Bounds.Height;
            var sample = new TextContent("Ag", default, content.Style, content.Foreground);
            var lines = _window.TextHitTester.HitTestRange(sample, Rect.FromXywh(0f, 0f, 1000f, 1000f), 0, 2);
            _lineHeight = lines.Count > 0 ? lines[0].Height : content.Style.FontSize * 1.4f;
            var inset = Math.Max(0f, (_height - 2f - _lineHeight) * .5f);
            _padding = new Thickness(11f, inset, 11f, inset);
        }

        widget.SetContentPadding(_padding);
    }

    protected override IDisposable? OnMounted()
    {
        _window.LayoutUpdated += CenterLine;
        return null;
    }

    protected override void OnDispose()
    {
        _window.LayoutUpdated -= CenterLine;
        base.OnDispose();
    }
}