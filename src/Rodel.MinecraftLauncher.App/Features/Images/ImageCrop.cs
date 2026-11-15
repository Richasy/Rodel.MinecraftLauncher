// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>Owns a cover crop in normalized source coordinates, independent of display DPI.</summary>
internal sealed class ImageCrop(int width, int height, byte[] pixels)
{
    internal const int OutputSize = 256;
    internal double Zoom { get; private set; } = 1;
    internal double X { get; private set; }
    internal double Y { get; private set; }

    internal void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, 1, 4);
        Clamp();
    }

    internal void Move(double x, double y)
    {
        X += x / Zoom;
        Y += y / Zoom;
        Clamp();
    }

    private void Clamp()
    {
        var side = Math.Min(width, height) / Zoom;
        X = Math.Clamp(X, -(width - side) / 2, (width - side) / 2);
        Y = Math.Clamp(Y, -(height - side) / 2, (height - side) / 2);
    }

    internal byte[] Render()
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4))
            throw new InvalidDataException();
        var output = new byte[OutputSize * OutputSize * 4];
        var side = Math.Min(width, height) / Zoom;
        var left = ((width - side) / 2) + X;
        var top = ((height - side) / 2) + Y;
        for (var y = 0; y < OutputSize; y++)
        {
            for (var x = 0; x < OutputSize; x++)
            {
                var sourceX = Math.Clamp((int)(left + ((x + .5) * side / OutputSize)), 0, width - 1);
                var sourceY = Math.Clamp((int)(top + ((y + .5) * side / OutputSize)), 0, height - 1);
                pixels.AsSpan(((sourceY * width) + sourceX) * 4, 4).CopyTo(output.AsSpan(((y * OutputSize) + x) * 4, 4));
            }
        }

        return output;
    }
}