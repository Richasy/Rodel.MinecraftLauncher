// Copyright (c) Richasy. All rights reserved.

using Sprout.Graphics;
using Sprout.Svg;
using System.Xml.Linq;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shares catalog-artwork sizing and viewport normalization between provider headers and model rows.</summary>
internal static class AiBrandArtwork
{
    internal const float Size = 32f;
    // Match SettingsRowPanel's native icon-to-label spacing. Model rows also use its two-DIP leading icon inset.
    internal const float Gap = 20f;
    internal const float TextInset = 16f + 2f + Size + Gap;

    internal static ImageSource Source(string svg)
    {
        var document = XDocument.Parse(svg);
        var root = document.Root!;
        // Sprout uses viewBox as the rasterization viewport. A fixed outer width/height would shrink and offset
        // the artwork inside that viewport a second time (e.g. 24x24 over a 40x40 viewBox).
        if (root.Attribute("viewBox") is not null)
        {
            root.SetAttributeValue("width", "100%");
            root.SetAttributeValue("height", "100%");
        }

        return SvgImageSource.FromString(document.ToString(SaveOptions.DisableFormatting));
    }
}