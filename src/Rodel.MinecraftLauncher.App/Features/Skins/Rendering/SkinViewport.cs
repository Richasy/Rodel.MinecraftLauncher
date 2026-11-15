// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reactive;
using Sprout.SwapChain;
using System.Numerics;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Displays one retained model or atlas view; the editor owns all paint gestures and history.</summary>
internal sealed partial class SkinViewport(SkinEditorState editor, bool atlas = false, bool inset = false) : Control
{
    private readonly SwapChainView _surface = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private SkinGpuRenderer? _renderer;
    private SkinGpuFrame? _submittedFrame, _gestureFrame;
    private SkinRenderSnapshot? _lastReference;
    private bool _disposed, _scheduled, _navigating, _panning;
    private int _width, _height;
    private float _yaw = -.4f, _pitch = -.14f, _distance = 60, _zoom = 1;
    private Vector2 _pan;
    private Vector3 _cameraPan;
    private Point _previous;
    private uint _pointer;
    private PointerButton _button;
    private IDisposable? _cursor;
    private Cursor? _cursorShape;
    private bool _hovered, _wasActive;
    private string _configuration = string.Empty, _orbitSelection = string.Empty;
    private bool Active => editor.HasProject && (inset ? editor.ShowUv : editor.FlatView == atlas);

    internal void Reset()
    {
        CancelGesture();
        _yaw = editor.IsCape ? MathF.PI + .4f : -.4f;
        _pitch = -.14f;
        _distance = 60;
        _zoom = 1;
        _pan = default;
        _cameraPan = default;
        PublishZoom();
        RequestFrame();
    }

