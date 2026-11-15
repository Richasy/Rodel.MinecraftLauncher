// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Displays only completed original tiles and routes pan, pointer-centred zoom and real point queries.</summary>
internal sealed partial class SeedMapSurface(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private readonly SeedMapDragLayer _layer = new(state, popups);
    private bool _dragging, _moved, _selectingDrag, _panStarted;
    private Point _start;
    private double _startX, _startZ;
    private int _selectionX, _selectionZ;
    private uint _pointer;
    private SeedExplorerSnapshot Current => state.Current;
    internal Rect Bounds => Render.Bounds;

    private Ui Body => PointerArea(Border(Clip(_layer)).Background(!Current.HasMap || LauncherTheme.HighContrast ? LauncherTheme.Background : Color.FromHex("#34423f")), Pointer, CancelDrag)
        .OnKey(HandleKey)
        .Automation(new() { Name = Resources.SeedMap, AutomationId = "seed.map" });

    private KeyEventResult HandleKey(in KeyEvent key)
    {
        if (key.Kind != KeyEventKind.Down || key.IsSystemKey || key.Modifiers != KeyModifiers.None
            || Current.Opening || !Current.HasMap || Current.SelectingRegion)
        {
            return KeyEventResult.Ignored;
        }

        var (dx, dz) = key.Key switch
        {
            Key.ArrowLeft => (-1, 0),
            Key.ArrowRight => (1, 0),
            Key.ArrowUp => (0, -1),
            Key.ArrowDown => (0, 1),
            _ => (0, 0),
        };
        if (dx != 0 || dz != 0)
        {
            state.Controller.Map.Pan(Current.CenterX + (dx * Current.MapWidth * Current.BlocksPerPixel / 4),
                Current.CenterZ + (dz * Current.MapHeight * Current.BlocksPerPixel / 4));
        }
        else if (key.Key == Key.Home)
        {
            if (Current.Spawn is not null) state.Controller.Map.ReturnToSpawn();
            else state.Controller.Map.ReturnToOrigin();
        }
        else if (key.Key == Key.Enter)
        {
            _ = state.Controller.Map.QueryPointAsync((int)Math.Floor(Current.CenterX), (int)Math.Floor(Current.CenterZ), popups.ShowPoint);
        }
        else
        {
            return KeyEventResult.Ignored;
        }

        return new(Handled: true);
    }

    private void CancelDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _panStarted = false;
        _layer.Offset(default);
        if (Current.Panning) state.Current = Current with { Panning = false };
        if (_selectingDrag) state.Controller.Regions.CancelSelection();
        else if (_moved) state.Controller.Map.ResumeViewport();
    }

    private (int X, int Z) World(Point point) => ((int)Math.Clamp(Math.Floor(Current.CenterX + ((point.X - Bounds.Left - (Current.MapWidth / 2)) * Current.BlocksPerPixel)), -30_000_000, 29_999_999),
        (int)Math.Clamp(Math.Floor(Current.CenterZ + ((point.Y - Bounds.Top - (Current.MapHeight / 2)) * Current.BlocksPerPixel)), -30_000_000, 29_999_999));

    private (double X, double Z, Point Offset) Pan(Point point)
    {
        var (X, Z) = state.Controller.Map.LimitPan(_startX - ((point.X - _start.X) * Current.BlocksPerPixel), _startZ - ((point.Y - _start.Y) * Current.BlocksPerPixel));
        return (X, Z, new((float)((_startX - X) / Current.BlocksPerPixel), (float)((_startZ - Z) / Current.BlocksPerPixel)));
    }

    private PointerEventResult Pointer(in PointerEvent e)
    {
        if (Current.Opening || !Current.HasMap)
        {
            if (_dragging && e.PointerId == _pointer && e.Kind is PointerEventKind.Up or PointerEventKind.Cancel) { CancelDrag(); return new(true, PointerCaptureRequest.Release); }

            return PointerEventResult.Ignored;
        }

        if (e.Kind == PointerEventKind.Scroll)
        {
            if (Current.SelectingRegion) return new(true, PointerCaptureRequest.None);
            var capture = _dragging ? PointerCaptureRequest.Release : PointerCaptureRequest.None;
            CancelDrag();
            var x = Current.CenterX + ((e.RootPosition.X - Bounds.Left - (Current.MapWidth / 2)) * Current.BlocksPerPixel);
            var z = Current.CenterZ + ((e.RootPosition.Y - Bounds.Top - (Current.MapHeight / 2)) * Current.BlocksPerPixel);
            state.Controller.Map.Zoom(e.Scroll.Y > 0 ? .5 : 2, x, z);
            return new(true, capture);
        }

        if (e.Kind == PointerEventKind.Down && e.Button == PointerButton.Primary)
        {
            if (_dragging) return PointerEventResult.Ignored;
            _dragging = true; _moved = false; _panStarted = false; _selectingDrag = Current.SelectingRegion; _start = e.RootPosition; _startX = Current.CenterX; _startZ = Current.CenterZ; _pointer = e.PointerId;
            if (Current.SelectingRegion) { (_selectionX, _selectionZ) = World(e.RootPosition); state.Controller.Regions.Select(_selectionX, _selectionZ, _selectionX, _selectionZ); }

            return new(true, PointerCaptureRequest.Capture);
        }

        if (!_dragging || e.PointerId != _pointer) return PointerEventResult.Ignored;
        if (_selectingDrag && !Current.SelectingRegion) { CancelDrag(); return new(true, PointerCaptureRequest.Release); }

        if (e.Kind == PointerEventKind.Move)
        {
            var dx = e.RootPosition.X - _start.X; var dy = e.RootPosition.Y - _start.Y;
            _moved |= Math.Abs(dx) + Math.Abs(dy) > 4;
            if (Current.SelectingRegion) { var (X, Z) = World(e.RootPosition); state.Controller.Regions.Select(_selectionX, _selectionZ, X, Z); }
            else if (_moved)
            {
                if (!_panStarted) { _panStarted = true; state.Current = Current with { Panning = true }; state.Controller.Map.BeginPan(); }

                _layer.Offset(Pan(e.RootPosition).Offset);
            }

            return new(true, PointerCaptureRequest.None);
        }

        if (e.Kind == PointerEventKind.Up && e.Button == PointerButton.Primary)
        {
            _dragging = false;
            _panStarted = false;
            if (Current.Panning) state.Current = Current with { Panning = false };
            if (Current.SelectingRegion)
            {
                _layer.Offset(default);
                var (X, Z) = World(e.RootPosition); state.Controller.Regions.Select(_selectionX, _selectionZ, X, Z);
                state.Current = Current with { SelectingRegion = false }; popups.ShowRegion();
                return new(true, PointerCaptureRequest.Release);
            }

            if (_moved)
            {
                var (X, Z, _) = Pan(e.RootPosition);
                state.Controller.Map.Pan(X, Z);
            }
            else
            {
                var x = (int)Math.Floor(Current.CenterX + ((e.RootPosition.X - Bounds.Left - (Current.MapWidth / 2)) * Current.BlocksPerPixel));
                var z = (int)Math.Floor(Current.CenterZ + ((e.RootPosition.Y - Bounds.Top - (Current.MapHeight / 2)) * Current.BlocksPerPixel));
                _ = state.Controller.Map.QueryPointAsync(x, z, popups.ShowPoint);
            }

            _layer.Offset(default);
            return new(true, PointerCaptureRequest.Release);
        }

        if (e.Kind == PointerEventKind.Cancel) { CancelDrag(); return new(true, PointerCaptureRequest.Release); }

        return PointerEventResult.Ignored;
    }
}

