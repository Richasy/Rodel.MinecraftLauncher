// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

internal sealed class SkinDrawing
{
    private readonly SkinDocument _document;
    private readonly SkinEdit _edit;
    private readonly SkinSurface _surface;
    private readonly SkinFaceInfo _face;
    private readonly SkinSelection _selection;
    private readonly uint[] _pixels;
    private readonly double _opacity;
    private readonly int _size;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<int, Mark> _marks = [];
    private readonly HashSet<(SkinSurface Surface, int X, int Y)> _centers = [];
    private uint[]? _strokeBaseline;
    private SkinStrokePoint? _previousPoint;
    private readonly HashSet<int> _dirtyMarks = [];
    private readonly record struct Mark(SkinSurface Surface, uint Color, double Coverage);

    internal SkinDrawing(SkinDocument document, SkinEdit edit, CancellationToken cancellationToken)
    {
        _document = document;
        _edit = edit;
        _cancellationToken = cancellationToken;
        _surface = edit.Surface ?? throw new ArgumentException("A drawing action requires a surface.", nameof(edit));
        _face = SkinLayout.Face(document.Model, _surface, document.Size);
        _selection = SkinSelections.Validate(_face, edit.Selection);
        if (edit.PixelMask is { } mask && (mask.Length != document.Size * document.Height
            || edit.Tool is SkinTool.Copy or SkinTool.Move or SkinTool.Cut or SkinTool.Paste or SkinTool.FlipHorizontal or SkinTool.FlipVertical))
        {
            throw new ArgumentException("Atlas write masks require Size*Size entries and a painting tool.", nameof(edit));
        }

        _pixels = document.GetLayerPixels(edit.PaintLayerId);
        _size = edit.BrushSize ?? 1;
        _opacity = edit.Opacity ?? 1;
        if (!Enum.IsDefined(edit.Tool) || !Enum.IsDefined(edit.BrushShape) || !Enum.IsDefined(edit.Mirror)
            || (edit.MirrorParts is { } parts && parts.Any(part => !Enum.IsDefined(part)))
            || !Enum.IsDefined(edit.FillMode) || !Enum.IsDefined(edit.GradientKind)
            || (edit.BlendMode is { } blend && !Enum.IsDefined(blend))
            || _size < 1 || _size > document.Size || edit.Tolerance is < 0 or > 255 || edit.Amount is < -255 or > 255
            || !double.IsFinite(edit.Hue) || edit.Hue is < -360 or > 360
            || !double.IsFinite(edit.Saturation) || edit.Saturation is < -1 or > 1
            || !double.IsFinite(edit.Brightness) || edit.Brightness is < -1 or > 1
            || !double.IsFinite(edit.Contrast) || edit.Contrast is < -1 or > 1)
        {
            throw new ArgumentException("Invalid drawing tool, brush, color adjustment or tolerance.", nameof(edit));
        }

        SkinColor.CheckUnit(_opacity, nameof(edit.Opacity));
        SkinColor.CheckUnit(edit.Softness, nameof(edit.Softness));
        if (edit.TransparencyMask && (edit.Tool is not (SkinTool.Pencil or SkinTool.Stroke or SkinTool.Eraser)
            || edit.PaintLayerId is not null || edit.AtlasTransfer is not null || _opacity != 1 || edit.Softness != 0 || edit.LockAlpha || edit.BlendMode is not null))
        {
            throw new ArgumentException("Transparency mask edits require an unlayered, hard brush or eraser at full opacity.", nameof(edit));
        }

        if (edit.Tool == SkinTool.Eraser && edit.LockAlpha)
            throw new ArgumentException("Unlock alpha before using the eraser.", nameof(edit));
    }

