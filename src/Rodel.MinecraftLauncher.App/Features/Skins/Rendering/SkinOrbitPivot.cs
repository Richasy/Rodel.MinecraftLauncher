// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.Skins.Rendering;

/// <summary>Finds the unweighted center of visible body-part midpoints from the same cuboids used for rendering.</summary>
internal static class SkinOrbitPivot
{
    private static readonly Vector3[] Classic = Centers(SkinModel.Classic);
    private static readonly Vector3[] Slim = Centers(SkinModel.Slim);

    internal static Vector3 For(SkinModel model, int parts)
    {
        if (model == SkinModel.Cape)
            return new(0, 16, -3);
        var centers = model == SkinModel.Slim ? Slim : Classic;
        var total = Vector3.Zero;
        var count = 0;
        for (var index = 0; index < centers.Length; index++)
        {
            if ((parts & (1 << index)) != 0)
            {
                total += centers[index];
                count++;
            }
        }

        return count == 0 ? new(0, 16, 0) : total / count;
    }

    private static Vector3[] Centers(SkinModel model)
    {
        var quads = SkinGeometry.Quads(model);
        var centers = new Vector3[6];
        for (var part = 0; part < centers.Length; part++)
        {
            var minimum = new Vector3(float.MaxValue);
            var maximum = new Vector3(float.MinValue);
            foreach (var quad in quads.Where(q => ((int)q.Texture.Surface.Part == part) && (q.Texture.Surface.Layer == SkinLayer.Base)))
            {
                var origin = new Vector3(quad.Origin);
                var right = new Vector3(quad.Right);
                var down = new Vector3(quad.Down);
                for (var corner = 0; corner < 4; corner++)
                {
                    var point = origin + (right * (corner & 1)) + (down * (corner >> 1));
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
            }

            centers[part] = (minimum + maximum) / 2;
        }

        return centers;
    }
}