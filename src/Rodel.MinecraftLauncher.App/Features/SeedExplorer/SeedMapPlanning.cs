// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Describes the world extent of a map viewport without depending on the native widget tree.</summary>
/// <param name="X">The horizontal world coordinate at the viewport center.</param>
/// <param name="Z">The world depth coordinate at the viewport center.</param>
/// <param name="BlocksPerPixel">The world distance represented by one logical viewport pixel.</param>
/// <param name="Width">The viewport width in logical pixels.</param>
/// <param name="Height">The viewport height in logical pixels.</param>
internal readonly record struct SeedMapViewport(double X, double Z, double BlocksPerPixel, float Width, float Height);

/// <summary>Anchors a map place or projected scene pin to screen coordinates.</summary>
/// <param name="Id">The stable identity used to retain the marker's native control.</param>
/// <param name="X">The projected horizontal world anchor before the graphic offset.</param>
/// <param name="Y">The projected vertical world anchor before the graphic offset.</param>
/// <param name="Positions">One real position for map pins; empty for scene pins whose container is owned by the scene.</param>
/// <param name="Size">The logical size of this marker's visible button and hit bounds.</param>
/// <param name="OffsetX">The graphic's horizontal offset from its real world anchor.</param>
/// <param name="OffsetY">The graphic's vertical offset from its real world anchor.</param>
internal sealed record SeedMarker(string Id, float X, float Y, SeedPosition[] Positions, float Size = 40f, float OffsetX = 0f, float OffsetY = 0f);

/// <summary>Relates a scale label's world distance to the actual length drawn on the map.</summary>
/// <param name="Blocks">The labeled world distance in blocks.</param>
/// <param name="Pixels">The matching length in logical map pixels.</param>
internal readonly record struct SeedMapScaleMetrics(long Blocks, float Pixels);

/// <summary>Plans a bounded preview, one final raster level and independent original-structure coverage.</summary>
internal static class SeedMapPlanning
{
    internal static double ClampScale(double scale, float width, float height) => Math.Clamp(scale, SeedValidation.MinimumViewportScale,
        Math.Max(SeedValidation.MinimumViewportScale, Math.Min(SeedValidation.MaximumViewportScale, 59_000_000d / Math.Max(1f, Math.Max(width, height)))));

    internal static SeedMapViewport Normalize(SeedMapViewport view)
    {
        var scale = ClampScale(view.BlocksPerPixel, view.Width, view.Height);
        var halfWidth = view.Width * scale / 2;
        var halfHeight = view.Height * scale / 2;
        return view with
        {
            BlocksPerPixel = scale,
            X = Math.Clamp(view.X, -30_000_000 + halfWidth, 30_000_000 - halfWidth),
            Z = Math.Clamp(view.Z, -30_000_000 + halfHeight, 30_000_000 - halfHeight),
        };
    }

    internal static SeedMapScaleMetrics Scale(double blocksPerPixel)
    {
        var target = blocksPerPixel * 80;
        var unit = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var factor = target / unit;
        var distance = (long)((factor >= 5 ? 5 : factor >= 2 ? 2 : 1) * unit);
        return new(distance, (float)(distance / blocksPerPixel));
    }

    internal static bool Overview(SeedMapViewport view) => view.BlocksPerPixel >= 32;

