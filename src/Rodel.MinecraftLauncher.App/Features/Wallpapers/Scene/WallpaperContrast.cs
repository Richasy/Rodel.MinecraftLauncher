// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

internal sealed record WallpaperSample(int Width, int Height, byte[] Pixels)
{
    internal byte[]? BlurPixels;
    internal byte[]? BlurScratch;
    internal int BlurRadius = -1;
}

internal sealed record WallpaperTextRegion(
    Rect[] Characters,
    Color Ink,
    bool Large,
    bool Outlined);

/// <summary>Cover mapping and local contrast, independent of the graphics device and UI thread.</summary>
internal static class WallpaperContrast
{
    private static readonly double[] Opacities = [.302, .45, .6, .72];
    private static readonly double[] Linear = [..Enumerable.Range(0, 256).Select(value =>
            {
                var s = value / 255d;
                return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
            })];

    internal static double Luminance(double red, double green, double blue) => (.2126 * Linear[Math.Clamp((int)Math.Round(red), 0, 255)]) + (.7152 * Linear[Math.Clamp((int)Math.Round(green), 0, 255)]) + (.0722 * Linear[Math.Clamp((int)Math.Round(blue), 0, 255)]);

    internal static TextOutlinePaint Analyze(
        WallpaperSample sample,
        Size viewport,
        ImageAlignment alignment,
        WallpaperTextRegion region,
        Color background,
        int characterBudget = 144,
        bool light = true,
        byte[]? blurred = null)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 || region.Characters.Length == 0)
            return default;
        double scale = Math.Max(viewport.Width / sample.Width, viewport.Height / sample.Height);
        var left = (viewport.Width - (sample.Width * scale)) * alignment.Horizontal;
        var top = (viewport.Height - (sample.Height * scale)) * alignment.Vertical;
        double ink = Luminance(region.Ink.R, region.Ink.G, region.Ink.B), threshold = region.Large ? 3 : 4.5;
        int edge = ink < .3 ? 255 : 0, bad = 0, total = 0;
        bool previousBad = false, adjacent = false;
        var strength = .302;
        var extent = (viewport.Width * .8660254038) + (viewport.Height * .5);
        foreach (var rect in region.Characters)
        {
            int localBad = 0, localTotal = 0;
            var columns = Math.Clamp((int)Math.Ceiling(rect.Width / scale), 3, Math.Min(12, characterBudget / 3));
            var rows = Math.Clamp((int)Math.Ceiling(rect.Height / scale), 3, Math.Min(12, characterBudget / columns));
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    double px = rect.Left + ((x + .5) * rect.Width / columns), py = rect.Top + ((y + .5) * rect.Height / rows);
                    if (px < 0 || py < 0 || px >= viewport.Width || py >= viewport.Height)
                        continue;
                    int sx = Math.Clamp((int)((px - left) / scale), 0, sample.Width - 1), sy = Math.Clamp((int)((py - top) / scale), 0, sample.Height - 1);
                    var offset = ((sy * sample.Width) + sx) * 4;
                    var transparency = 1 - (sample.Pixels[offset + 3] / 255d);
                    double r = sample.Pixels[offset + 2] + (background.R * transparency), g = sample.Pixels[offset + 1] + (background.G * transparency), b = sample.Pixels[offset] + (background.B * transparency);
                    var diagonal = ((px * .8660254038) + ((viewport.Height - py) * .5)) / extent;
                    if (blurred is not null)
                    {
                        var weight = WallpaperScenePolicy.BlurWeight(diagonal);
                        var alpha = blurred[offset + 3] / 255d;
                        r = ((blurred[offset + 2] + (background.R * (1 - alpha))) * weight) + (r * (1 - weight));
                        g = ((blurred[offset + 1] + (background.G * (1 - alpha))) * weight) + (g * (1 - weight));
                        b = ((blurred[offset] + (background.B * (1 - alpha))) * weight) + (b * (1 - weight));
                    }

                    WallpaperScenePolicy.Composite(
                        light,
                        diagonal,
                        ref r,
                        ref g,
                        ref b);
                    var luminance = Luminance(r, g, b);
                    if ((Math.Max(ink, luminance) + .05) / (Math.Min(ink, luminance) + .05) < threshold)
                    {
                        localBad++;
                        foreach (var opacity in Opacities)
                        {
                            var rim = Luminance((edge * opacity) + (r * (1 - opacity)), (edge * opacity) + (g * (1 - opacity)), (edge * opacity) + (b * (1 - opacity)));
                            strength = Math.Max(strength, opacity);
                            if ((Math.Max(ink, rim) + .05) / (Math.Min(ink, rim) + .05) >= threshold)
                                break;
                        }
                    }

                    localTotal++;
                }
            }

            var poor = localTotal > 0 && localBad / (double)localTotal >= .4;
            adjacent |= previousBad && poor;
            previousBad = poor;
            bad += localBad;
            total += localTotal;
        }

        if (total == 0 || (!adjacent && bad / (double)total < (region.Outlined ? .05 : .12)))
            return default;
        return new(Color.FromArgb((byte)Math.Round(strength * 255), (byte)edge, (byte)edge, (byte)edge), region.Large ? 1.4f : .85f);
    }
}