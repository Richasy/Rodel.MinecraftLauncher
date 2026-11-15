// Copyright (c) Richasy. All rights reserved.

using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Uses the Windows WIC median-cut palette, without quantizing the pixels used by the eyedropper.</summary>
internal static class SkinPaletteQuantizer
{
    internal static string[] Extract(SkinPaletteImage image, CancellationToken cancellation = default)
    {
        // Reservoir sampling avoids aliasing periodic pixel art or missing all opaque pixels.
        const int maximumSamples = 65536;
        var samples = new byte[Math.Min(image.Pixels.Length, maximumSamples * 4)];
        var random = new Random(0);
        var seen = 0;
        for (var offset = 0; offset < image.Pixels.Length; offset += 4)
        {
            if ((offset & 16383) == 0)
                cancellation.ThrowIfCancellationRequested();
            if (image.Pixels[offset + 3] == 0)
                continue;
            seen++;
            var sample = seen <= maximumSamples ? seen - 1 : random.Next(seen);
            if (sample >= maximumSamples)
                continue;
            var target = sample * 4;
            samples[target] = image.Pixels[offset];
            samples[target + 1] = image.Pixels[offset + 1];
            samples[target + 2] = image.Pixels[offset + 2];
            samples[target + 3] = 255;
        }

        if (seen == 0)
            return [];
        Array.Resize(ref samples, Math.Min(seen, maximumSamples) * 4);
        return Quantize(samples);
    }

    private static unsafe string[] Quantize(byte[] pixels)
    {
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED).Succeeded;
        IWICImagingFactory* factory = null;
        IWICBitmap* bitmap = null;
        IWICPalette* palette = null;
        try
        {
            PInvoke.CoCreateInstance(
                PInvoke.CLSID_WICImagingFactory,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                IWICImagingFactory.IID_Guid,
                out var instance).ThrowOnFailure();
            factory = (IWICImagingFactory*)instance;
            var format = PInvoke.GUID_WICPixelFormat32bppRGBA;
            fixed (byte* data = pixels)
            {
                factory->CreateBitmapFromMemory(
                    (uint)(pixels.Length / 4), 1, &format,
                    (uint)pixels.Length, (uint)pixels.Length, data, &bitmap);
                factory->CreatePalette(&palette);
                palette->InitializeFromBitmap((IWICBitmapSource*)bitmap, 16, false);
            }

            var colors = stackalloc uint[16];
            uint count;
            palette->GetColors(16, colors, &count);
            var result = new List<string>();
            for (var index = 0; index < count; index++)
            {
                // WIC may return a box center absent from the source; retain an actual sampled color.
                var color = ClosestSample(pixels, colors[index]);
                if (!result.Contains(color, StringComparer.Ordinal))
                    result.Add(color);
            }

            return SkinPaletteRefinement.Apply(pixels, result);
        }
        finally
        {
            if (palette is not null) palette->Release();
            if (bitmap is not null) bitmap->Release();
            if (factory is not null) factory->Release();
            if (initialized) PInvoke.CoUninitialize();
        }
    }

    private static string ClosestSample(byte[] pixels, uint color)
    {
        var red = (int)((color >> 16) & 255);
        var green = (int)((color >> 8) & 255);
        var blue = (int)(color & 255);
        var nearest = 0;
        var distance = int.MaxValue;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var r = pixels[offset] - red;
            var g = pixels[offset + 1] - green;
            var b = pixels[offset + 2] - blue;
            var candidate = (r * r) + (g * g) + (b * b);
            if (candidate >= distance)
                continue;
            nearest = offset;
            distance = candidate;
            if (distance == 0)
                break;
        }

        return $"#{pixels[nearest]:X2}{pixels[nearest + 1]:X2}{pixels[nearest + 2]:X2}FF";
    }
}