/// <summary>Moves one retained world paint tree and the corresponding hit-test and automation geometry.</summary>
internal sealed partial class SeedMapDragLayer(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private readonly SeedMapLayers _layers = new(state, popups);
    internal void Offset(Point value) { if (ReconcileRender is TransformWidget transform) SeedMapPanPresentation.Offset(transform, Interaction, Semantics, value); }
    private Ui Body => Transform(_layers);
}

internal sealed partial class SeedMapLayers(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private readonly SeedMapRaster _raster = new(state);
    private readonly SeedMarkerLayer _markers = new(state, popups);
    private SeedExplorerSnapshot Current => state.Current;
    private Ui Body => Overlay([
        _raster,
        new SeedSelectionOverlay(state).Visible(Current.RegionBounds is not null),
        _markers.Visible(!Current.SelectingRegion),
        new SeedPlayerPin(state).Visible(Current.HasMap && Current.WorldSource && !Current.SearchMode && Current.TrackPlayer
            && Current.Player?.Player is { } player && player.Dimension == Current.World.Dimension),
        new SeedPickedPointOverlay(state).Visible(Current.PickedPoint is not null && !Current.SelectingRegion),
    ]);
}

/// <summary>Paints retained and current samples on one geographic display grid, independently of map overlays.</summary>
internal sealed partial class SeedMapRaster(SeedExplorerState state) : Control
{
    private readonly SeedMapPresentation _presentation = new();
    private SeedExplorerSnapshot Current => state.Current;
    private SeedMapFrame[] Frames => _presentation.Frame(Current) is { } frame ? [frame] : [];
    private Ui Body => Overlay([Each(Frames, _ => 1, frame => new SeedMapFrameView(frame, Current))]);
}

