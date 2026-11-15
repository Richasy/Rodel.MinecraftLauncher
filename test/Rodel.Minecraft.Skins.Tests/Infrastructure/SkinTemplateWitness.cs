// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Defines independent physical bounds and explicit standard-template rectangles; never reads production Quads or Face mappings.</summary>
internal static class SkinTemplateWitness
{
    /// <summary>Pairs a physical input ray with independently specified face and atlas expectations.</summary>
    /// <param name="Surface">The expected model face.</param>
    /// <param name="X">The expected face-local horizontal texel.</param>
    /// <param name="Y">The expected face-local vertical texel.</param>
    /// <param name="AtlasX">The expected PNG column.</param>
    /// <param name="AtlasY">The expected PNG row.</param>
    /// <param name="Origin">The physical ray origin.</param>
    /// <param name="Direction">The inward ray direction.</param>
    /// <param name="Color">The unique RGBA mark to export.</param>
    internal sealed record Pixel(SkinSurface Surface, int X, int Y, int AtlasX, int AtlasY, SkinVector Origin, SkinVector Direction, uint Color);

    internal static IEnumerable<Pixel> Pixels(SkinModel model, int size = 64)
    {
        var serial = 1u;
        foreach (var part in Enum.GetValues<SkinPart>().Where(part => part != SkinPart.Cape))
        {
            var slim = model == SkinModel.Slim;
            var armWidth = slim ? 3 : 4;
            var (minimum, maximum) = part switch
            {
                SkinPart.Head => (new Vector3(-4, 24, -4), new Vector3(4, 32, 4)),
                SkinPart.Body => (new Vector3(-4, 12, -2), new Vector3(4, 24, 2)),
                SkinPart.RightArm => (new Vector3(-4 - armWidth, 12, -2), new Vector3(-4, 24, 2)),
                SkinPart.LeftArm => (new Vector3(4, 12, -2), new Vector3(4 + armWidth, 24, 2)),
                SkinPart.RightLeg => (new Vector3(-4, 0, -2), new Vector3(0, 12, 2)),
                _ => (new Vector3(0, 0, -2), new Vector3(4, 12, 2))
            };
            var width = part is SkinPart.Head or SkinPart.Body ? 8 : part is SkinPart.LeftArm or SkinPart.RightArm ? armWidth : 4;
            var height = part == SkinPart.Head ? 8 : 12;
            var depth = part == SkinPart.Head ? 8 : 4;
            // Entries are Front, Back, Right, Left, Top, Bottom in the published 64x64 cube-net template.
            (int X, int Y)[] origins = part switch
            {
                SkinPart.Head => [(8, 8), (24, 8), (0, 8), (16, 8), (8, 0), (16, 0)],
                SkinPart.Body => [(20, 20), (32, 20), (16, 20), (28, 20), (20, 16), (28, 16)],
                SkinPart.RightArm => [(44, 20), (slim ? 51 : 52, 20), (40, 20), (slim ? 47 : 48, 20), (44, 16), (slim ? 47 : 48, 16)],
                SkinPart.LeftArm => [(36, 52), (slim ? 43 : 44, 52), (32, 52), (slim ? 39 : 40, 52), (36, 48), (slim ? 39 : 40, 48)],
                SkinPart.RightLeg => [(4, 20), (12, 20), (0, 20), (8, 20), (4, 16), (8, 16)],
                _ => [(20, 52), (28, 52), (16, 52), (24, 52), (20, 48), (24, 48)]
            };
            foreach (var layer in Enum.GetValues<SkinLayer>())
            {
                var outer = layer == SkinLayer.Outer;
                var inflation = outer ? part == SkinPart.Head ? 0.5f : 0.25f : 0;
                var min = minimum - new Vector3(inflation);
                var max = maximum + new Vector3(inflation);
                var (offsetX, offsetY) = !outer ? (0, 0) : part switch
                {
                    SkinPart.Head => (32, 0),
                    SkinPart.LeftArm => (16, 0),
                    SkinPart.LeftLeg => (-16, 0),
                    _ => (0, 16)
                };
                var faces = new[] { SkinFace.Front, SkinFace.Back, SkinFace.Right, SkinFace.Left, SkinFace.Top, SkinFace.Bottom };
                for (var index = 0; index < faces.Length; index++)
                {
                    var face = faces[index];
                    var faceWidth = face is SkinFace.Right or SkinFace.Left ? depth : width;
                    var faceHeight = face is SkinFace.Top or SkinFace.Bottom ? depth : height;
                    faceWidth *= size / 64;
                    faceHeight *= size / 64;
                    foreach (var (x, y) in new[] { (0, faceHeight - 1), (faceWidth - 1, 0) })
                    {
                        var u = (x + 0.5f) / faceWidth;
                        var v = (y + 0.5f) / faceHeight;
                        var span = max - min;
                        var (point, normal) = face switch
                        {
                            SkinFace.Front => (new Vector3(min.X + (span.X * u), max.Y - (span.Y * v), max.Z), Vector3.UnitZ),
                            SkinFace.Back => (new Vector3(max.X - (span.X * u), max.Y - (span.Y * v), min.Z), -Vector3.UnitZ),
                            SkinFace.Right => (new Vector3(min.X, max.Y - (span.Y * v), min.Z + (span.Z * u)), -Vector3.UnitX),
                            SkinFace.Left => (new Vector3(max.X, max.Y - (span.Y * v), max.Z - (span.Z * u)), Vector3.UnitX),
                            SkinFace.Top => (new Vector3(min.X + (span.X * u), max.Y, min.Z + (span.Z * v)), Vector3.UnitY),
                            _ => (new Vector3(min.X + (span.X * u), min.Y, max.Z - (span.Z * v)), -Vector3.UnitY)
                        };
                        var origin = point + (normal * 8);
                        var direction = -normal;
                        var alpha = outer ? 128u : 255u;
                        var color = 0x40000000u | (serial++ << 8) | alpha;
                        yield return new(new() { Part = part, Layer = layer, Face = face }, x, y,
                            ((origins[index].X + offsetX) * (size / 64)) + x, ((origins[index].Y + offsetY) * (size / 64)) + y,
                            new(origin.X, origin.Y, origin.Z), new(direction.X, direction.Y, direction.Z), color);
                    }
                }
            }
        }
    }
}