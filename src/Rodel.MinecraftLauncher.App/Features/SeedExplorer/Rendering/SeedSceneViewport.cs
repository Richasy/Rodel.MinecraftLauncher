// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Reactive;
using Sprout.Reconcile;
using Sprout.SwapChain;
using System.Numerics;
using System.Runtime.InteropServices;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;

/// <summary>Hosts original-model GPU presentation and stationary matching camera markers; both mouse buttons orbit the model.</summary>
internal sealed partial class SeedSceneViewport(SeedExplorerState state) : Control
{
    private readonly SwapChainView _surface = new();
    private SeedSceneGpuRenderer? _renderer;
    private partial SeedSceneFrame? DisplayedFrame { get; set; }
    private readonly SeedSceneOrbitGesture _orbit = new();
    private bool _disposed, _scheduled;
    private int _width, _height;
    private partial bool Orbiting { get; set; }
    private SeedExplorerSnapshot Current => state.Current;
    private SeedSceneFrame? MarkerFrame => !Orbiting && DisplayedFrame is { } frame && ReferenceEquals(Current.SceneMesh, frame.Mesh)
        && frame.Projection == state.Camera.Project(frame.Mesh, frame.Projection.Width, frame.Projection.Height) ? frame : null;
    private void CancelOrbit() { _orbit.Cancel(); Orbiting = false; }
    private Ui Body => PointerArea(Clip(Flow(FlowDirection.LeftToRight, Overlay([
        _surface.Automation(new() { Name = Resources.SeedPreviewStructure, AutomationId = "seed.scene.viewport" }),
        new SeedScenePins(Current, MarkerFrame, state.Controller.Regions.SelectContainer),
    ]).Background(LauncherTheme.ControlBackground))), Pointer, CancelOrbit)
        .OnKey(HandleKey)
        .Automation(new() { Name = Resources.SeedPreviewStructure, AutomationId = "seed.scene.input" });

    private KeyEventResult HandleKey(in KeyEvent key)
    {
        if (key.Kind != KeyEventKind.Down || key.IsSystemKey || key.Modifiers != KeyModifiers.None
            || Current.SceneBusy || Current.SceneMesh is null)
        {
            return KeyEventResult.Ignored;
        }

        var (dx, dy) = key.Key switch
        {
            Key.ArrowLeft => (-40f, 0f),
            Key.ArrowRight => (40f, 0f),
            Key.ArrowUp => (0f, -40f),
            Key.ArrowDown => (0f, 40f),
            _ => (0f, 0f),
        };
        if (dx != 0 || dy != 0) state.Camera = state.Camera.Drag(dx, dy);
        else if (key.Key == Key.Home) state.Camera = new();
        else return KeyEventResult.Ignored;
        return new(Handled: true);
    }

    protected override IDisposable? OnMounted()
    {
        _surface.OnSizeChanged((width, height) => { _width = width; _height = height; Request(); });
        state.Controller.Window.StateChanged += Shown;
        return new Effect(() => { _ = Current.SceneOpen; _ = Current.SceneMesh; _ = state.Camera; _ = Current.SceneRenderVersion; _ = LauncherTheme.ControlBackground; Request(); });
    }
    private void Shown(object? sender, EventArgs args) => Request();
    private void StopRenderer()
    {
        _surface.SwapChain(0); _renderer?.Stop(); _renderer = null; DisplayedFrame = null;
    }
    private void Request()
    {
        if (_disposed) return;
        if (!Current.SceneOpen || Current.SceneMesh is null) { StopRenderer(); return; }

        if (Current.SceneError.Length > 0 || _scheduled || state.Controller.Window.State == WindowState.Minimized) return;
        _scheduled = true;
        state.Controller.Window.RequestAnimationFrame(_ =>
        {
            _scheduled = false;
            if (_disposed || !Current.SceneOpen || Current.SceneMesh is not { } mesh || state.Controller.Window.State == WindowState.Minimized) return;
            var size = Render.Bounds;
            if (size.Width < 1 || size.Height < 1 || _width < 1 || _height < 1) return;
            EnsureRenderer();
            var color = LauncherTheme.ControlBackground;
            var frame = new SeedSceneFrame(mesh, state.Camera.Project(mesh, size.Width, size.Height), _width, _height,
                new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1));
            _renderer!.Submit(frame);
        });
    }
    private void EnsureRenderer()
    {
        if (_renderer is not null) return;
        SeedSceneGpuRenderer? worker = null;
        SeedSceneFrame? pending = null;
        var queued = 0;
        worker = new(handle =>
        {
            if (handle != 0) Marshal.AddRef(handle);
            if (!state.Controller.Dispatcher.TryEnqueue(() =>
            {
                try { if (!_disposed && ReferenceEquals(worker, _renderer)) _surface.SwapChain(handle); }
                finally { if (handle != 0) Marshal.Release(handle); }
            }) && handle != 0)
            {
                Marshal.Release(handle);
            }
        }, frame =>
        {
            Interlocked.Exchange(ref pending, frame);
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            if (!state.Controller.Dispatcher.TryEnqueue(() =>
            {
                Volatile.Write(ref queued, 0);
                var completed = Interlocked.Exchange(ref pending, null);
                if (!_disposed && ReferenceEquals(worker, _renderer) && Current.SceneOpen && completed is not null && ReferenceEquals(Current.SceneMesh, completed.Mesh))
                    DisplayedFrame = completed;
            }))
            {
                Volatile.Write(ref queued, 0);
            }
        }, error => state.Controller.Dispatcher.TryEnqueue(() =>
        {
            if (_disposed || !ReferenceEquals(worker, _renderer)) return;
            StopRenderer(); state.Current = Current with { SceneError = error };
        }));
        _renderer = worker;
        state.Controller.Regions.Register(worker);
    }
    private PointerEventResult Pointer(in PointerEvent e)
    {
        var enabled = !Current.SceneBusy && Current.SceneMesh is not null;
        var result = _orbit.Handle(e, enabled, out var delta);
        Orbiting = _orbit.Active;
        if (result.Handled)
        {
            if (delta.X != 0 || delta.Y != 0) state.Camera = state.Camera.Drag(delta.X, delta.Y);
            return result;
        }

        if (enabled && e.Kind == PointerEventKind.Scroll)
        {
            state.Camera = state.Camera with { Zoom = Math.Clamp(state.Camera.Zoom * (e.Scroll.Y > 0 ? 1.15f : 1 / 1.15f), .4f, 4f) };
            return new(true, PointerCaptureRequest.None);
        }

        return PointerEventResult.Ignored;
    }
    protected override void OnDispose()
    {
        _disposed = true; CancelOrbit(); state.Controller.Window.StateChanged -= Shown;
        StopRenderer(); base.OnDispose();
    }
}

