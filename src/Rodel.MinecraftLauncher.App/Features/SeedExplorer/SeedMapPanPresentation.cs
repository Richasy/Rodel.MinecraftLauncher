// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Input;
using Sprout.Semantics;
using Sprout.Widgets;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Translates the retained world subtree and its interaction geometry without rebuilding map state.</summary>
internal static class SeedMapPanPresentation
{
    internal static float Project(double coordinate, double center, double blocksPerPixel, float extent) => (float)(((coordinate - center) / blocksPerPixel) + (extent / 2));

    internal static void Offset(TransformWidget transform, InteractionNode? interaction, SemanticsNode? semantics, Point value)
    {
        transform.SetTranslation(value);
        var matrix = Matrix3x2.CreateTranslation(value.X, value.Y);
        interaction?.Transform = matrix;
        semantics?.Transform = matrix;
    }
}