// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Maps physical output pixels into one atlas view's independent pan and zoom.</summary>
internal sealed record SkinAtlasView(
    float Cell,
    float X,
    float Y,
    int Layer);

/// <summary>One immutable camera/viewport state shared by GPU submission and CPU picking.</summary>
internal sealed record SkinCamera(
    int Width,
    int Height,
    float Yaw,
    float Pitch,
    float Distance,
    Vector3 Pan = default,
    Vector3? Pivot = null)
{
    internal Quaternion Rotation => Quaternion.CreateFromYawPitchRoll(Yaw, Pitch, 0);
    internal Vector3 Center => (Pivot ?? new Vector3(0, 16, 0)) + Vector3.Transform(Pan, Rotation);
    internal Vector3 Eye => Center + Vector3.Transform(new Vector3(0, 0, Distance), Rotation);
    internal Matrix4x4 ViewProjection => Matrix4x4.CreateLookAt(Eye, Center, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 5, (float)Width / Height, 0.1f, 200f);
    internal Matrix4x4 Inverse
    {
        get
        {
            Matrix4x4.Invert(ViewProjection, out var inverse);
            return inverse;
        }
    }

    internal Vector3 PlanePoint(float x, float y)
    {
        var forward = Vector3.Normalize(Center - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var halfHeight = Distance * MathF.Tan(MathF.PI / 10f);
        return Center + (right * (((x / Width * 2f) - 1f) * halfHeight * Width / Height)) + (up * ((1f - (y / Height * 2f)) * halfHeight));
    }

    internal Vector3 PanDelta(float x, float y) => Vector3.Transform(
        PlanePoint(Width / 2f, Height / 2f) - PlanePoint((Width / 2f) + x, (Height / 2f) + y),
        Quaternion.Inverse(Rotation));

    internal SkinPickRequest Ray(
        float x,
        float y,
        SkinLayer layer,
        SkinPart[]? parts)
    {
        var far = Vector4.Transform(new Vector4((x / Width * 2) - 1, 1 - (y / Height * 2), 1, 1), Inverse);
        var direction = Vector3.Normalize((new Vector3(far.X, far.Y, far.Z) / far.W) - Eye);
        return new(
            new(Eye.X, Eye.Y, Eye.Z),
            new(direction.X, direction.Y, direction.Z),
            layer,
            parts,
            SkipTransparent: false);
    }
}