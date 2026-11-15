// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Images;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

[View]
internal readonly partial record struct InstanceIconView(string? Encoded, float IconWidth = 36f, float IconHeight = 40f)
{
    private static readonly Dictionary<string, ImageSource> Sources = [with(StringComparer.Ordinal)];
    internal ImageSource? Source
    {
        get
        {
            if (string.IsNullOrEmpty(Encoded))
                return null;
            if (Sources.TryGetValue(Encoded, out var source))
                return source;
            try
            {
                source = ImageSource.FromBytes(Convert.FromBase64String(Encoded), decodePixelWidth: 128, decodePixelHeight: 128);
                if (Sources.Count >= 16)
                    Sources.Remove(Sources.Keys.First());
                Sources[Encoded] = source;
                return source;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
    internal static readonly ImageSource Transparent = ImageSource.FromBytes(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="));

    public Ui Body => Overlay([
        Image(Source ?? ImagePresetCatalog.Instances[0].Source)
            .Stretch(ImageStretch.Uniform),
    ])
        .Width(IconWidth).Height(IconHeight);
}