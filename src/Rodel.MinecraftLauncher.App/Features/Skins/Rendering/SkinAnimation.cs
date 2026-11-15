// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Read-only transforms for the character parts and the separately textured cape in Y-up, front-positive-Z coordinates.</summary>
internal readonly record struct SkinAnimationPose(Matrix4x4[] Parts);

/// <summary>Samples the standard alternating Minecraft walk without changing the skin document.</summary>
internal static class SkinAnimation
{
    internal const double Duration = 1.2;

    internal static SkinAnimationPose Sample(double seconds)
    {
        var phase = (float)(((seconds % Duration) + Duration) % Duration / Duration);
        // Vanilla's cos(limbSwing * .6662), with opposite arms/legs and 1.4 leg amplitude.
        var swing = MathF.Cos(phase * MathF.Tau);
        return new([Matrix4x4.Identity, Matrix4x4.Identity, Rotate(-swing * .65f, new(-5, 22, 0)), Rotate(swing * .65f, new(5, 22, 0)), Rotate(swing * .91f, new(-2, 12, 0)), Rotate(-swing * .91f, new(2, 12, 0)), Rotate(.12f + (swing * swing * .08f), new(0, 24, -3)),]);
    }

    private static Matrix4x4 Rotate(float angle, Vector3 pivot) => Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(pivot);
}