// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Reduces perceptually close automatic swatches and orders real source colors into lightness ramps.</summary>
internal static class SkinPaletteRefinement
{
    // This is a useful palette separation, not a claim that smaller differences are invisible.
    private const float MinimumDistance = .05f;
    private const float NeutralChroma = .018f;
    private const float FamilySpan = 60f;

    internal static string[] Apply(ReadOnlySpan<byte> pixels, IReadOnlyList<string> palette)
    {
        var candidates = palette.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(value => new Candidate(value, ToOklab(
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16)))).ToArray();
        if (candidates.Length == 0)
            return [];

        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] == 0)
                continue;
            var sample = ToOklab(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            var closest = 0;
            var distance = float.MaxValue;
            for (var index = 0; index < candidates.Length; index++)
            {
                var next = Vector3.DistanceSquared(sample, candidates[index].Lab);
                if (next < distance)
                {
                    closest = index;
                    distance = next;
                }
            }

            candidates[closest].Population++;
        }

        var retained = new List<Candidate>();
        foreach (var color in candidates.OrderByDescending(color => color.Population).ThenBy(color => color.Value, StringComparer.Ordinal))
        {
            // Keep a subtle skin or fabric tint separate from a similarly bright neutral background.
            if (color.Population > 0 && !retained.Any(other => other.IsNeutral == color.IsNeutral
                && Vector3.DistanceSquared(other.Lab, color.Lab) < MinimumDistance * MinimumDistance))
            {
                retained.Add(color);
            }
        }

        return OrderFamilies(retained);
    }

    private static string[] OrderFamilies(List<Candidate> colors)
    {
        var result = ByLightness(colors.Where(color => color.IsNeutral)).Select(color => color.Value).ToList();
        var families = new List<List<Candidate>>();
        foreach (var color in colors.Where(color => !color.IsNeutral).OrderBy(color => color.Hue).ThenBy(color => color.Value, StringComparer.Ordinal))
        {
            if (families.Count == 0 || color.Hue - families[^1][0].Hue > FamilySpan)
                families.Add([]);
            families[^1].Add(color);
        }

        // Reds on opposite sides of the hue seam still belong to one color family.
        if (families.Count > 1 && families[0][^1].Hue + 360 - families[^1][0].Hue <= FamilySpan)
        {
            families[0].AddRange(families[^1]);
            families.RemoveAt(families.Count - 1);
        }

        foreach (var family in families)
            result.AddRange(ByLightness(family).Select(color => color.Value));
        return [.. result];
    }

    private static IOrderedEnumerable<Candidate> ByLightness(IEnumerable<Candidate> colors) =>
        colors.OrderBy(color => color.Lab.X).ThenBy(color => color.Value, StringComparer.Ordinal);

    private static Vector3 ToOklab(byte red, byte green, byte blue)
    {
        var r = Linear(red);
        var g = Linear(green);
        var b = Linear(blue);
        var l = MathF.Cbrt((.4122214708f * r) + (.5363325363f * g) + (.0514459929f * b));
        var m = MathF.Cbrt((.2119034982f * r) + (.6806995451f * g) + (.1073969566f * b));
        var s = MathF.Cbrt((.0883024619f * r) + (.2817188376f * g) + (.6299787005f * b));
        return new(
            (.2104542553f * l) + (.7936177850f * m) - (.0040720468f * s),
            (1.9779984951f * l) - (2.4285922050f * m) + (.4505937099f * s),
            (.0259040371f * l) + (.7827717662f * m) - (.8086757660f * s));
    }

    private static float Linear(byte component)
    {
        var value = component / 255f;
        return value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    }

    private sealed class Candidate(string value, Vector3 lab)
    {
        internal string Value { get; } = value;
        internal Vector3 Lab { get; } = lab;
        internal int Population { get; set; }
        internal bool IsNeutral => (Lab.Y * Lab.Y) + (Lab.Z * Lab.Z) < NeutralChroma * NeutralChroma;
        internal float Hue => ((MathF.Atan2(Lab.Z, Lab.Y) * 180 / MathF.PI) + 360) % 360;
    }
}