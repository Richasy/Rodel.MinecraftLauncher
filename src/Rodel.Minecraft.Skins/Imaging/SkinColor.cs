// Copyright (c) Richasy. All rights reserved.

using System.Globalization;

namespace Rodel.Minecraft.Skins;

/// <summary>Provides deterministic straight-alpha pixel arithmetic independent of any UI or image host.</summary>
public static class SkinColor
{
    /// <summary>Parses an exact RGB or RGBA hexadecimal color.</summary>
    public static uint Parse(string color)
    {
        if (color.Length is not (7 or 9) || color[0] != '#'
            || !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException("Use #RRGGBB or #RRGGBBAA.", nameof(color));
        }

        return color.Length == 7 ? (value << 8) | 255 : value;
    }

    /// <summary>Composites source-over RGBA colors with an independent coverage/opacity factor.</summary>
    public static uint Composite(uint background, uint foreground, double opacity = 1)
    {
        CheckUnit(opacity, nameof(opacity));
        var sourceAlpha = (foreground & 255) / 255.0 * opacity;
        var destinationAlpha = (background & 255) / 255.0;
        var alpha = sourceAlpha + (destinationAlpha * (1 - sourceAlpha));
        if (alpha == 0)
            return 0;
        uint Channel(int shift) => (uint)Byte(((((foreground >> shift) & 255) * sourceAlpha)
            + (((background >> shift) & 255) * destinationAlpha * (1 - sourceAlpha))) / alpha) << shift;
        return Channel(24) | Channel(16) | Channel(8) | (uint)Byte(alpha * 255);
    }

    internal static uint Replace(uint background, uint foreground, double coverage)
    {
        if (coverage >= 1)
            return foreground;
        var sourceAlpha = (foreground & 255) / 255.0;
        var destinationAlpha = (background & 255) / 255.0;
        var alpha = (sourceAlpha * coverage) + (destinationAlpha * (1 - coverage));
        if (alpha == 0)
            return 0;
        uint Channel(int shift) => (uint)Byte(((((foreground >> shift) & 255) * sourceAlpha * coverage)
            + (((background >> shift) & 255) * destinationAlpha * (1 - coverage))) / alpha) << shift;
        return Channel(24) | Channel(16) | Channel(8) | (uint)Byte(alpha * 255);
    }

    internal static uint Adjust(uint color, double hue, double saturation, double brightness, double contrast)
    {
        var r = ((color >> 24) & 255) / 255.0;
        var g = ((color >> 16) & 255) / 255.0;
        var b = ((color >> 8) & 255) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var h = 0.0;
        if (delta != 0)
        {
            if (max == r)
            {
                h = (g - b) / delta;
                h %= 6;
            }
            else
            {
                h = max == g ? ((b - r) / delta) + 2 : ((r - g) / delta) + 4;
            }
        }

        h = ((((h * 60) + hue) % 360) + 360) % 360;
        var s = Math.Clamp((max == 0 ? 0 : delta / max) + saturation, 0, 1);
        var v = Math.Clamp(max + brightness, 0, 1);
        var chroma = v * s;
        var x = chroma * (1 - Math.Abs((h / 60 % 2) - 1));
        var (red, green, blue) = h switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        var factor = contrast >= 0 ? 1 / Math.Max(1 - contrast, 1.0 / 255) : 1 + contrast;
        uint Channel(double value) => (uint)Byte((((value + v - chroma - 0.5) * factor) + 0.5) * 255);
        return (Channel(red) << 24) | (Channel(green) << 16) | (Channel(blue) << 8) | (color & 255);
    }

    internal static uint Gradient(uint first, uint second, double amount) => Replace(first, second, amount);

    internal static bool Matches(uint first, uint second, int tolerance) =>
        Enumerable.Range(0, 4).All(channel => Math.Abs((int)((first >> (channel * 8)) & 255) - (int)((second >> (channel * 8)) & 255)) <= tolerance);

    internal static void CheckUnit(double value, string parameter)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(parameter, "Use a finite value from zero through one.");
    }

    internal static int Byte(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}