    internal void Draw(bool footprintOnly = false)
    {
        var tool = _edit.Tool;
        var color = !_edit.TransparencyMask && tool is (SkinTool.Pencil or SkinTool.Stroke or SkinTool.Line or SkinTool.Rectangle or SkinTool.Ellipse
            or SkinTool.Fill or SkinTool.Replace or SkinTool.Gradient)
            ? SkinColor.Parse(_edit.Color ?? throw new ArgumentException("This drawing tool requires a color."))
            : 0;
        if (tool is SkinTool.Pencil or SkinTool.Eraser or SkinTool.Fill or SkinTool.Line or SkinTool.Rectangle or SkinTool.Ellipse or SkinTool.Gradient)
            SkinSelections.CheckPoint(_face, _edit.X, _edit.Y);
        if (tool is SkinTool.Line or SkinTool.Rectangle or SkinTool.Ellipse or SkinTool.Gradient)
        {
            if (_edit.X2 is null || _edit.Y2 is null)
                throw new ArgumentException("Lines, shapes and gradients require x2 and y2.");
            SkinSelections.CheckPoint(_face, _edit.X2.Value, _edit.Y2.Value);
        }

        switch (tool)
        {
            case SkinTool.Pencil:
            case SkinTool.Eraser:
                if (_edit.Points is not null)
                    Stroke(color);
                else
                    Stamp(_surface, _edit.X, _edit.Y, color);
                break;
            case SkinTool.Line:
                Line(_surface, _edit.X, _edit.Y, _edit.X2!.Value, _edit.Y2!.Value, color);
                break;
            case SkinTool.Stroke:
                Stroke(color);
                break;
            case SkinTool.Rectangle:
            case SkinTool.Ellipse:
                Shape(color);
                break;
            case SkinTool.Fill:
                Fill(color);
                break;
            case SkinTool.Replace:
                var matching = SkinColor.Parse(_edit.MatchColor ?? throw new ArgumentException("Replace requires matchColor."));
                var sample = _edit.SampleMerged ? SkinLayers.Pixels(_document) : _pixels;
                ForFace(_surface, (x, y) =>
                {
                    if (SkinColor.Matches(sample[Index(_face, x, y)], matching, _edit.Tolerance))
                        Queue(_surface, x, y, color, 1);
                });
                break;
            case SkinTool.Shade:
            case SkinTool.AdjustColor:
                ForFace(_surface, (x, y) => Queue(_surface, x, y, 0, 1));
                break;
            case SkinTool.Gradient:
                Gradient(color);
                break;
            case SkinTool.Copy:
            case SkinTool.Move:
            case SkinTool.Cut:
            case SkinTool.Paste:
            case SkinTool.FlipHorizontal:
            case SkinTool.FlipVertical:
                Blit();
                break;
            default:
                throw new ArgumentException("Unsupported drawing operation.");
        }

        if (!footprintOnly)
            Flush();
    }

    internal SkinBrushMark[] Footprint()
    {
        if (_edit.Tool is not (SkinTool.Pencil or SkinTool.Eraser or SkinTool.Stroke or SkinTool.Fill))
            throw new ArgumentException("Footprint requires pencil, eraser, stroke or fill.");
        Draw(footprintOnly: true);
        return [.. _marks.Select(pair =>
        {
            var face = SkinLayout.Face(_document.Model, pair.Value.Surface, _document.Size);
            return new SkinBrushMark(pair.Value.Surface, (pair.Key % _document.Size) - face.X,
                (pair.Key / _document.Size) - face.Y, pair.Value.Coverage * _opacity);
        })];
    }