    internal static SeedMarker[] Points(IEnumerable<SeedPosition> positions, SeedMapViewport view, Func<SeedPosition, int>? priority = null)
    {
        var markers = new List<SeedMarker>();
        var occupied = new Dictionary<(int X, int Y), List<(float X, float Y, float Radius)>>();
        // Priority and world identity, rather than arrival order or viewport distance, keep collision decisions stable.
        var candidates = positions.DistinctBy(position => (position.Kind, position.Id, position.X, position.Z))
            .Select(position => (Position: position, Priority: position.Kind is "origin" or "spawn" ? Math.Min(0, priority?.Invoke(position) ?? 0) : priority?.Invoke(position) ?? 4))
            .OrderBy(item => item.Position.Kind is "origin" or "spawn" ? 0 : 1).ThenBy(item => item.Priority)
            .ThenBy(item => item.Position.Kind, StringComparer.Ordinal).ThenBy(item => item.Position.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Position.X).ThenBy(item => item.Position.Z);
        foreach (var (position, rank) in candidates)
        {
            if (Overview(view) && rank >= 4) continue;
            var permanent = position.Kind is "origin" or "spawn";
            var size = position.Kind == "origin" ? 20f : position.Kind == "spawn" ? 32f
                : (float)Math.Clamp(48 - (8 * Math.Log2(view.BlocksPerPixel)), 24, 56);
            var radius = (size / 2) + 3f;
            var x = (float)(((position.X - view.X) / view.BlocksPerPixel) + (view.Width / 2));
            var y = (float)(((position.Z - view.Z) / view.BlocksPerPixel) + (view.Height / 2));
            if (permanent ? x < 0 || y < 0 || x > view.Width || y > view.Height : x < radius || y < radius || x > view.Width - radius || y > view.Height - radius) continue;
            // The flag pole's foot (5,22 in the 24-unit symbol) stays on the actual coordinate. Its flag is above
            // the small origin compass, so both remain identifiable and independently clickable even at one point.
            var offsetX = position.Kind == "spawn" ? (size - 4f) * (12f - 5f) / 24f : 0f;
            var offsetY = position.Kind == "spawn" ? (size - 4f) * (12f - 22f) / 24f : 0f;
            var centerX = x + offsetX;
            var centerY = y + offsetY;
            var west = (int)Math.Floor((centerX - radius) / 32);
            var north = (int)Math.Floor((centerY - radius) / 32);
            var east = (int)Math.Floor((centerX + radius) / 32);
            var south = (int)Math.Floor((centerY + radius) / 32);
            var collision = false;
            for (var row = north; row <= south && !collision && !permanent; row++)
            {
                for (var column = west; column <= east && !collision; column++)
                {
                    if (occupied.TryGetValue((column, row), out var neighbors))
                        collision = neighbors.Any(other => Math.Abs(centerX - other.X) < radius + other.Radius && Math.Abs(centerY - other.Y) < radius + other.Radius);
                }
            }

            if (collision) continue;
            for (var row = north; row <= south; row++)
            {
                for (var column = west; column <= east; column++)
                {
                    if (!occupied.TryGetValue((column, row), out var neighbors)) occupied[(column, row)] = neighbors = [];
                    neighbors.Add((centerX, centerY, radius));
                }
            }

            markers.Add(new($"{position.Kind}.{position.Id}.{position.X}.{position.Z}", x, y, [position], size, offsetX, offsetY));
        }

        return [.. markers.OrderBy(marker => marker.Positions[0].Kind == "origin" ? 2 : marker.Positions[0].Kind == "spawn" ? 1 : 0)];
    }

    internal static int Step(SeedMapViewport view) => (int)Math.Clamp(
        Math.Pow(2, Math.Ceiling(Math.Log2(view.BlocksPerPixel * (view.BlocksPerPixel <= 1
            ? Math.Max(1, Math.Max(view.Width / 1024, view.Height / 640))
            : Math.Max(4, Math.Max(view.Width / 256, view.Height / 160)))))), 1, 524_288);