/// <summary>Retains marker identity and native input inside the map's shared geographic paint subtree.</summary>
internal sealed partial class SeedMarkerLayer(SeedExplorerState state, SeedExplorerPopups popups) : Control
{
    private readonly Dictionary<string, int> _keys = [];
    private int _key;
    private SeedExplorerSnapshot Current => state.Current;
    private SeedMarker[] Markers => !Current.HasMap ? [] : SeedMapMarkers.Layout(Current);
    private int Key(SeedMarker marker) { if (!_keys.TryGetValue(marker.Id, out var key)) _keys[marker.Id] = key = ++_key; return key; }
    private Ui Body => ItemsRepeater(Markers, item => Key(item), marker => new SeedMarkerView(marker, Current, () => popups.ShowPosition(marker.Positions[0])),
        new SeedMarkerLayout(Markers, Current.MapWidth, Current.MapHeight)).KeyboardNavigation();
}

/// <summary>Marks a transient terrain query independently of the collision-filtered place layer.</summary>
internal sealed partial class SeedPickedPointOverlay(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private SeedPosition Point => Current.PickedPoint ?? new();
    private float Left => (float)(((Point.X + .5 - Current.CenterX) / Current.BlocksPerPixel) + (Current.MapWidth / 2)) - 11f;
    private float Top => (float)(((Point.Z + .5 - Current.CenterZ) / Current.BlocksPerPixel) + (Current.MapHeight / 2)) - 11f;
    private Ui Body => Canvas([
        Border(LauncherIcons.Line("map-point", 18f, LauncherTheme.Ink)
            .Automation(new() { Name = SeedText.Coordinates(Point), AutomationId = "seed.point.selection" }))
            .Padding(2f).Background(LauncherTheme.Background.MultiplyAlpha(.75f)).Width(22f).Height(22f).At(Left, Top),
    ]).Width(Current.MapWidth).Height(Current.MapHeight);
}

