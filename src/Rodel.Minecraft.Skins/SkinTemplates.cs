// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Loads original characters and blank face guides shared by native and command-line hosts.</summary>
public static class SkinTemplates
{
    /// <summary>Returns stable template IDs; hosts localize their display labels.</summary>
    public static string[] Ids => ["classic", "alex", "casual", "base", "blank", "cape"];

    /// <summary>Returns the template's work kind and initial character arm-model suggestion.</summary>
    public static SkinModel SuggestedModel(string id)
    {
        CheckId(id);
        return id switch { "alex" => SkinModel.Slim, "cape" => SkinModel.Cape, _ => SkinModel.Classic };
    }

    /// <summary>Creates independent pixels at the explicit model and width; the cape template requires Cape and has half-height, while HD character seeds use nearest-neighbor expansion.</summary>
    public static SkinDocument Create(string id, SkinModel model, int size = 64)
    {
        CheckId(id);
        SkinLayout.CheckSize(size);
        if (!Enum.IsDefined(model) || (id == "cape" != (model == SkinModel.Cape)))
            throw new ArgumentOutOfRangeException(nameof(model));
        if (id is "blank" or "cape")
            return CreateBlank(model, size) with { Transaction = new("template", "system", id) };
        var modelId = model == SkinModel.Slim ? "slim" : "classic";
        using var resource = typeof(SkinTemplates).Assembly.GetManifestResourceStream($"Rodel.Minecraft.Skins.Templates.{id}-{modelId}.png")
            ?? throw new InvalidDataException("The packaged skin template is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var seed = SkinPng.Decode(buffer.ToArray(), model);
        if (size == 64)
            return seed with { Transaction = new("template", "system", id) };
        var pixels = new uint[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                pixels[(y * size) + x] = seed.Pixels[(y / 2 * 64) + (x / 2)];
        }

        return seed with { Size = size, Pixels = pixels, Transaction = new("template", "system", id) };
    }

    private static SkinDocument CreateBlank(SkinModel model, int size)
    {
        var document = SkinEditor.Create(model, "#D4D4D4", size);
        var edgeWidth = size / 64;
        foreach (var face in SkinLayout.Faces(model, size).Where(face => face.Surface.Layer == SkinLayer.Base))
        {
            var shade = face.Surface.Face switch
            {
                SkinFace.Front => 212u,
                SkinFace.Back => 192u,
                SkinFace.Left => 152u,
                SkinFace.Right => 168u,
                SkinFace.Top => 228u,
                _ => 144u,
            };
            var edge = Math.Min(245u, shade + 18u);
            for (var y = 0; y < face.Height; y++)
            {
                for (var x = 0; x < face.Width; x++)
                {
                    var outline = x < edgeWidth || y < edgeWidth || x >= face.Width - edgeWidth || y >= face.Height - edgeWidth;
                    document.Pixels[((face.Y + y) * size) + face.X + x] = Gray(outline ? edge : shade);
                }
            }
        }

        return document;
    }

    private static uint Gray(uint value) => (value * 0x01010100u) | 0xFFu;

    private static void CheckId(string id)
    {
        if (!Ids.Contains(id, StringComparer.Ordinal))
            throw new ArgumentException("Choose an existing skin template ID.", nameof(id));
    }
}