[View]
internal readonly partial record struct SeedScenePins(SeedExplorerSnapshot State, SeedSceneFrame? Frame, Action<SeedContainer> Select)
{
    /// <summary>Shows markers only for a completed frame matching the current stationary camera.</summary>
    public Ui Body => new SeedScenePinsControl(State, Frame, Select);
}

internal sealed partial class SeedScenePinsControl : Control
{
    internal SeedScenePinsControl(SeedExplorerSnapshot state, SeedSceneFrame? frame, Action<SeedContainer> select) { State = state; Frame = frame; Select = select; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial SeedSceneFrame? Frame { get; set; }
    private partial Action<SeedContainer> Select { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedScenePinsControl other) { State = other.State; Frame = other.Frame; Select = other.Select; } }
    private SeedScenePin[] Pins
    {
        get
        {
            if (Frame is not { } frame || State.SceneData is not { } data) return [];
            var b = data.Bounds;
            return [.. data.Containers.Select((container, index) =>
            {
                var point = frame.Projection.Point(new Vector3(container.X - b.MinX + .5f, container.Y - b.MinY + 1.5f, container.Z - b.MinZ + .5f));
                return new SeedScenePin(index, container, point.X - 13, point.Y - 26,
                    container.Y >= frame.Mesh.MinY && container.Y <= frame.Mesh.MaxY && point.X >= 14 && point.X <= frame.Projection.Width - 14 && point.Y >= 28 && point.Y <= frame.Projection.Height,
                    State.SceneContainer == container, SeedRegionText.Name(State, container.Id) + " · " + SeedRegionText.Position(container));
            })];
        }
    }
    private SeedScenePin[] VisiblePins => [.. Pins.Where(pin => pin.Visible)];
    private Ui Body => ItemsRepeater(VisiblePins, pin => pin.Index, pin => new SeedScenePinView(LauncherAppearance.Capture(), pin, () => Select(pin.Container)),
        new SeedMarkerLayout([.. VisiblePins.Select(pin => new SeedMarker(pin.Index.ToString(), pin.X + 13, pin.Y + 13, []))], Frame?.Projection.Width ?? 0, Frame?.Projection.Height ?? 0, 24f)).KeyboardNavigation();
}

internal sealed record SeedScenePin(int Index, SeedContainer Container, float X, float Y, bool Visible, bool Selected, string Label);
[View]
internal readonly partial record struct SeedScenePinView(LauncherAppearance Theme, SeedScenePin Pin, Action Open)
{
    /// <summary>Offers a keyboard-focusable original container marker at its projected position.</summary>
    public Ui Body => Button(Text((Pin.Index + 1).ToString()).FontSize(11f), Open)
        .Style(SeedChrome.For(Theme).ScenePin(Pin.Selected)).Width(24f).Height(24f).ToolTip(Pin.Label)
        .Automation(new() { Name = Pin.Label, AutomationId = $"seed.scene.pin.{Pin.Container.X}.{Pin.Container.Y}.{Pin.Container.Z}" })
        ;
}