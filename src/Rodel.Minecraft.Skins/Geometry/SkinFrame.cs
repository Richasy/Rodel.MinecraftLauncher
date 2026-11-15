// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Skins;

internal readonly struct SkinFrame
{
    internal SkinFaceInfo Texture { get; }
    internal Vector3 Origin { get; }
    internal Vector3 Right { get; }
    internal Vector3 Down { get; }
    internal Vector3 Normal { get; }

    internal SkinFrame(SkinFaceInfo texture, Vector3 origin, Vector3 right, Vector3 down)
    {
        Texture = texture;
        Origin = origin;
        Right = right;
        Down = down;
        Normal = Vector3.Normalize(Vector3.Cross(down, right));
    }

    internal Vector3 Point(double x, double y) => Origin + (Right * (float)((x + 0.5) / Texture.Width))
        + (Down * (float)((y + 0.5) / Texture.Height));
    internal SkinPoint Local(Vector3 point) => new(
        (Vector3.Dot(point - Origin, Right) / Right.LengthSquared() * Texture.Width) - 0.5,
        (Vector3.Dot(point - Origin, Down) / Down.LengthSquared() * Texture.Height) - 0.5);
}