// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Builds read-only pixel snapshots for rendering, picking and editing queries without invoking edit transactions.</summary>
internal static class SkinCompositor
{
    internal static uint[] Pixels(SkinDocument document, string? layerId = null)
    {
        document.Validate();
        if (layerId is not null)
            return (uint[])document.GetLayerPixels(layerId, writable: false).Clone();
        var result = (uint[])document.Pixels.Clone();
        foreach (var layer in document.Layers.Where(layer => layer.Visible))
        {
            for (var i = 0; i < result.Length; i++)
                result[i] = SkinColor.Composite(result[i], layer.Pixels[i], layer.Opacity);
        }

        if (document.TransparencyMask is { } mask)
        {
            for (var index = 0; index < result.Length; index++)
            {
                if (SkinTransparency.Contains(mask, index))
                    result[index] = 0;
            }
        }

        return result;
    }
}