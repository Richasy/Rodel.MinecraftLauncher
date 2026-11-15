// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Skins.Rendering;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>One local, decoded reference; only its edited colors are saved in palette preferences.</summary>
internal sealed record SkinPaletteReference(string Name, SkinPaletteImage Image, ImageSource Preview, string[] Colors);

/// <summary>Offers explicit zoom/reset controls and original-pixel sampling above the draft swatches.</summary>
internal sealed partial class SkinPaletteReferenceView : Control
{
    private readonly SkinPaletteReference? _reference;
    private readonly SkinPaletteReferenceSurface _surface;

    internal SkinPaletteReferenceView(Window window, SkinPaletteReference? reference, Action<string> add)
    {
        _reference = reference;
        _surface = new(window, reference, add);
    }

    internal SkinPaletteReferenceView WithSize(float width, float height)
    {
        _surface.Resize(width, height);
        return this;
    }

    internal bool StopPicking() => _surface.StopPicking();

    private Ui Body => Column([
        Row([
            Flow(FlowDirection.LeftToRight,
                Text(_reference?.Name ?? string.Empty)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed())
                .Weight(1f),
            new SkinEditorToolButton(
                LauncherAppearance.Capture(),
                Resources.SkinPalettePickImage,
                "skin-picker",
                "skin.palette.reference.picker",
                _surface.Picking,
                _surface.TogglePicking,
                SkinEditorChrome.ToolPalette,
                SkinEditorChrome.ToolStyle,
                28f).Width(28f),
            Button(Row([LauncherIcons.Line("zoom-out", 16f)]), () => _surface.Zoom(.8f))
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .ToolTip(Resources.SkinZoomOut)
                .Automation(new() { Name = Resources.SkinZoomOut, AutomationId = "skin.palette.reference.zoom-out" }),
            Button(Row([LauncherIcons.Line("zoom-in", 16f)]), () => _surface.Zoom(1.25f))
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .ToolTip(Resources.SkinZoomIn)
                .Automation(new() { Name = Resources.SkinZoomIn, AutomationId = "skin.palette.reference.zoom-in" }),
            Button(Row([LauncherIcons.Line("refresh", 16f)]), _surface.Reset)
                .Style(SkinEditorChrome.QuietIconCommand)
                .Width(28f).Height(28f)
                .ToolTip(Resources.SkinResetView)
                .Automation(new() { Name = Resources.SkinResetView, AutomationId = "skin.palette.reference.reset" }),
        ]).Spacing(4f),
        _surface,
        Text(Resources.SkinPaletteImageHint)
            .FontSize(11f).Foreground(LauncherTheme.Muted).Wrap(),
    ]).Spacing(8f);
}

/// <summary>Keeps navigation, the magnifier and exact sampling on one image transform and captured gesture.</summary>
internal sealed partial class SkinPaletteReferenceSurface : Control
{
    private static readonly ImageSource Empty = ImageSource.FromPixels(new(1, 1, new byte[4]));
    private readonly Window _window;
    private readonly SkinPaletteReference? _reference;
    private readonly Action<string> _add;
    private readonly List<IDisposable> _keys = [];
    private PopupFocusRecovery? _focus;
    private IDisposable? _cursor;
    private Cursor? _cursorShape;
    private bool _hovered, _focused, _dragging, _sampling;
    private uint _pointer;
    private Point _previous;
    private (int X, int Y)? _pixel;

    internal SkinPaletteReferenceSurface(Window window, SkinPaletteReference? reference, Action<string> add)
    {
        _window = window;
        _reference = reference;
        _add = add;
        View = new(reference?.Image.Width ?? 1, reference?.Image.Height ?? 1, 560, 280);
        Sample = string.Empty;
    }

    internal partial bool Picking { get; set; }
    private partial SkinReferenceTransform View { get; set; }
    private partial ImageSource? Lens { get; set; }
    private partial string Sample { get; set; }
    private partial Point Position { get; set; }
    private float LensLeft => Math.Clamp(Position.X + 24 + 130 <= View.Width ? Position.X + 24 : Position.X - 154, 0, Math.Max(0, View.Width - 130));
    private float LensTop => Math.Clamp(Position.Y - 150, 0, Math.Max(0, View.Height - 150));