    private void Stroke(uint color)
    {
        var points = _edit.Points ?? throw new ArgumentException("Stroke requires points.");
        if (points.Length is < 1 or > 4096)
            throw new ArgumentException("A stroke requires 1 through 4096 sampled points.");
        SkinStrokePoint? previous = null;
        foreach (var point in points)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(point);
            var surface = point.Surface ?? _surface;
            SkinSelections.CheckPoint(SkinLayout.Face(_document.Model, surface, _document.Size), point.X, point.Y);
            if (_edit.Selection is not null && surface != _surface)
                throw new ArgumentException("A selected stroke must stay on the selection's face.");
            if (previous is not null && !point.BreakBefore)
            {
                var from = previous.Surface ?? _surface;
                var unfolded = from == surface ? new SkinPoint(point.X, point.Y)
                    : _edit.WrapFaces ? SkinGeometry.Unfold(_document.Model, from, surface, point.X, point.Y, _document.Size) : null;
                if (unfolded is not null)
                    Line(from, previous.X, previous.Y, unfolded.X, unfolded.Y, color);
            }

            Stamp(surface, point.X, point.Y, color);
            previous = point;
        }
    }

    internal void Append(SkinStrokePoint point)
    {
        var surface = point.Surface ?? _surface;
        SkinSelections.CheckPoint(SkinLayout.Face(_document.Model, surface, _document.Size), point.X, point.Y);
        if (_edit.Selection is not null && surface != _surface)
            throw new ArgumentException("A selected stroke must stay on the selection's face.");
        if (!_edit.TransparencyMask)
            _strokeBaseline ??= (uint[])_pixels.Clone();
        var color = _edit.TransparencyMask || _edit.Tool == SkinTool.Eraser ? 0 : SkinColor.Parse(_edit.Color!);
        if (_previousPoint is { } previous && !point.BreakBefore)
        {
            var from = previous.Surface ?? _surface;
            var unfolded = from == surface ? new SkinPoint(point.X, point.Y)
                : _edit.WrapFaces ? SkinGeometry.Unfold(_document.Model, from, surface, point.X, point.Y, _document.Size) : null;
            if (unfolded is not null)
                Line(from, previous.X, previous.Y, unfolded.X, unfolded.Y, color);
        }

        Stamp(surface, point.X, point.Y, color);
        _previousPoint = point;
        Flush();
    }

    private void Line(SkinSurface surface, double x1, double y1, double x2, double y2, uint color)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) * 2));
        for (var step = 0; step <= steps; step++)
            Stamp(surface, x1 + ((x2 - x1) * step / steps), y1 + ((y2 - y1) * step / steps), color);
    }

    private void Stamp(SkinSurface surface, double x, double y, uint color)
    {
        var centerX = (int)Math.Floor(x + 0.5);
        var centerY = (int)Math.Floor(y + 0.5);
        if (!_centers.Add((surface, centerX, centerY)))
            return;
        var even = _size % 2 == 0 ? 0.5 : 0;
        var cx = centerX + even;
        var cy = centerY + even;
        var radius = _size / 2.0;
        for (var py = (int)Math.Floor(cy - radius) + 1; py < Math.Ceiling(cy + radius); py++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var px = (int)Math.Floor(cx - radius) + 1; px < Math.Ceiling(cx + radius); px++)
            {
                var dx = Math.Abs(px - cx);
                var dy = Math.Abs(py - cy);
                var distance = _edit.BrushShape == SkinBrushShape.Circle ? Math.Sqrt((dx * dx) + (dy * dy)) : Math.Max(dx, dy);
                if (distance >= radius)
                    continue;
                var inner = radius * (1 - _edit.Softness);
                var coverage = distance <= inner ? 1 : (radius - distance) / (radius - inner);
                Queue(surface, px, py, color, coverage);
            }
        }
    }

    private void Shape(uint color)
    {
        var left = Math.Min(_edit.X, _edit.X2!.Value);
        var top = Math.Min(_edit.Y, _edit.Y2!.Value);
        var right = Math.Max(_edit.X, _edit.X2.Value);
        var bottom = Math.Max(_edit.Y, _edit.Y2.Value);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                if (Inside(x, y) && (_edit.Filled || edge))
                    Stamp(_surface, x, y, color);
            }
        }

        return;

        bool Inside(int x, int y)
        {
            if (x < left || x > right || y < top || y > bottom)
                return false;
            return _edit.Tool == SkinTool.Rectangle
                || Math.Pow(((2.0 * x) - left - right) / (right - left + 1), 2)
                    + Math.Pow(((2.0 * y) - top - bottom) / (bottom - top + 1), 2) <= 1;
        }
    }

    private void Fill(uint color)
    {
        var mode = _edit.FillMode;
        var sample = _edit.SampleMerged ? SkinLayers.Pixels(_document) : _pixels;
        var matching = sample[Index(_face, _edit.X, _edit.Y)];
        if (mode == SkinFillMode.Connected)
        {
            var visited = new HashSet<int>();
            var pending = new Queue<(SkinSurface Surface, int X, int Y)>();
            pending.Enqueue((_surface, _edit.X, _edit.Y));
            while (pending.TryDequeue(out var point))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var resolved = Resolve(point.Surface, point.X, point.Y);
                if (resolved is not { } value || !Selected(value.Surface, value.X, value.Y))
                    continue;
                var face = SkinLayout.Face(_document.Model, value.Surface, _document.Size);
                var index = Index(face, value.X, value.Y);
                if (!visited.Add(index) || !SkinColor.Matches(sample[index], matching, _edit.Tolerance))
                    continue;
                Queue(value.Surface, value.X, value.Y, color, 1);
                pending.Enqueue((value.Surface, value.X - 1, value.Y));
                pending.Enqueue((value.Surface, value.X + 1, value.Y));
                pending.Enqueue((value.Surface, value.X, value.Y - 1));
                pending.Enqueue((value.Surface, value.X, value.Y + 1));
            }

            return;
        }

        var faces = SkinLayout.Faces(_document.Model, _document.Size).Where(face => face.Surface.Layer == _surface.Layer
            && (mode == SkinFillMode.Layer || (mode == SkinFillMode.Part && face.Surface.Part == _surface.Part) || face.Surface == _surface));
        foreach (var face in faces)
        {
            ForFace(face.Surface, (x, y) =>
            {
                if (mode != SkinFillMode.Matching || SkinColor.Matches(sample[Index(face, x, y)], matching, _edit.Tolerance))
                    Queue(face.Surface, x, y, color, 1);
            });
        }
    }

    private void Gradient(uint first)
    {
        var second = SkinColor.Parse(_edit.SecondaryColor ?? throw new ArgumentException("Gradient requires secondaryColor."));
        var dx = _edit.X2!.Value - _edit.X;
        var dy = _edit.Y2!.Value - _edit.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0)
            throw new ArgumentException("Gradient endpoints must differ.");
        ForFace(_surface, (x, y) =>
        {
            var px = x - _edit.X;
            var py = y - _edit.Y;
            var amount = _edit.GradientKind == SkinGradientKind.Radial
                ? Math.Sqrt(((px * px) + (py * py)) / (double)lengthSquared)
                : ((px * dx) + (py * dy)) / (double)lengthSquared;
            Queue(_surface, x, y, SkinColor.Gradient(first, second, Math.Clamp(amount, 0, 1)), 1);
        });
    }

    private void Blit()
    {
        var plan = SkinTransfers.Plan(_document, _edit, _cancellationToken);
        foreach (var pixel in plan.Clears)
            Store(Index(SkinLayout.Face(_document.Model, pixel.Surface, _document.Size), pixel.X, pixel.Y), pixel.Surface, pixel.Color);
        foreach (var pixel in plan.Writes)
            Add(_marks, pixel.Surface, pixel.X, pixel.Y, pixel.Color, 1);
    }

    private void Queue(SkinSurface surface, int x, int y, uint color, double coverage)
    {
        var resolved = Resolve(surface, x, y);
        if (resolved is not { } point || !Selected(point.Surface, point.X, point.Y))
            return;
        Add(_marks, point.Surface, point.X, point.Y, color, coverage);
        if (_edit.Mirror != SkinMirrorMode.None)
        {
            var mirrored = SkinGeometry.MirrorPoint(_document.Model, point.Surface, point.X, point.Y, _edit.Mirror, _edit.MirrorParts, _document.Size);
            Add(_marks, mirrored.Surface, mirrored.X, mirrored.Y, color, coverage);
        }
    }

    private void Add(Dictionary<int, Mark> marks, SkinSurface surface, int x, int y, uint color, double coverage)
    {
        var index = Index(SkinLayout.Face(_document.Model, surface, _document.Size), x, y);
        if (_edit.PixelMask is { } mask && !mask[index]) return;
        if (!marks.TryGetValue(index, out var previous) || coverage > previous.Coverage)
        {
            marks[index] = new(surface, color, coverage);
            _dirtyMarks.Add(index);
        }
    }

    private (SkinSurface Surface, int X, int Y)? Resolve(SkinSurface surface, int x, int y)
    {
        var face = SkinLayout.Face(_document.Model, surface, _document.Size);
        if (x >= 0 && y >= 0 && x < face.Width && y < face.Height)
            return (surface, x, y);
        return _edit.WrapFaces ? SkinGeometry.Wrap(_document.Model, surface, x, y, _document.Size) : null;
    }

    private bool Selected(SkinSurface surface, int x, int y) =>
        (_edit.PixelMask is null || _edit.PixelMask[Index(SkinLayout.Face(_document.Model, surface, _document.Size), x, y)])
        && (_edit.Selection is null || (surface == _surface && SkinSelections.Contains(_selection, x, y)));

    private void ForFace(SkinSurface surface, Action<int, int> action)
    {
        var face = SkinLayout.Face(_document.Model, surface, _document.Size);
        for (var y = 0; y < face.Height; y++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < face.Width; x++)
            {
                if (Selected(surface, x, y))
                    action(x, y);
            }
        }
    }

    private void Flush()
    {
        if (_edit.TransparencyMask)
        {
            foreach (var index in _dirtyMarks)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                SkinTransparency.Set(_document.TransparencyMask!, index, _edit.Tool != SkinTool.Eraser);
            }

            _dirtyMarks.Clear();
            return;
        }

        var blit = _edit.Tool is SkinTool.Copy or SkinTool.Move or SkinTool.Paste or SkinTool.FlipHorizontal or SkinTool.FlipVertical;
        var blend = _edit.BlendMode ?? (blit || _edit.Tool == SkinTool.Replace ? SkinBlendMode.Replace : SkinBlendMode.Normal);
        foreach (var index in _strokeBaseline is null ? _marks.Keys : (IEnumerable<int>)_dirtyMarks)
        {
            var mark = _marks[index];
            _cancellationToken.ThrowIfCancellationRequested();
            var coverage = mark.Coverage * _opacity;
            if (coverage == 0)
                continue;
            var before = (_strokeBaseline ?? _pixels)[index];
            uint after;
            if (_edit.Tool == SkinTool.Eraser)
            {
                if (Background && mark.Surface.Layer == SkinLayer.Base)
                    throw new ArgumentException("The background's Java base surface cannot be erased; use an opaque replacement color.");
                var alpha = SkinColor.Byte((before & 255) * (1 - coverage));
                after = alpha == 0 ? 0 : (before & 0xFFFFFF00) | (uint)alpha;
            }
            else if (_edit.Tool is SkinTool.Shade or SkinTool.AdjustColor)
            {
                if ((before & 255) == 0)
                    continue;
                uint Shift(int shift) => (uint)Math.Clamp((int)((before >> shift) & 255) + _edit.Amount, 0, 255) << shift;
                var adjusted = _edit.Tool == SkinTool.Shade ? Shift(24) | Shift(16) | Shift(8) | (before & 255)
                    : SkinColor.Adjust(before, _edit.Hue, _edit.Saturation, _edit.Brightness, _edit.Contrast);
                after = SkinColor.Replace(before, adjusted, coverage);
            }
            else if (_edit.LockAlpha)
            {
                if ((before & 255) == 0)
                    continue;
                after = SkinColor.Replace(before, (mark.Color & 0xFFFFFF00) | (before & 255), coverage * (mark.Color & 255) / 255.0);
            }
            else
            {
                after = blend == SkinBlendMode.Replace ? SkinColor.Replace(before, mark.Color, coverage) : SkinColor.Composite(before, mark.Color, coverage);
            }

            Store(index, mark.Surface, after);
        }

        _dirtyMarks.Clear();
    }

    private bool Background => _document.Model != SkinModel.Cape && _edit.PaintLayerId is null or SkinDocument.BackgroundLayerId;

    private void Store(int index, SkinSurface surface, uint color)
    {
        if (Background && surface.Layer == SkinLayer.Base && (color & 255) != 255)
            throw new ArgumentException("Clearing the background's Java base needs an opaque clearColor; paint layers can remain transparent.");
        _pixels[index] = color;
    }

    private int Index(SkinFaceInfo face, int x, int y) => ((face.Y + y) * _document.Size) + face.X + x;
}