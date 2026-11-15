// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Retains oriented, full-resolution straight RGBA pixels for local palette sampling.</summary>
internal sealed class SkinPaletteImage(int width, int height, byte[] pixels)
{
    internal int Width { get; } = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    internal int Height { get; } = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    internal byte[] Pixels { get; } = pixels.Length == checked(width * height * 4)
        ? pixels
        : throw new ArgumentException("The reference pixel dimensions do not match.", nameof(pixels));

    internal static async Task<SkinPaletteImage> ReadAsync(string path, CancellationToken cancellation)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation).ConfigureAwait(false);
        using var stream = await file.OpenReadAsync().AsTask(cancellation).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation).ConfigureAwait(false);
        _ = checked((int)((long)decoder.OrientedPixelWidth * decoder.OrientedPixelHeight * 4));
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(cancellation).ConfigureAwait(false);
        return new(checked((int)decoder.OrientedPixelWidth), checked((int)decoder.OrientedPixelHeight), data.DetachPixelData());
    }

    internal string Sample(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        var offset = ((y * Width) + x) * 4;
        return $"#{Pixels[offset]:X2}{Pixels[offset + 1]:X2}{Pixels[offset + 2]:X2}{Pixels[offset + 3]:X2}";
    }

    internal ImageSource CreateSource()
    {
        var display = new byte[Pixels.Length];
        for (var offset = 0; offset < Pixels.Length; offset += 4)
        {
            var alpha = Pixels[offset + 3];
            display[offset] = (byte)(((Pixels[offset + 2] * alpha) + 127) / 255);
            display[offset + 1] = (byte)(((Pixels[offset + 1] * alpha) + 127) / 255);
            display[offset + 2] = (byte)(((Pixels[offset] * alpha) + 127) / 255);
            display[offset + 3] = alpha;
        }

        return ImageSource.FromPixels(new(Width, Height, display));
    }

    internal ImageSource Magnify(int x, int y)
    {
        const int cells = 11, cellSize = 10, side = cells * cellSize;
        var display = new byte[side * side * 4];
        for (var py = 0; py < side; py++)
        {
            for (var px = 0; px < side; px++)
            {
                var sx = x + (px / cellSize) - (cells / 2);
                var sy = y + (py / cellSize) - (cells / 2);
                var offset = ((py * side) + px) * 4;
                var background = ((px / cellSize) + (py / cellSize)) % 2 == 0 ? 220 : 170;
                var inside = sx >= 0 && sx < Width && sy >= 0 && sy < Height;
                var source = inside ? ((sy * Width) + sx) * 4 : 0;
                var alpha = inside ? Pixels[source + 3] : 0;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = ((Pixels[source + 2 - channel] * alpha) + (background * (255 - alpha)) + 127) / 255;
                    display[offset + channel] = (byte)(px % cellSize == 0 || py % cellSize == 0 ? value * .75 : value);
                }

                display[offset + 3] = 255;
            }
        }

        return ImageSource.FromPixels(new(side, side, display));
    }
}