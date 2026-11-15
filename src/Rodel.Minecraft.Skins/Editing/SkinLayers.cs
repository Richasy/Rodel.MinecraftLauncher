// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Applies paint-layer changes and exposes layer descriptions and composited snapshots.</summary>
public static class SkinLayers
{
    /// <summary>The reserved identifier of the required opaque-base background texture.</summary>
    public const string BackgroundId = SkinDocument.BackgroundLayerId;

    /// <summary>Returns a flattened snapshot for display/export, or a copy of a specifically addressed paint layer.</summary>
    public static uint[] Pixels(SkinDocument document, string? layerId = null) => SkinCompositor.Pixels(document, layerId);

    /// <summary>Returns the required background and additional layer settings without their pixel buffers.</summary>
    public static SkinLayerInfo[] Describe(SkinDocument document) =>
        [new(BackgroundId, document.BackgroundName, true, 1, false), .. document.Layers.Select(layer => new SkinLayerInfo(layer.Id, layer.Name, layer.Visible, layer.Opacity, layer.Locked))];

    internal static void Apply(SkinDocument document, SkinLayerChange change)
    {
        if (!Enum.IsDefined(change.Operation) || string.IsNullOrWhiteSpace(change.Id) || (change.Id == BackgroundId && (change.Operation != SkinLayerOperation.Set || change.Name is null || change.Visible is not null || change.Opacity is not null || change.Locked is not null)))
            throw new ArgumentException("Specify a paint-layer operation and a non-background layer ID.", nameof(change));
        if (change.Opacity is { } opacity)
            SkinColor.CheckUnit(opacity, nameof(change.Opacity));
        if (change.Id == BackgroundId)
            return; // Background name is handled by the immutable document transaction.
        var index = document.Layers.FindIndex(layer => layer.Id == change.Id);
        if (change.Operation is SkinLayerOperation.Add or SkinLayerOperation.Duplicate)
        {
            if (index >= 0)
                throw new ArgumentException("The new paint-layer ID already exists.", nameof(change));
            var source = change.Operation == SkinLayerOperation.Duplicate
                ? (change.SourceId == BackgroundId ? new SkinPaintLayer(BackgroundId, "Background", document.Pixels)
                    : document.Layers.FirstOrDefault(layer => layer.Id == change.SourceId))
                    ?? throw new ArgumentException("Specify an existing source layer for duplication.", nameof(change))
                : null;
            document.Layers.Add(new(change.Id, change.Name ?? source?.Name ?? change.Id,
                source is null ? new uint[document.Size * document.Height] : (uint[])source.Pixels.Clone(),
                change.Visible ?? source?.Visible ?? true, change.Opacity ?? source?.Opacity ?? 1, change.Locked ?? source?.Locked ?? false));
            return;
        }

        if (index < 0)
            throw new ArgumentException("The paint-layer ID was not found.", nameof(change));
        var target = document.Layers[index];
        switch (change.Operation)
        {
            case SkinLayerOperation.Set:
                document.Layers[index] = target with
                {
                    Name = change.Name ?? target.Name,
                    Visible = change.Visible ?? target.Visible,
                    Opacity = change.Opacity ?? target.Opacity,
                    Locked = change.Locked ?? target.Locked
                };
                break;
            case SkinLayerOperation.Reorder:
                var destination = change.Index ?? throw new ArgumentException("Reorder requires an index.", nameof(change));
                if (destination < 0 || destination >= document.Layers.Count)
                    throw new ArgumentOutOfRangeException(nameof(change), "Layer index is outside the layer stack.");
                document.Layers.RemoveAt(index);
                document.Layers.Insert(destination, target);
                break;
            case SkinLayerOperation.Remove:
            case SkinLayerOperation.MergeDown:
                if (target.Locked || (change.Operation == SkinLayerOperation.MergeDown && index > 0 && document.Layers[index - 1].Locked))
                    throw new InvalidOperationException("Unlock the affected layers before deleting or merging them.");
                if (change.Operation == SkinLayerOperation.MergeDown)
                {
                    var below = index == 0 ? null : document.Layers[index - 1];
                    var pixels = below is null ? document.Pixels : new uint[document.Size * document.Height];
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var background = below is null ? pixels[i]
                            : SkinColor.Composite(0, below.Pixels[i], below.Visible ? below.Opacity : 0);
                        pixels[i] = SkinColor.Composite(background, target.Pixels[i], target.Visible ? target.Opacity : 0);
                    }

                    if (below is not null)
                        document.Layers[index - 1] = below with { Pixels = pixels, Opacity = 1, Visible = below.Visible || target.Visible };
                }

                document.Layers.RemoveAt(index);
                break;
        }
    }
}