    internal void Resize(float width, float height)
    {
        if (View.Width == width && View.Height == height)
            return;
        CancelGesture();
        View = (View with { Width = width, Height = height }).Reset();
        ClearSample();
    }

    private Ui Body => Row([Button(
        HoverArea(PointerArea(
            Flow(FlowDirection.LeftToRight, Clip(Canvas([
                Image(_reference?.Preview ?? Empty)
                    .Stretch(ImageStretch.Fill)
                    .Interpolation(View.Scale >= 1 ? ImageInterpolationMode.NearestNeighbor : ImageInterpolationMode.HighQualityCubic)
                    .Width(View.ImageWidth * View.Scale).Height(View.ImageHeight * View.Scale)
                    .At(View.Left, View.Top),
                Border(Column([
                    Overlay([
                        Image(Lens ?? Empty).Stretch(ImageStretch.Fill).Interpolation(ImageInterpolationMode.NearestNeighbor),
                        Border(Rectangle(Colors.Transparent))
                            .BorderBrush(Colors.Black).BorderThickness(1f)
                            .Width(12f).Height(12f)
                            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center),
                        Border(Rectangle(Colors.Transparent))
                            .BorderBrush(Colors.White).BorderThickness(1f)
                            .Width(10f).Height(10f)
                            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center),
                    ]).Width(110f).Height(110f),
                    Flow(FlowDirection.LeftToRight,
                        Text(Sample).FontSize(11f)
                            .Automation(new() { Name = Sample, AutomationId = "skin.palette.reference.sample" })),
                ]).Spacing(6f))
                    .Padding(8f).Background(LauncherTheme.Background)
                    .BorderBrush(LauncherTheme.Edge).BorderThickness(1f)
                    .At(LensLeft, LensTop)
                    .Visible(Picking && Lens is not null),
            ]).Width(View.Width).Height(View.Height))),
            HandlePointer,
            CancelGesture), Hover), () => Pick())
        .Style(SkinEditorChrome.QuietIconCommand.Padding(0f))
        .Width(View.Width).Height(View.Height)
        .OnGotFocus(_ => _focused = true)
        .OnLostFocus(() =>
        {
            _focused = false;
            CancelGesture();
        })
        .Automation(new()
        {
            Name = Resources.SkinPalettePickImage,
            AutomationId = "skin.palette.reference.image",
            HelpText = Resources.SkinPaletteImageHint
        })]);

    internal void TogglePicking()
    {
        if (StopPicking())
            return;
        Picking = true;
        _focus?.Request(this);
        SampleAt(new(View.Width / 2, View.Height / 2));
        UpdateCursor();
    }

    internal bool StopPicking()
    {
        if (!Picking)
            return false;
        Picking = false;
        CancelGesture();
        ClearSample();
        UpdateCursor();
        return true;
    }

    internal void Zoom(float factor, Point? anchor = null)
    {
        CancelGesture();
        var point = anchor ?? new(View.Width / 2, View.Height / 2);
        View = View.ZoomAt(factor, point.X, point.Y);
        if (Picking)
            SampleAt(point);
    }

    internal void Reset()
    {
        CancelGesture();
        View = View.Reset();
        if (Picking)
            SampleAt(new(View.Width / 2, View.Height / 2));
    }

    private void Hover(bool entered)
    {
        _hovered = entered;
        if (!entered && !_sampling)
            ClearSample();
        UpdateCursor();
    }

    private PointerEventResult HandlePointer(in PointerEvent e)
    {
        var point = new Point(e.Position.X - Render.Bounds.Left, e.Position.Y - Render.Bounds.Top);
        if (e.Kind == PointerEventKind.Cancel)
        {
            CancelGesture();
            return new(true, PointerCaptureRequest.Release);
        }

        if (e.Kind == PointerEventKind.Scroll)
        {
            if (e.Scroll.Y == 0)
                return PointerEventResult.Ignored;
            Zoom(e.Scroll.Y > 0 ? 1.25f : .8f, point);
            return new(true, PointerCaptureRequest.None);
        }

        if (e.Kind == PointerEventKind.Down && e.Button is PointerButton.Primary or PointerButton.Secondary)
        {
            _pointer = e.PointerId;
            _previous = point;
            _dragging = !Picking || e.Button == PointerButton.Secondary || (e.Modifiers & KeyModifiers.Control) != 0;
            _sampling = !_dragging;
            _focus?.Request(this);
            if (_sampling)
                SampleAt(point);
            else
                ClearSample();
            UpdateCursor();
            return new(true, PointerCaptureRequest.Capture);
        }

        if (e.Kind == PointerEventKind.Move)
        {
            if (_dragging && e.PointerId == _pointer)
            {
                View = View.Move(point.X - _previous.X, point.Y - _previous.Y);
                _previous = point;
            }
            else if (Picking && (!_sampling || e.PointerId == _pointer))
            {
                SampleAt(point);
            }

            return new(true, PointerCaptureRequest.None);
        }

        if (e.Kind == PointerEventKind.Up && e.PointerId == _pointer)
        {
            if (_sampling)
            {
                SampleAt(point);
                Pick();
            }

            CancelGesture();
            return new(true, PointerCaptureRequest.Release);
        }

        return PointerEventResult.Ignored;
    }

    private void SampleAt(Point point)
    {
        Position = point;
        if (_reference is null || !View.TryPixel(point.X, point.Y, out var x, out var y))
        {
            ClearSample();
            return;
        }

        SetPixel(x, y);
    }

    private void SetPixel(int x, int y)
    {
        if (_reference is null || _pixel == (x, y))
            return;
        _pixel = (x, y);
        Sample = _reference.Image.Sample(x, y);
        Lens = _reference.Image.Magnify(x, y);
    }

    private void Nudge(int x, int y)
    {
        var (pixelX, pixelY) = _pixel ?? (View.ImageWidth / 2, View.ImageHeight / 2);
        var nextX = Math.Clamp(pixelX + x, 0, View.ImageWidth - 1);
        var nextY = Math.Clamp(pixelY + y, 0, View.ImageHeight - 1);
        var px = View.Left + ((nextX + .5f) * View.Scale);
        var py = View.Top + ((nextY + .5f) * View.Scale);
        if (px < 0 || px >= View.Width || py < 0 || py >= View.Height)
            View = View.Move((View.Width / 2) - px, (View.Height / 2) - py);
        Position = new(View.Left + ((nextX + .5f) * View.Scale), View.Top + ((nextY + .5f) * View.Scale));
        SetPixel(nextX, nextY);
    }

    private void Pick()
    {
        if (!Picking || _pixel is null || Sample.Length == 0)
            return;
        _add(Sample);
        StopPicking();
    }

    private void ClearSample()
    {
        _pixel = null;
        Lens = null;
        Sample = string.Empty;
    }

    private void CancelGesture()
    {
        _dragging = _sampling = false;
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        var next = _hovered
            ? _dragging ? Cursor.Grabbing : Picking ? SkinToolCursors.For(3, _window.Scale) : Cursor.Grab
            : null;
        if (_cursorShape == next)
            return;
        _cursor?.Dispose();
        _cursor = null;
        _cursorShape = next;
        if (next is { } cursor)
            _cursor = _window.Cursor.Push(cursor);
    }

    protected override IDisposable? OnMounted()
    {
        _focus = new(_window);
        Bind(Key.ArrowLeft, () => Nudge(-1, 0));
        Bind(Key.ArrowRight, () => Nudge(1, 0));
        Bind(Key.ArrowUp, () => Nudge(0, -1));
        Bind(Key.ArrowDown, () => Nudge(0, 1));
        Bind(Key.Enter, Pick);
        _window.Deactivated += Deactivated;
        return null;
    }

    private void Bind(Key key, Action action) => _keys.Add(_window.Commands.Bind(
        new KeyChord(key),
        new Command("skin.palette.reference." + key, action, () => Picking && _focused),
        KeyScope.When(KeyContexts.Popup).InertWhenTyping()));

    private void Deactivated(object? sender, EventArgs args)
    {
        _hovered = false;
        CancelGesture();
        ClearSample();
    }

    protected override void OnDispose()
    {
        _window.Deactivated -= Deactivated;
        foreach (var binding in _keys)
            binding.Dispose();
        _keys.Clear();
        _cursor?.Dispose();
        _focus?.Dispose();
        base.OnDispose();
    }
}