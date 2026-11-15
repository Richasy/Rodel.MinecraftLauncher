// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Writes the same rest geometry for the editor's D3D renderer and the popup's retained GPU effect.</summary>
internal static class SkinShaderScene
{
    internal const int FloatCount = 1612;

    internal static Vector4 ProjectBounds(
        SkinGpuFrame frame,
        ReadOnlySpan<Vector3> corners,
        int width,
        int height,
        Span<Vector4> boxes)
    {
        var minimum = new Vector2(width, height);
        var maximum = Vector2.Zero;
        var projection = frame.Camera.ViewProjection;
        for (var box = 0; box < 14; box++)
        {
            if (((frame.Parts & (1 << (box / 2))) == 0) || (((box % 2) > 0) && !frame.Outer))
                continue;
            if (corners[box * 8] == corners[(box * 8) + 7])
                continue;
            var boxMinimum = new Vector2(width, height);
            var boxMaximum = Vector2.Zero;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = corners[(box * 8) + corner];
                if (frame.Pose is { } pose && box / 2 < pose.Parts.Length)
                    point = Vector3.Transform(point, pose.Parts[box / 2]);
                var clip = Vector4.Transform(new Vector4(point, 1), projection);
                // A box crossing the camera plane needs conservative full coverage.
                if (clip.W <= 0)
                {
                    boxMinimum = Vector2.Zero;
                    boxMaximum = new(width, height);
                    break;
                }

                var screen = new Vector2(((clip.X / clip.W) + 1) * width / 2, (1 - (clip.Y / clip.W)) * height / 2);
                boxMinimum = Vector2.Min(boxMinimum, screen);
                boxMaximum = Vector2.Max(boxMaximum, screen);
            }

            boxes[box] = new(Math.Clamp(MathF.Floor(boxMinimum.X) - 2, 0, width), Math.Clamp(MathF.Floor(boxMinimum.Y) - 2, 0, height), Math.Clamp(MathF.Ceiling(boxMaximum.X) + 2, 0, width), Math.Clamp(MathF.Ceiling(boxMaximum.Y) + 2, 0, height));
            minimum = Vector2.Min(minimum, boxMinimum);
            maximum = Vector2.Max(maximum, boxMaximum);
        }
        // Include subpixel coverage at the silhouette as well as rounding in the image placement.
        return new(Math.Clamp(MathF.Floor(minimum.X) - 2, 0, width), Math.Clamp(MathF.Floor(minimum.Y) - 2, 0, height), Math.Clamp(MathF.Ceiling(maximum.X) + 2, 0, width), Math.Clamp(MathF.Ceiling(maximum.Y) + 2, 0, height));
    }

    internal static int FaceIndex(SkinSurface surface) => surface.Part == SkinPart.Cape ? surface.Face switch
    {
        SkinFace.Front => 1,
        SkinFace.Back => 0,
        SkinFace.Right => 3,
        SkinFace.Left => 2,
        _ => (int)surface.Face,
    } : (int)surface.Face;

    internal static void WriteGeometry(SkinSceneSnapshot texture, float[] data, Vector3[] corners)
    {
        void Vector(int offset, Vector4 value) => MemoryMarshal.Cast<float, Vector4>(data.AsSpan(offset, 4))[0] = value;
        var quads = texture.Quads();
        for (var part = 0; part < 7; part++)
        {
            for (var layer = 0; layer < 2; layer++)
            {
                var box = (part * 2) + layer;
                var minimum = new Vector3(float.MaxValue);
                var maximum = new Vector3(float.MinValue);
                foreach (var quad in quads.Where(q => ((int)q.Texture.Surface.Part == part) && ((int)q.Texture.Surface.Layer == layer)))
                {
                    var origin = new Vector3(quad.Origin);
                    var right = new Vector3(quad.Right);
                    var down = new Vector3(quad.Down);
                    foreach (var point in new[]
                    {
                        origin,
                        origin + right,
                        origin + down,
                        origin + right + down
                    }
                    )
                    {
                        minimum = Vector3.Min(minimum, point);
                        maximum = Vector3.Max(maximum, point);
                    }

                    var offset = 144 + (((box * 6) + FaceIndex(quad.Texture.Surface)) * 16);
                    Vector(offset, new(origin, 0));
                    Vector(offset + 4, new(right, 0));
                    Vector(offset + 8, new(down, 0));
                    Vector(offset + 12, new(quad.Texture.X, quad.Texture.Y, quad.Texture.Width, quad.Texture.Height));
                }

                if (minimum.X == float.MaxValue)
                {
                    minimum = maximum = Vector3.Zero;
                }

                Vector(32 + (box * 8), new(minimum, part));
                Vector(36 + (box * 8), new(maximum, layer));
                for (var corner = 0; corner < 8; corner++)
                {
                    corners[(box * 8) + corner] = new Vector3((corner & 1) == 0 ? minimum.X : maximum.X, (corner & 2) == 0 ? minimum.Y : maximum.Y, (corner & 4) == 0 ? minimum.Z : maximum.Z);
                }
            }
        }
    }
}