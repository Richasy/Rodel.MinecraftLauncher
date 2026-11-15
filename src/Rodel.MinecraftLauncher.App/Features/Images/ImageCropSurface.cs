// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>Provides pointer capture and keyboard movement for the same square crop.</summary>
internal sealed partial class ImageCropSurface(Action<double, double> move) : Control
{
    private Point _previous;
    private bool _dragging;
    private uint _pointer;
    private partial ImageSource? Source { get; set; }

    internal ImageCropSurface WithImage(ImageSource? source)
    {
        Source = source;
        return this;
    }

    /// <summary>Displays a focusable crop with captured pointer gestures and the current cropped pixels.</summary>
    public Ui Body => Row([
        PointerArea(Image(Source ?? Instances.InstanceIconView.Transparent)
                .Stretch(ImageStretch.Fill), HandlePointer, () => _dragging = false)
            .OnKey(OnKey)
            .Width(236f).Height(236f)
            .Automation(new()
            {
                Name = Resources.ImageCropLabel,
                AutomationId = "image.crop"
            })
    ]);

    private PointerEventResult HandlePointer(in PointerEvent e)
    {
        if (e.Kind == PointerEventKind.Cancel)
        {
            _dragging = false;
            return new(true, PointerCaptureRequest.Release);
        }

        if (e.Kind == PointerEventKind.Down && e.Button == PointerButton.Primary)
        {
            _dragging = true;
            _pointer = e.PointerId;
            _previous = e.Position;
            return new(true, PointerCaptureRequest.Capture);
        }

        if (!_dragging || e.PointerId != _pointer)
            return PointerEventResult.Ignored;
        if (e.Kind == PointerEventKind.Move)
        {
            move(-(e.Position.X - _previous.X) / Math.Max(1, Render.Bounds.Width), -(e.Position.Y - _previous.Y) / Math.Max(1, Render.Bounds.Height));
            _previous = e.Position;
        }

        if (e.Kind == PointerEventKind.Up)
        {
            _dragging = false;
            return new(true, PointerCaptureRequest.Release);
        }

        return new(true, PointerCaptureRequest.None);
    }

    private KeyEventResult OnKey(in KeyEvent key)
    {
        if (key.Kind != KeyEventKind.Down || key.IsImeOwned || key.IsSystemKey || key.Modifiers != KeyModifiers.None)
            return KeyEventResult.Ignored;
        var x = key.Key == Key.ArrowLeft
            ? -.04
            : key.Key == Key.ArrowRight ? .04 : 0;
        var y = key.Key == Key.ArrowUp
            ? -.04
            : key.Key == Key.ArrowDown ? .04 : 0;
        if (x != 0 || y != 0)
        {
            move(x, y);
            return new(Handled: true);
        }

        return KeyEventResult.Ignored;
    }
}