    internal static SeedMapQuery[] Raster(SeedMapQuery template, SeedMapViewport view)
    {
        var fine = Step(view);
        var result = new List<SeedMapQuery>();
        foreach (var (step, side) in new[] { (Math.Min(524_288, fine * 8), 16), (fine, 64) })
        {
            var span = step * side;
            var offset = side == 16 ? span / 2 : 0;
            var west = view.X - (view.Width * view.BlocksPerPixel / 2);
            var north = view.Z - (view.Height * view.BlocksPerPixel / 2);
            var east = view.X + (view.Width * view.BlocksPerPixel / 2);
            var south = view.Z + (view.Height * view.BlocksPerPixel / 2);
            var level = new List<SeedMapQuery>();
            for (var z = (Math.Floor((north + offset) / span) * span) - offset; z < south; z += span)
            {
                for (var x = (Math.Floor((west + offset) / span) * span) - offset; x < east; x += span)
                {
                    level.Add(template with
                    {
                        MinX = (int)Math.Clamp(x, -30_000_000, 30_000_000 - span),
                        MinZ = (int)Math.Clamp(z, -30_000_000, 30_000_000 - span),
                        Side = side,
                        Step = step,
                        Structures = [],
                        Decorated = template.Decorated && template.Y is null && template.Terrain && side == 64 && step * side <= 128,
                    });
                }
            }

            result.AddRange(level.DistinctBy(Key).OrderBy(query => Distance(query, view)));
        }

        return [.. result.DistinctBy(Key)];
    }

    internal static SeedMapQuery[] Markers(SeedMapQuery template, SeedMapViewport view, out bool limited)
    {
        const int span = 1024;
        limited = false;
        if (Overview(view)) return [];
        var width = view.Width * view.BlocksPerPixel;
        var height = view.Height * view.BlocksPerPixel;
        var minimum = (int)Math.Floor(-30_000_000d / span);
        var maximum = (int)Math.Floor(29_999_999d / span);
        var west = Math.Clamp((int)Math.Floor((view.X - (width / 2)) / span), minimum, maximum);
        var north = Math.Clamp((int)Math.Floor((view.Z - (height / 2)) / span), minimum, maximum);
        var east = Math.Clamp((int)Math.Ceiling((view.X + (width / 2)) / span) - 1, west, maximum);
        var south = Math.Clamp((int)Math.Ceiling((view.Z + (height / 2)) / span) - 1, north, maximum);
        limited = east - west >= 8 || south - north >= 8;
        // A connected, at most 8x8 region footprint replaces nine isolated overview samples.
        if (east - west >= 8) { west = Math.Clamp((int)Math.Floor(view.X / span) - 4, west, east - 7); east = west + 7; }

        if (south - north >= 8) { north = Math.Clamp((int)Math.Floor(view.Z / span) - 4, north, south - 7); south = north + 7; }

        var result = new List<SeedMapQuery>();
        for (var z = north; z <= south; z++)
        {
            for (var x = west; x <= east; x++)
            {
                result.Add(template with { MinX = Math.Clamp(x * span, -30_000_000, 30_000_000 - span), MinZ = Math.Clamp(z * span, -30_000_000, 30_000_000 - span), Side = 1, Step = span, Y = 64, Terrain = false, Decorated = false, Structures = null });
            }
        }

        return [.. result.DistinctBy(Key).OrderBy(query => Distance(query, view)).ThenBy(query => query.MinX).ThenBy(query => query.MinZ)];
    }

    internal static bool Visible(SeedMapTile tile, SeedMapViewport view)
    {
        var halfWidth = view.Width * view.BlocksPerPixel / 2;
        var halfHeight = view.Height * view.BlocksPerPixel / 2;
        var span = tile.Side * tile.Step;
        return tile.MinX < view.X + halfWidth && tile.MinX + span > view.X - halfWidth
            && tile.MinZ < view.Z + halfHeight && tile.MinZ + span > view.Z - halfHeight;
    }

    internal static string Key(SeedMapQuery query) => $"{query.MinX}:{query.MinZ}:{query.Side}:{query.Step}:{query.Terrain}:{query.Decorated}:{(query.Structures is { Length: 0 } ? 0 : 1)}";
    private static double Distance(SeedMapQuery query, SeedMapViewport view) => Math.Pow(query.MinX + (query.Side * query.Step / 2) - view.X, 2)
        + Math.Pow(query.MinZ + (query.Side * query.Step / 2) - view.Z, 2);
}