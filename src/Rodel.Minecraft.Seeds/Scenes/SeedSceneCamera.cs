// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Seeds;

/// <summary>Defines an orthographic turntable that follows pointer movement and shares its projection with container pins.</summary>
/// <param name="Yaw">Horizontal viewing angle in radians.</param>
/// <param name="Pitch">Elevation in radians, clamped during pointer movement.</param>
/// <param name="Zoom">Positive scale relative to the fitted original mesh.</param>
/// <param name="Plan">Whether the original mesh is viewed directly from above.</param>
/// <param name="Focus">Optional region-local point to center without replacing the frozen structure or its view angles.</param>
public sealed record SeedSceneCamera(float Yaw = -.74f, float Pitch = .6f, float Zoom = 1f, bool Plan = false, Vector3? Focus = null)
{
    /// <summary>Rotates the near side in the direction of the pointer; dragging right moves the near model surface right.</summary>
    public SeedSceneCamera Drag(float deltaX, float deltaY) => this with { Yaw = Yaw - (deltaX * .009f), Pitch = Math.Clamp(Pitch + (deltaY * .006f), .15f, 1.35f) };

    /// <summary>Fits an original layer mesh in logical viewport units; the same matrix is used by GPU rasterization and UI pins.</summary>
    public SeedSceneProjection Project(SeedSceneMesh mesh, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width < 1 || height < 1 || !float.IsFinite(Zoom) || Zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The model viewport and zoom must be positive and finite.");
        var angle = Plan ? MathF.PI / 2 : Pitch;
        var ca = MathF.Cos(Yaw); var sa = MathF.Sin(Yaw); var cp = MathF.Cos(angle); var sp = MathF.Sin(angle);
        var right = new Vector3(ca, 0, -sa);
        var up = new Vector3(-sa * sp, cp, -ca * sp);
        var toward = new Vector3(sa * cp, sp, ca * cp);
        var minimum = new Vector2(float.MaxValue); var maximum = new Vector2(float.MinValue);
        foreach (var x in new[] { mesh.Minimum.X, mesh.Maximum.X })
        {
            foreach (var y in new[] { mesh.Minimum.Y, mesh.Maximum.Y })
            {
                foreach (var z in new[] { mesh.Minimum.Z, mesh.Maximum.Z })
                {
                    var point = new Vector3(x, y, z);
                    var projected = new Vector2(Vector3.Dot(point, right), Vector3.Dot(point, up));
                    minimum = Vector2.Min(minimum, projected); maximum = Vector2.Max(maximum, projected);
                }
            }
        }

        var scale = MathF.Min(Math.Max(1, width - 56) / Math.Max(1, maximum.X - minimum.X), Math.Max(1, height - 56) / Math.Max(1, maximum.Y - minimum.Y)) * Zoom;
        var center = Focus is { } focus ? new Vector2(Vector3.Dot(focus, right), Vector3.Dot(focus, up)) : (minimum + maximum) / 2;
        var depth = Vector3.Dot(Focus ?? ((mesh.Minimum + mesh.Maximum) / 2), toward);
        var span = Math.Max(4, Vector3.Distance(mesh.Minimum, mesh.Maximum) * 2);
        var sx = 2 * scale / width; var sy = 2 * scale / height;
        return new(new Matrix4x4(
            right.X * sx, up.X * sy, -toward.X / span, 0,
            right.Y * sx, up.Y * sy, -toward.Y / span, 0,
            right.Z * sx, up.Z * sy, -toward.Z / span, 0,
            -center.X * sx, -center.Y * sy, .5f + (depth / span), 1), width, height);
    }
}