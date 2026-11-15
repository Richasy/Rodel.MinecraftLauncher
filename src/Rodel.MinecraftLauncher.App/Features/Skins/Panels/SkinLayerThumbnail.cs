// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Creates a small, actual RGBA layer preview over a checkerboard without PNG encoding.</summary>
internal static class SkinLayerThumbnail
{
    internal static ImageSource Create(uint[] pixels, int size, int width = 38)
    {
        var sourceHeight = pixels.Length / size;
        var height = Math.Max(1, width * sourceHeight / size);
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample = pixels[(Math.Min((int)((y + .5) * sourceHeight / height), sourceHeight - 1) * size) + Math.Min((int)((x + .5) * size / width), size - 1)];
                var checker = (((x / 4) + (y / 4)) & 1) == 0 ? 0x485052FFu : 0x697174FFu;
                var color = SkinColor.Composite(checker, sample);
                var offset = ((y * width) + x) * 4;
                bytes[offset] = (byte)(color >> 8);
                bytes[offset + 1] = (byte)(color >> 16);
                bytes[offset + 2] = (byte)(color >> 24);
                bytes[offset + 3] = 255;
            }
        }

        return ImageSource.FromPixels(new(width, height, bytes));
    }
}