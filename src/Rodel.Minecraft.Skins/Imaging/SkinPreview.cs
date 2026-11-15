// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Skins;

/// <summary>Renders the shared model geometry and the visible paint-layer composite.</summary>
public static class SkinPreview
{
    /// <summary>Renders an orthographic snapshot with actual surface intersection, depth and alpha composition.</summary>
    public static byte[] Render(SkinDocument document, SkinViewOptions? options = null, SkinRenderSnapshot? companion = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        SkinGeometry.CheckView(options);
        var scene = new SkinSceneSnapshot(new(document), companion);
        var texture = scene.Pixels.Span;
        var size = options.Size;
        var rotation = Matrix4x4.CreateRotationX(-options.Pitch * MathF.PI / 180) * Matrix4x4.CreateRotationY(options.Yaw * MathF.PI / 180);
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, rotation);
        var rightAxis = Vector3.TransformNormal(Vector3.UnitX, rotation);
        var upAxis = Vector3.TransformNormal(Vector3.UnitY, rotation);
        var quads = scene.Quads().Select(quad => new SkinFrame(quad.Texture, new(quad.Origin), new(quad.Right), new(quad.Down)))
            .Where(frame => SkinGeometry.Visible(frame.Texture.Surface, options)).ToArray();
        var pixels = new uint[size * size];
        var hits = new List<(float Distance, uint Color, int Order)>(12);
        for (var y = 0; y < size; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                hits.Clear();
                var ray = new Vector3(0, 16, 0) - (forward * 64)
                    + (rightAxis * (((x + 0.5f) / size) - 0.5f) * 40)
                    + (upAxis * (0.5f - ((y + 0.5f) / size)) * 40);
                foreach (var frame in quads)
                {
                    var denominator = Vector3.Dot(frame.Normal, forward);
                    if (denominator >= -0.00001f)
                        continue;
                    var distance = Vector3.Dot(frame.Origin - ray, frame.Normal) / denominator;
                    var local = frame.Local(ray + (distance * forward));
                    var textureX = (int)Math.Floor(local.X + 0.5);
                    var textureY = (int)Math.Floor(local.Y + 0.5);
                    if (distance < 0 || textureX < 0 || textureY < 0 || textureX >= frame.Texture.Width || textureY >= frame.Texture.Height)
                        continue;
                    var color = texture[((frame.Texture.Y + textureY) * scene.Width) + frame.Texture.X + textureX];
                    if ((color & 255) != 0)
                        hits.Add((distance, color, ((int)frame.Texture.Surface.Part * 2) + (int)frame.Texture.Surface.Layer));
                }

                var pixel = 0x20242BFFu;
                // Picking keeps the first geometry on a coplanar tie; source-over must therefore draw that geometry last.
                foreach (var (Distance, Color, Order) in hits.OrderByDescending(hit => hit.Distance).ThenByDescending(hit => hit.Order))
                    pixel = SkinColor.Composite(pixel, Color);
                pixels[(y * size) + x] = pixel;
            }
        }

        return SkinPng.EncodePixels(pixels, size, size);
    }

}