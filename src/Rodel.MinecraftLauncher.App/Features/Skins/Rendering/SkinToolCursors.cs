// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Rasterizes DPI-sized tool silhouettes with the actual working end at the native cursor hotspot.</summary>
internal static class SkinToolCursors
{
    private readonly record struct Path(Vector2[] Points, bool Closed = false);
    private static readonly Dictionary<(int Tool, int Size), Cursor> Cache = [];
    private static readonly Lock Gate = new();

    internal static Cursor? For(int tool, DpiScale scale)
    {
        // The brush keeps the normal pointer; its projected footprint already describes the paint area.
        if (tool is < 1 or > 3)
            return null;
        var size = scale.ToDevice(new Size(40f, 40f)).Width;
        lock (Gate)
        {
            if (!Cache.TryGetValue((tool, size), out var cursor))
                Cache[(tool, size)] = cursor = Create(tool, size);
            return cursor;
        }
    }

    private static Cursor Create(int tool, int size)
    {
        // Geometry is authored on a 48-unit square. Each silhouette extends northeast from its working end,
        // leaving the sampled cell southwest of the tool unobscured. There is no detached crosshair.
        Path[] paths = tool switch
        {
            1 => [new([new(6, 41), new(6, 33), new(28, 11), new(41, 24), new(24, 41)], true), new([new(17, 22), new(32, 33)]),],
            2 => [new([new(18, 17), new(14, 13), new(14, 10), new(18, 6), new(23, 6), new(27, 10)]), new([new(13, 23), new(28, 8), new(42, 22), new(28, 36)], true), new([new(19, 24), new(36, 24)]), new([new(13, 23), new(10, 27), new(7, 30)]), new([new(7, 30), new(3, 36), new(3, 38), new(7, 42), new(11, 38), new(11, 36)], true),],
            _ => [new([new(6, 41), new(9, 31), new(29, 11), new(36, 18), new(16, 38)], true), new([new(30, 10), new(34, 6), new(41, 13), new(37, 17)], true), new([new(24, 10), new(27, 7), new(40, 20), new(37, 23)], true), new([new(12, 32), new(16, 36)]),],
        };
        var pixels = new byte[size * size * 4];
        const int samples = 4;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                float alpha = 0, luminance = 0;
                for (var sy = 0; sy < samples; sy++)
                {
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var point = new Vector2((x + ((sx + .5f) / samples)) * 48 / size, (y + ((sy + .5f) / samples)) * 48 / size);
                        float a = 0, shade = 0;
                        foreach (var path in paths)
                        {
                            var distance = Distance(point, path);
                            if (distance <= 2f)
                            {
                                a = 1;
                                shade = distance <= 1.15f ? .06f : .96f;
                            }
                            else if (path.Closed && Contains(point, path.Points))
                            {
                                a = 1;
                                shade = .96f;
                            }
                        }

                        alpha += a;
                        luminance += shade * a;
                    }
                }

                var offset = ((y * size) + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = alpha > 0 ? (byte)MathF.Round(luminance / alpha * 255) : (byte)0;
                pixels[offset + 3] = (byte)MathF.Round(alpha / (samples * samples) * 255);
            }
        }

        var hotspot = tool == 2 ? new Vector2(7, 42) : new Vector2(6, 41);
        return Cursor.FromCustom(CustomCursor.FromImage(
                pixels,
                size,
                size,
                (int)MathF.Round(hotspot.X * size / 48),
                (int)MathF.Round(hotspot.Y * size / 48)));
    }

    private static float Distance(Vector2 point, Path path)
    {
        var distance = float.MaxValue;
        var count = path.Closed ? path.Points.Length : path.Points.Length - 1;
        for (var index = 0; index < count; index++)
        {
            var a = path.Points[index];
            var b = path.Points[(index + 1) % path.Points.Length];
            var delta = b - a;
            var t = Math.Clamp(Vector2.Dot(point - a, delta) / delta.LengthSquared(), 0, 1);
            distance = Math.Min(distance, Vector2.Distance(point, a + (t * delta)));
        }

        return distance;
    }

    private static bool Contains(Vector2 point, Vector2[] polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (((a.Y > point.Y) != (b.Y > point.Y)) && (point.X < (((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)))
                inside = !inside;
        }

        return inside;
    }
}