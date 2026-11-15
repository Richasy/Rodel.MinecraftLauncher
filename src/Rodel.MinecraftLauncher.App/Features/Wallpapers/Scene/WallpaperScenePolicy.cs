// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Wallpapers;

/// <summary>CPU counterpart of the scene shader, using bounded cached blur samples.</summary>
internal static class WallpaperScenePolicy
{
    internal static double BlurWeight(double t) => t <= .14
        ? 1
        : t <= .28
        ? Lerp(
        1,
        166 / 255d,
        t,
            .14,
            .28)
        : t <= .42 ? Lerp(
        166 / 255d,
            .2,
        t,
            .28,
            .42) : Lerp(
            .2,
        0,
        t,
            .42,
            .54);

    private static double Lerp(
        double a,
        double b,
        double t,
        double start,
        double end) => a + ((b - a) * Math.Clamp((t - start) / (end - start), 0, 1));

    internal static void Composite(
        bool light,
        double t,
        ref double red,
        ref double green,
        ref double blue)
    {
        var opacity = light
            ? t <= .24
            ? Lerp(
                .62,
                .56,
            t,
            0,
                .24)
            : t <= .42 ? Lerp(
                .56,
                .24,
            t,
                .24,
                .42) : Lerp(
                .24,
            0,
            t,
                .42,
                .56)
            : t <= .32
            ? Lerp(
                .68,
                .58,
            t,
            0,
                .32)
            : t <= .64 ? Lerp(
                .58,
                .25,
            t,
                .32,
                .64) : Lerp(
                .25,
                .16,
            t,
                .64,
            1);
        red = (red * (1 - opacity)) + ((light ? 249 : 9) * opacity);
        green = (green * (1 - opacity)) + ((light ? 246 : 22) * opacity);
        blue = (blue * (1 - opacity)) + ((light ? 235 : 27) * opacity);
    }

    internal static byte[] Blur(
        WallpaperSample sample,
        float viewportWidth,
        float viewportHeight,
        CancellationToken token)
    {
        var scale = Math.Max(viewportWidth / sample.Width, viewportHeight / sample.Height);
        var radius = Math.Clamp((int)Math.Round((10 / scale) - .5), 1, 24);
        sample.BlurPixels ??= new byte[sample.Pixels.Length];
        sample.BlurScratch ??= new byte[sample.Pixels.Length];
        if (sample.BlurRadius == radius)
            return sample.BlurPixels;
        var input = sample.Pixels;
        for (var pass = 0; pass < 3; pass++)
        {
            token.ThrowIfCancellationRequested();
            Box(
                input,
                sample.BlurScratch,
                sample.Width,
                sample.Height,
                radius,
                false);
            Box(
                sample.BlurScratch,
                sample.BlurPixels,
                sample.Width,
                sample.Height,
                radius,
                true);
            input = sample.BlurPixels;
        }

        sample.BlurRadius = radius;
        return sample.BlurPixels;
    }

    private static void Box(
        byte[] source,
        byte[] target,
        int width,
        int height,
        int radius,
        bool vertical)
    {
        int length = vertical ? height : width, lines = vertical ? width : height, stride = vertical ? width * 4 : 4, count = (radius * 2) + 1;
        for (var line = 0; line < lines; line++)
        {
            var start = vertical ? line * 4 : line * width * 4;
            int b = 0, g = 0, r = 0, a = 0;
            for (var i = -radius; i <= radius; i++)
            {
                var offset = start + (Math.Clamp(i, 0, length - 1) * stride);
                b += source[offset];
                g += source[offset + 1];
                r += source[offset + 2];
                a += source[offset + 3];
            }

            for (var i = 0; i < length; i++)
            {
                var offset = start + (i * stride);
                target[offset] = (byte)(b / count);
                target[offset + 1] = (byte)(g / count);
                target[offset + 2] = (byte)(r / count);
                target[offset + 3] = (byte)(a / count);
                int remove = start + (Math.Max(0, i - radius) * stride), add = start + (Math.Min(length - 1, i + radius + 1) * stride);
                b += source[add] - source[remove];
                g += source[add + 1] - source[remove + 1];
                r += source[add + 2] - source[remove + 2];
                a += source[add + 3] - source[remove + 3];
            }
        }
    }
}