[View]
internal readonly partial record struct SeedMapFrameView(SeedMapFrame Frame, SeedExplorerSnapshot State)
{
    /// <summary>Places the uniform display grid at its actual world extent without changing retained source data.</summary>
    public Ui Body => Canvas([Image(Frame.Image).Stretch(ImageStretch.Fill).Interpolation(ImageInterpolationMode.NearestNeighbor)
        .Width((float)(Frame.Width * Frame.Step / State.BlocksPerPixel)).Height((float)(Frame.Height * Frame.Step / State.BlocksPerPixel))
        .At((float)(((Frame.MinX - State.CenterX) / State.BlocksPerPixel) + (State.MapWidth / 2)),
            (float)(((Frame.MinZ - State.CenterZ) / State.BlocksPerPixel) + (State.MapHeight / 2)))]).Width(State.MapWidth).Height(State.MapHeight);
}

[View]
internal readonly partial record struct SeedMarkerView(SeedMarker Marker, SeedExplorerSnapshot State, Action Open)
{
    /// <summary>Hosts one retained row/editor with stable native control identity.</summary>
    public Ui Body => new SeedMarkerViewControl(Marker, State, Open);
}

internal sealed partial class SeedMarkerViewControl : Control
{
    internal SeedMarkerViewControl(SeedMarker marker, SeedExplorerSnapshot state, Action open) { Marker = marker; State = state; Open = open; }
    private partial SeedMarker Marker { get; set; }
    private partial SeedExplorerSnapshot State { get; set; }
    private partial Action Open { get; set; }
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) { if (source is SeedMarkerViewControl other) { Marker = other.Marker; State = other.State; Open = other.Open; } }

    private SeedPosition Position => Marker.Positions[0];
    private string Label => State.UseSavedMap && State.PreviewPositions.Any(position => position.Id == Position.Id && position.X == Position.X && position.Z == Position.Z)
        ? string.Format(Resources.SeedMapStatusFormat, SeedText.Position(Position, State), Resources.SeedWorldPreviewStatus) : SeedText.Position(Position, State);
    private bool Selected => State.SelectedPlace is { } selected && Position.Kind == selected.Kind && Position.Id == selected.Id && Position.X == selected.X && Position.Z == selected.Z;
    /// <summary>Displays one real place using the same single-element image as its legend and detail popup.</summary>
    public Ui Body => Button(Row([new SeedVisual(SeedAppearance.For(Position), SeedAppearance.Symbols || Position.Kind is "origin" or "spawn", Marker.Size - 4f)
        .Automation(new() { AutomationId = "seed.marker.image." + Marker.Id })]), Open).Style(Position.Kind is "origin" or "spawn" ? SeedChrome.OriginPin(Selected) : SeedChrome.Pin(Selected)).Width(Marker.Size).Height(Marker.Size).ToolTip(Label).Enabled(!State.Opening)
        .Automation(new() { Name = Label, AutomationId = "seed.marker." + Marker.Id, HelpText = SeedText.Coordinates(Position) });
}

internal sealed partial class SeedSelectionOverlay(SeedExplorerState state) : Control
{
    private SeedExplorerSnapshot Current => state.Current;
    private float Left => Pixel(Current.RegionBounds?.MinX ?? 0, true);
    private float Top => Pixel(Current.RegionBounds?.MinZ ?? 0, false);
    private float Right => Pixel((Current.RegionBounds?.MaxX ?? -1) + 1, true);
    private float Bottom => Pixel((Current.RegionBounds?.MaxZ ?? -1) + 1, false);
    private float Pixel(int value, bool x) => SeedMapPanPresentation.Project(value, x ? Current.CenterX : Current.CenterZ, Current.BlocksPerPixel, x ? Current.MapWidth : Current.MapHeight);
    private Ui Body => Canvas([Border(Text(string.Empty)).Background(LauncherTheme.ProgressAccent.MultiplyAlpha(.14f)).BorderBrush(LauncherTheme.ProgressAccent).BorderThickness(2f)
        .Width(Math.Max(0, Right - Left)).Height(Math.Max(0, Bottom - Top)).At(Left, Top)]).Width(Current.MapWidth).Height(Current.MapHeight);
}