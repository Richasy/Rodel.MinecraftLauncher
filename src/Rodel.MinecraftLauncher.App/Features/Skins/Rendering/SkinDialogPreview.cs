// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Reactive;
using Sprout.Reconcile;
using System.Diagnostics;
using System.Numerics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Renders walking directly in the popup's GPU layer; static import previews use bounded readback.</summary>
internal sealed partial class SkinDialogPreview(
    Window window,
    Func<SkinRenderSnapshot> snapshot,
    Func<bool> flat,
    Func<bool> working,
    Action<string> reportError,
    string automationId,
    bool animated = false,
    Func<SkinRenderSnapshot?>? companion = null) : Control
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private SkinGpuRenderer? _renderer;
    private SkinPreviewEffect? _effect;
    private SkinGpuFrame? _expectedFrame;
    private partial ImageSource? Bitmap { get; set; }
    private partial CustomImageEffect? Shader { get; set; }
    private bool _disposed, _scheduled, _dragging, _inFlight, _failed, _dirty;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrameTime = double.NegativeInfinity;
    private int _width, _height;
    private float _yaw = -.4f, _pitch = -.14f, _distance = 60f;
    private Point _previous;
    private uint _pointer;
    private PointerButton _button;

    private Ui Body => HoverArea(PointerArea(PreviewImage.Interpolation(animated ? ImageInterpolationMode.Linear : ImageInterpolationMode.NearestNeighbor)
                .Automation(new()
                {
                    Name = Resources.SkinModelViewport,
                    AutomationId = automationId
                }), HandlePointer, () => _dragging = false), _ =>
        {
        });
    // Read the signal even before OnMounted creates the retained inputs, so the first frame can repaint.
    private ImageView PreviewImage => Shader is { } shader && _effect is not null ? Image(_effect.Surface).Stretch(ImageStretch.Fill).CustomShader(shader) : Image(Bitmap ?? Features.Instances.InstanceIconView.Transparent).Stretch(ImageStretch.Uniform);

    protected override IDisposable? OnMounted()
    {
        _clock.Restart();
        if (snapshot().Model == SkinModel.Cape)
            _yaw = MathF.PI + .4f;
        if (animated)
        {
            _effect = new(snapshot(), companion?.Invoke());
        }
        else
        {
            _renderer = new(_ =>
                {
                }, _ =>
                {
                }, error => _dispatcher.TryEnqueue(() =>
                    {
                        _inFlight = false;
                        _failed = true;
                        if (!_disposed)
                            reportError(error);
                    }), (
                    frame,
                    bytes,
                    width,
                    height) => _dispatcher.TryEnqueue(() =>
                    {
                        _inFlight = false;
                        if (_disposed || flat())
                            return;
                        if (ReferenceEquals(frame, _expectedFrame))
                            Bitmap = ImageSource.FromPixels(new(width, height, bytes));
                        if (_dirty)
                            RequestFrame();
                    }));
        }

        window.LayoutUpdated += OnLayoutUpdated;
        window.Activated += OnWindowShown;
        window.StateChanged += OnWindowShown;
        return new Effect(() =>
            {
                _ = snapshot();
                _ = flat();
                _ = LauncherTheme.Background;
                RequestFrame();
            });
    }

    private void OnLayoutUpdated()
    {
        var size = Render.Bounds;
        var scale = Math.Min(window.Scale.Factor, (animated ? 2048f : 512f) / Math.Max(1f, Math.Max(size.Width, size.Height)));
        var width = Math.Max(0, (int)(size.Width * scale));
        var height = Math.Max(0, (int)(size.Height * scale));
        if ((width == _width) && (height == _height))
            return;
        _width = width;
        _height = height;
        RequestFrame();
    }

    private void RequestFrame()
    {
        _dirty = true;
        if (_disposed || _scheduled || _inFlight || _failed || flat() || (_width < 1) || (_height < 1) || window.State == WindowState.Minimized)
            return;
        _scheduled = true;
        window.RequestAnimationFrame(_ =>
            {
                _scheduled = false;
                if (_disposed || flat() || (_width < 1) || (_height < 1) || window.State == WindowState.Minimized)
                    return;
                var color = LauncherTheme.Background;
                var seconds = _clock.Elapsed.TotalSeconds;
                if (animated && seconds - _lastFrameTime < 1.0 / 30)
                {
                    RequestFrame();
                    return;
                }

                _lastFrameTime = seconds;
                var distance = animated ? Math.Max(72f, 72f * .92f * _height / _width) * _distance / 60f : _distance;
                var pivot = animated ? new Vector3(0, 16, 0) : (Vector3?)null;
                _expectedFrame = new(
                    snapshot(),
                    new(
                        _width,
                        _height,
                        _yaw,
                        _pitch,
                        distance,
                        Pivot: pivot),
                    127,
                    true,
                    false,
                    new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1),
                    automationId,
                    Pose: animated ? SkinAnimation.Sample(seconds) : null,
                    Antialias: animated,
                    Companion: companion?.Invoke());
                _dirty = false;
                if (_effect is not null)
                {
                    Shader = _effect.Create(_expectedFrame);
                    RequestFrame();
                }
                else
                {
                    _inFlight = true;
                    _renderer?.Submit(_expectedFrame);
                }
            });
    }

    private void OnWindowShown(object? sender, EventArgs args) => RequestFrame();

    private PointerEventResult HandlePointer(in PointerEvent packet)
    {
        if (packet.Kind == PointerEventKind.Cancel)
        {
            _dragging = false;
            return new(true, PointerCaptureRequest.Release);
        }

        if (_dragging && (packet.PointerId == _pointer) && (packet.Kind == PointerEventKind.Up) && (packet.Button == _button))
        {
            _dragging = false;
            return new(true, PointerCaptureRequest.Release);
        }

        if (flat() || working())
            return PointerEventResult.Ignored;
        if (packet.Kind == PointerEventKind.Scroll)
        {
            _distance = Math.Clamp(_distance * (packet.Scroll.Y > 0 ? 1f / 1.15f : 1.15f), 28f, 110f);
            RequestFrame();
            return new(true, PointerCaptureRequest.None);
        }

        if ((packet.Kind == PointerEventKind.Down) && packet.Button is PointerButton.Primary or PointerButton.Secondary)
        {
            if (_dragging)
                return new(true, PointerCaptureRequest.None);
            _dragging = true;
            _pointer = packet.PointerId;
            _button = packet.Button;
            _previous = packet.Position;
            return new(true, PointerCaptureRequest.Capture);
        }

        if (!_dragging || (packet.PointerId != _pointer))
            return PointerEventResult.Ignored;
        if (packet.Kind == PointerEventKind.Move)
        {
            _yaw -= (packet.Position.X - _previous.X) * .008f;
            _pitch = Math.Clamp(_pitch - ((packet.Position.Y - _previous.Y) * .008f), -1.35f, 1.35f);
            _previous = packet.Position;
            RequestFrame();
        }

        return new(true, PointerCaptureRequest.None);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        _dragging = false;
        window.LayoutUpdated -= OnLayoutUpdated;
        window.Activated -= OnWindowShown;
        window.StateChanged -= OnWindowShown;
        _renderer?.Stop();
        Shader = null;
        _effect?.Dispose();
        _effect = null;
        Bitmap = null;
        base.OnDispose();
    }
}