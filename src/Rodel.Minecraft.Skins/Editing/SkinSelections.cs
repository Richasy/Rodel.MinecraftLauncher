// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Creates and evaluates editor selections independently of pointer or UI state.</summary>
public static class SkinSelections
{
    /// <summary>Builds a geometric selection or a color mask from one document revision.</summary>
    public static SkinSelection Create(SkinDocument document, SkinSelectionRequest request)
    {
        document.Validate();
        if (!Enum.IsDefined(request.Kind) || request.Tolerance is < 0 or > 255)
            throw new ArgumentException("Invalid selection kind or color tolerance.", nameof(request));
        var face = SkinLayout.Face(document.Model, request.Surface, document.Size);
        SkinSelection selection;
        if (request.Kind is SkinSelectionKind.Matching or SkinSelectionKind.Connected)
        {
            CheckPoint(face, request.X, request.Y);
            var pixels = SkinCompositor.Pixels(document, request.PaintLayerId);
            var reference = pixels[((face.Y + request.Y) * document.Size) + face.X + request.X];
            var mask = new bool[face.Width * face.Height];
            if (request.Kind == SkinSelectionKind.Matching)
            {
                for (var y = 0; y < face.Height; y++)
                {
                    for (var x = 0; x < face.Width; x++)
                        mask[(y * face.Width) + x] = Matches(x, y);
                }
            }
            else
            {
                var visited = new bool[mask.Length];
                var pending = new Queue<(int X, int Y)>();
                pending.Enqueue((request.X, request.Y));
                while (pending.TryDequeue(out var point))
                {
                    if (point.X < 0 || point.Y < 0 || point.X >= face.Width || point.Y >= face.Height)
                        continue;
                    var index = (point.Y * face.Width) + point.X;
                    if (visited[index])
                        continue;
                    visited[index] = true;
                    if (!Matches(point.X, point.Y))
                        continue;
                    mask[index] = true;
                    pending.Enqueue((point.X - 1, point.Y));
                    pending.Enqueue((point.X + 1, point.Y));
                    pending.Enqueue((point.X, point.Y - 1));
                    pending.Enqueue((point.X, point.Y + 1));
                }
            }

            selection = new(0, 0, face.Width, face.Height) { Shape = SkinSelectionShape.Mask, Mask = mask, Inverted = request.Inverted };

            bool Matches(int x, int y) => SkinColor.Matches(pixels[((face.Y + y) * document.Size) + face.X + x], reference, request.Tolerance);
        }
        else
        {
            var shape = request.Kind switch
            {
                SkinSelectionKind.Rectangle => SkinSelectionShape.Rectangle,
                SkinSelectionKind.Ellipse => SkinSelectionShape.Ellipse,
                _ => SkinSelectionShape.Polygon
            };
            var remainingWidth = face.Width - request.X;
            var remainingHeight = face.Height - request.Y;
            selection = new(request.X, request.Y, request.Width ?? remainingWidth, request.Height ?? remainingHeight)
            { Shape = shape, Points = request.Points, Inverted = request.Inverted };
        }

        Validate(face, selection);
        return selection;
    }

    /// <summary>Tests a face-local pixel center against an already validated selection.</summary>
    public static bool Contains(SkinSelection selection, int x, int y)
    {
        var localX = x - selection.X;
        var localY = y - selection.Y;
        var inside = localX >= 0 && localY >= 0 && localX < selection.Width && localY < selection.Height;
        if (inside)
        {
            inside = selection.Shape switch
            {
                SkinSelectionShape.Rectangle => true,
                SkinSelectionShape.Ellipse => Math.Pow(((localX + 0.5) * 2 / selection.Width) - 1, 2)
                    + Math.Pow(((localY + 0.5) * 2 / selection.Height) - 1, 2) <= 1,
                SkinSelectionShape.Mask => selection.Mask![(localY * selection.Width) + localX],
                SkinSelectionShape.Polygon => PolygonContains(selection.Points!, localX + 0.5, localY + 0.5),
                _ => throw new ArgumentException("Unknown selection shape.", nameof(selection))
            };
        }

        return inside != selection.Inverted;
    }

    internal static SkinSelection Validate(SkinFaceInfo face, SkinSelection? selection)
    {
        var area = selection ?? new(0, 0, face.Width, face.Height);
        if (!Enum.IsDefined(area.Shape) || area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1
            || area.Width > face.Width - area.X || area.Height > face.Height - area.Y)
        {
            throw new ArgumentException("Selection bounds must fit inside the addressed face.", nameof(selection));
        }

        if (area.Shape == SkinSelectionShape.Mask && (area.Mask is null || area.Mask.Length != area.Width * area.Height))
            throw new ArgumentException("A selection mask must have width*height entries.", nameof(selection));
        if (area.Shape == SkinSelectionShape.Polygon && (area.Points is null || area.Points.Length < 3
            || area.Points.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)
                || point.X < 0 || point.X > area.Width || point.Y < 0 || point.Y > area.Height)))
        {
            throw new ArgumentException("A polygon needs at least three finite vertices inside its bounding box.", nameof(selection));
        }

        return area;
    }

    internal static void CheckPoint(SkinFaceInfo face, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < -0.5 || y < -0.5 || x >= face.Width - 0.5 || y >= face.Height - 0.5)
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must lie within the addressed face.");
    }

    private static bool PolygonContains(SkinPoint[] points, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < points.Length; i++)
        {
            var first = points[i];
            var second = points[(i + 1) % points.Length];
            if ((first.Y > y) != (second.Y > y)
                && x < first.X + ((y - first.Y) * (second.X - first.X) / (second.Y - first.Y)))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}