    private Ui Body => Overlay([
        _surface,
        HoverArea(PointerArea(Rectangle(Colors.Transparent), HandlePointer, CancelGesture)
            .OnKey(HandleKey)
            .Automation(new()
            {
                Name = atlas ? Resources.SkinUv : Resources.SkinModelViewport,
                AutomationId = inset ? "skin.uv.viewport" : atlas ? "skin.flat.viewport" : "skin.viewport"
            }), entered =>
            {
                _hovered = entered;
                UpdateCursor();
                if (!entered)
                    editor.Controller.Input.ClearHover(this);
            }),
        Row([
            Button(
                Row([
                    LauncherIcons.Line("refresh", 14f)
                ]),
                Reset)
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .ToolTip(Resources.SkinResetView)
                .Automation(new()
                {
                    Name = Resources.SkinResetView,
                    AutomationId = atlas ? "skin.flat.reset" : "skin.camera.reset"
                }),
            Button(
                Row([
                    LauncherIcons.Line("zoom-in", 14f)
                ]),
                () => Zoom(-1))
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .ToolTip(Resources.SkinZoomIn)
                .Automation(new()
                {
                    Name = Resources.SkinZoomIn,
                    AutomationId = atlas ? "skin.flat.in" : "skin.camera.in"
                }),
            Button(
                Row([
                    LauncherIcons.Line("zoom-out", 14f)
                ]),
                () => Zoom(1))
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .ToolTip(Resources.SkinZoomOut)
                .Automation(new()
                {
                    Name = Resources.SkinZoomOut,
                    AutomationId = atlas ? "skin.flat.out" : "skin.camera.out"
                }),
            Button(
                Row([
                    LauncherIcons.Line("skin-undo", 14f)
                ]),
                () =>
                {
                    CancelGesture();
                    _yaw -= MathF.PI / 8;
                    RequestFrame();
                })
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .Visible(!atlas)
                .ToolTip(Resources.SkinRotateView)
                .Automation(new()
                {
                    Name = Resources.SkinRotateView,
                    AutomationId = "skin.camera.rotate"
                }),
        ])
            .Spacing(4f)
            .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top)
            .Margin(new Thickness(8f))
            .Visible(!inset),
    ]);

    protected override IDisposable? OnMounted()
    {
        if (Interaction is { } inputRoot)
            inputRoot.KeyContext = new("skin-canvas");
        _surface.OnSizeChanged((width, height) =>
            {
                CancelGesture();
                _width = width;
                _height = height;
                RequestFrame();
            });
        _renderer = new(handle =>
            {
                if (handle != 0)
                    System.Runtime.InteropServices.Marshal.AddRef(handle);
                if (!_dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            if (!_disposed && (handle == 0 || Active))
                                _surface.SwapChain(handle);
                        }

                        finally
                        {
                            if (handle != 0)
                                System.Runtime.InteropServices.Marshal.Release(handle);
                        }
                    }))
                {
                    if (handle != 0)
                        System.Runtime.InteropServices.Marshal.Release(handle);
                }
            }, frame => _dispatcher.TryEnqueue(() =>
                {
                    if (!_disposed && Active)
                        _submittedFrame = frame;
                }), error => _dispatcher.TryEnqueue(() =>
                {
                    if (!_disposed)
                    {
                        _submittedFrame = null;
                        CancelGesture();
                        editor.Controller.Error(error);
                    }
                }));
        editor.Controller.Changed += RequestFrame;
        editor.Controller.Window.Deactivated += OnDeactivated;
        editor.Controller.Window.LayoutUpdated += UpdateCursor;
        return new Effect(() =>
            {
                var orbitSelection = $"{editor.DocumentModel}:{editor.Parts}";
                if (_orbitSelection != orbitSelection)
                {
                    _cameraPan = default;
                    if (_orbitSelection.Split(':')[0] != editor.DocumentModel.ToString())
                        _yaw = editor.IsCape ? MathF.PI + .4f : -.4f;
                    _orbitSelection = orbitSelection;
                }

                var configuration = $"{editor.Tool}:{editor.BrushSize}:{editor.Foreground}:{editor.TransparentInk}:{editor.Mirror}:{editor.Parts}:{editor.OuterSurface}:{editor.OuterVisible}:{editor.FlatView}:{editor.UvVisible}";
                editor.Controller.Transfers.CheckConfiguration();
                if (_configuration != configuration)
                {
                    CancelGesture();
                    _configuration = configuration;
                }

                if (!inset && Active)
                {
                    editor.SetZoom = value => Zoom(0, percent: value);
                    PublishZoom();
                }

                _ = editor.FileActive;
                _ = editor.Panel;
                UpdateCursor();
                var reference = editor.Controller.Outfit.Reference;
                if (!ReferenceEquals(reference, _lastReference))
                {
                    _lastReference = reference;
                    if (reference is not null)
                    {
                        CancelGesture();
                        _yaw = MathF.PI + .4f;
                        _cameraPan = default;
                    }
                }

                _ = editor.GridVisible;
                _ = LauncherTheme.Background;
                if (Active)
                {
                    _wasActive = true;
                    RequestFrame();
                }
                else if (_wasActive)
                {
                    _wasActive = false;
                    CancelGesture();
                    _submittedFrame = null;
                    _surface.SwapChain(0);
                    _ = _renderer?.SuspendAsync();
                }
            });
    }

    private KeyEventResult HandleKey(in KeyEvent key)
    {
        if (!Active || key.Kind != KeyEventKind.Down || key.IsSystemKey
            || key.Modifiers != KeyModifiers.None)
        {
            return KeyEventResult.Ignored;
        }

        if (key.Key == Key.Home)
        {
            Reset();
            return new(Handled: true);
        }

        var (dx, dy) = key.Key switch
        {
            Key.ArrowLeft => (-1f, 0f),
            Key.ArrowRight => (1f, 0f),
            Key.ArrowUp => (0f, -1f),
            Key.ArrowDown => (0f, 1f),
            _ => (0f, 0f),
        };
        if (dx == 0 && dy == 0)
            return KeyEventResult.Ignored;
        CancelGesture();
        if (atlas)
        {
            _pan += new Vector2(dx * 24f, dy * 24f);
        }
        else
        {
            _yaw -= dx * .15f;
            _pitch = Math.Clamp(_pitch - (dy * .15f), -1.35f, 1.35f);
        }

        RequestFrame();
        return new(Handled: true);
    }

    private void RequestFrame()
    {
        if (_disposed || _scheduled || !Active || (_width < 1) || (_height < 1) || editor.Controller.Session is null)
            return;
        _scheduled = true;
        // The swap chain presents independently. Queue its latest scene without admitting a new window
        // frame, which would repaint unrelated chrome for every brush-hover update.
        if (!_dispatcher.TryEnqueue(() =>
            {
                _scheduled = false;
                if (_disposed || !Active || (_width < 1) || (_height < 1) || editor.Controller.Session is not { } session)
                    return;
                var snapshot = session.Render();
                var color = LauncherTheme.Background;
                var cell = Math.Max(.01f, Math.Min((_width - (inset ? 12f : 64f)) / snapshot.Size, (_height - (inset ? 12f : 64f)) / snapshot.Height)) * _zoom;
                SkinAtlasView? transform = atlas ? new(cell, ((_width - (cell * snapshot.Size)) / 2) + _pan.X, ((_height - (cell * snapshot.Height)) / 2) + _pan.Y, editor.OuterSurface ? 1 : 0) : null;
                _renderer?.Submit(new(
                        snapshot,
                        Camera(snapshot.Model),
                        SceneParts,
                        editor.OuterVisible,
                        editor.GridVisible,
                        new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1),
                        session.Id,
                        transform,
                        editor.Controller.Input.Overlay(snapshot),
                        Companion: atlas ? null : editor.Controller.Outfit.Reference));
            }))
        {
            _scheduled = false;
        }
    }

    private int SceneParts => editor.Parts | (!atlas && editor.Controller.Outfit.Reference is not null
        ? editor.IsCape ? 63 : 64
        : 0);

    private SkinCamera Camera(SkinModel model) => new(
        _width,
        _height,
        _yaw,
        _pitch,
        _distance,
        _cameraPan,
        SkinOrbitPivot.For(model, editor.Parts));

    private void PublishZoom()
    {
        if (!inset && Active)
            editor.Zoom = atlas ? _zoom * 100 : 6000d / _distance;
    }

    private void Zoom(int direction, Point? pointer = null, double? percent = null)
    {
        if (editor.Controller.Input.HasGesture || (_width < 1) || (_height < 1))
            return;
        var bounds = Render.Bounds;
        var x = pointer is { } point ? (point.X - bounds.Left) / bounds.Width * _width : _width / 2f;
        var y = pointer is { } location ? (location.Y - bounds.Top) / bounds.Height * _height : _height / 2f;
        if (atlas)
        {
            var old = _zoom;
            _zoom = Math.Clamp(percent is { } flatPercent ? (float)flatPercent / 100 : _zoom * MathF.Pow(1.2f, -direction), .25f, 32f);
            _pan = new Vector2(x - (_width / 2f), y - (_height / 2f)) - ((new Vector2(x - (_width / 2f), y - (_height / 2f)) - _pan) * (_zoom / old));
        }
        else
        {
            var previous = Camera(editor.DocumentModel).PlanePoint(x, y);
            _distance = Math.Clamp(percent is { } modelPercent ? 6000f / (float)modelPercent : _distance * MathF.Pow(1.15f, direction), 28, 110);
            var next = Camera(editor.DocumentModel).PlanePoint(x, y);
            _cameraPan += Vector3.Transform(previous - next, Quaternion.Inverse(Camera(editor.DocumentModel).Rotation));
        }

        PublishZoom();
        editor.Controller.Input.ClearHover(this);
        RequestFrame();
    }

    private void OnDeactivated(object? sender, EventArgs args)
    {
        _hovered = false;
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        var next = !_disposed && _hovered && Active && !editor.FileActive && !editor.Controller.Window.Popups.HasDismissablePopups
            ? _navigating
            ? Cursor.Grabbing
            : editor.Tool == 4 ? Cursor.Of(CursorKind.Cross) : SkinToolCursors.For(editor.Tool, editor.Controller.Window.Scale)
            : null;
        if (_cursorShape == next)
            return;
        _cursor?.Dispose();
        _cursor = null;
        _cursorShape = next;
        if (next is { } shape)
            _cursor = editor.Controller.Window.Cursor.Push(shape);
    }

    private void CancelGesture()
    {
        editor.Controller.Input.Cancel(this);
        _navigating = false;
        _panning = false;
        _gestureFrame = null;
        UpdateCursor();
    }

    internal PointerEventResult HandlePointer(in PointerEvent e)
    {
        if (editor.Controller.Input.ReleaseCancelled(this, e))
            return new(true, PointerCaptureRequest.Release);
        if (e.Kind == PointerEventKind.Cancel)
        {
            CancelGesture();
            return new(true, PointerCaptureRequest.Release);
        }

        if (!Active || editor.Controller.Session is not { } session)
            return PointerEventResult.Ignored;
        if (!_navigating && !inset && editor.IsOverUv(e.Position))
        {
            return e.Kind == PointerEventKind.Down
                ? new(true, PointerCaptureRequest.None)
                : editor.Controller.Input.Handle(this, e, null, atlas);
        }

        if (e.Kind == PointerEventKind.Scroll)
        {
            Zoom(e.Scroll.Y > 0 ? -1 : 1, e.Position);
            return new(true, PointerCaptureRequest.None);
        }

        if (_navigating && (e.Kind == PointerEventKind.Down))
            return new(true, PointerCaptureRequest.None);
        var pan = e.Button == PointerButton.Primary && (e.Modifiers & KeyModifiers.Control) != 0;
        if ((e.Kind == PointerEventKind.Down) && ((e.Button == PointerButton.Secondary) || ((e.Modifiers & KeyModifiers.Alt) != 0) || pan))
        {
            if (editor.Controller.Input.HasGesture || editor.Busy || session.HasDraft || editor.Controller.HasDeferredStroke)
                return new(true, PointerCaptureRequest.None);
            _navigating = true;
            _panning = atlas || pan;
            UpdateCursor();
            _pointer = e.PointerId;
            _button = e.Button;
            _previous = e.Position;
            editor.Controller.Input.ClearHover(this);
            return new(true, PointerCaptureRequest.Capture);
        }

        if (_navigating && (e.PointerId == _pointer))
        {
            if (e.Kind == PointerEventKind.Move)
            {
                var dx = e.Position.X - _previous.X;
                var dy = e.Position.Y - _previous.Y;
                if (_panning)
                {
                    var bounds = Render.Bounds;
                    if (_width > 0 && _height > 0 && bounds.Width > 0 && bounds.Height > 0)
                    {
                        var delta = new Vector2(dx * _width / bounds.Width, dy * _height / bounds.Height);
                        if (atlas)
                            _pan += delta;
                        else
                            _cameraPan += Camera(editor.DocumentModel).PanDelta(delta.X, delta.Y);
                    }
                }
                else
                {
                    _yaw -= dx * .008f;
                    _pitch = Math.Clamp(_pitch - (dy * .008f), -1.35f, 1.35f);
                }

                _previous = e.Position;
                RequestFrame();
            }

            if ((e.Kind == PointerEventKind.Up) && (e.Button == _button))
            {
                _navigating = false;
                UpdateCursor();
                return new(true, PointerCaptureRequest.Release);
            }

            return new(true, PointerCaptureRequest.None);
        }

        if (editor.Ai.IsEditing)
            return new(true, PointerCaptureRequest.None);
        var owns = editor.Controller.Input.Owns(this);
        var frame = owns ? _gestureFrame : _submittedFrame;
        if (frame is null || (frame.DocumentId != session.Id))
            return PointerEventResult.Ignored;
        if (!owns)
        {
            var snapshot = session.Render();
            var cell = Math.Max(.01f, Math.Min((_width - (inset ? 12f : 64f)) / snapshot.Size, (_height - (inset ? 12f : 64f)) / snapshot.Height)) * _zoom;
            SkinAtlasView? expected = atlas ? new(cell, ((_width - (cell * snapshot.Size)) / 2) + _pan.X, ((_height - (cell * snapshot.Height)) / 2) + _pan.Y, editor.OuterSurface ? 1 : 0) : null;
            if ((frame.Texture.Model != snapshot.Model) || (frame.Texture.Size != snapshot.Size) || (frame.Atlas != expected) || (frame.Camera != Camera(snapshot.Model)) || (frame.Parts != SceneParts) || !ReferenceEquals(frame.Companion, atlas ? null : editor.Controller.Outfit.Reference) || (frame.Outer != editor.OuterVisible))
            {
                editor.Controller.Input.ClearHover(this);
                return PointerEventResult.Ignored;
            }
        }

        if (e.Kind == PointerEventKind.Down)
        {
            if (!editor.Pasting && !editor.Controller.Input.CanDefer && !ReferenceEquals(frame.Texture, session.Render()))
                return PointerEventResult.Ignored;
            _gestureFrame = frame;
        }

        var hit = Pick(frame, e.Position, editor.Tool == 3);
        SkinSelectionPoint? selectionPoint = null;
        if (frame.Atlas is { } selectionAtlas)
        {
            var x = (((e.Position.X - Render.Bounds.Left) / Render.Bounds.Width * frame.Camera.Width) - selectionAtlas.X) / selectionAtlas.Cell;
            var y = (((e.Position.Y - Render.Bounds.Top) / Render.Bounds.Height * frame.Camera.Height) - selectionAtlas.Y) / selectionAtlas.Cell;
            if ((e.Kind != PointerEventKind.Down) || ((x >= 0) && (y >= 0) && (x < frame.Texture.Size) && (y < frame.Texture.Height)))
            {
                selectionPoint = new(Math.Clamp((int)MathF.Floor(x), 0, frame.Texture.Size - 1), Math.Clamp((int)MathF.Floor(y), 0, frame.Texture.Height - 1));
            }
        }
        else if (hit is { } selectedHit)
        {
            var face = SkinLayout.Face(frame.Texture.Model, selectedHit.Surface, frame.Texture.Size);
            selectionPoint = new(face.X + selectedHit.X, face.Y + selectedHit.Y, selectedHit.Surface);
        }

        return editor.Controller.Input.Handle(
            this,
            e,
            hit,
            atlas,
            selectionPoint);
    }

    private SkinHit? Pick(SkinGpuFrame frame, Point point, bool observe)
    {
        var bounds = Render.Bounds;
        var x = (point.X - bounds.Left) / bounds.Width * frame.Camera.Width;
        var y = (point.Y - bounds.Top) / bounds.Height * frame.Camera.Height;
        if ((x < 0) || (y < 0) || (x >= frame.Camera.Width) || (y >= frame.Camera.Height))
            return null;
        if (frame.Atlas is { } flat)
        {
            var hit = frame.Texture.PickAtlas((x - flat.X) / flat.Cell, (y - flat.Y) / flat.Cell, editor.OuterSurface ? SkinLayer.Outer : SkinLayer.Base);
            return hit is { } found && ((frame.Parts & (1 << (int)found.Surface.Part)) != 0) ? found : null;
        }

        var parts = Enum.GetValues<SkinPart>().Where(part => (editor.Parts & (1 << (int)part)) != 0).ToArray();
        var ray = frame.Camera.Ray(x, y, editor.OuterSurface ? SkinLayer.Outer : SkinLayer.Base, parts);
        return SkinSceneSnapshot.Pick(frame.Texture, frame.Companion, observe ? ray with
        {
            Layer = frame.Outer ? null : SkinLayer.Base,
            SkipTransparent = true
        } : ray, frame.Outer);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        CancelGesture();
        editor.Controller.Changed -= RequestFrame;
        editor.Controller.Window.Deactivated -= OnDeactivated;
        editor.Controller.Window.LayoutUpdated -= UpdateCursor;
        _surface.SwapChain(0);
        _renderer?.Stop();
        base.OnDispose();
    }
}