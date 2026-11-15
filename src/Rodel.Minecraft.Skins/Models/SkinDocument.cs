// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Stores an editable texture, paint-layer aggregate and bounded transaction history.</summary>
public sealed record SkinDocument
{
    /// <summary>The reserved identifier of the required background texture; character Base pixels are opaque, while cape pixels may be transparent.</summary>
    public const string BackgroundLayerId = "background";

    /// <summary>Gets the document format understood by this editor.</summary>
    public int SchemaVersion { get; init; } = 3;
    /// <summary>Gets the model interpreting the current texture.</summary>
    public required SkinModel Model { get; init; }
    /// <summary>Gets the texture width, 64 or 128 texels.</summary>
    public int Size { get; init; } = 64;
    /// <summary>Gets the model-specific texture height, including rectangular cape atlases.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Height => SkinLayout.Height(Model, Size);
    /// <summary>Gets the user-editable name of the protected background.</summary>
    public string BackgroundName { get; init; } = "Background";
    /// <summary>Gets the semantic transaction that produced the current content.</summary>
    public SkinTransactionInfo Transaction { get; init; } = new("create", "system");
    /// <summary>Gets the increasing revision used to reject stale edits.</summary>
    public long Revision { get; init; }
    /// <summary>Gets the row-major background pixels packed as #RRGGBBAA.</summary>
    public required uint[] Pixels { get; init; }
    /// <summary>Gets additional paint layers in bottom-to-top order, independently of Minecraft's base/outer surfaces.</summary>
    public required List<SkinPaintLayer> Layers { get; init; }
    /// <summary>Gets the optional row-major atlas mask, with the lowest bit of each byte addressing its first texel.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? TransparencyMask { get; init; }
    /// <summary>Gets complete previous model/size/texture/layer states, oldest first.</summary>
    public List<SkinSnapshot> Undo { get; init; } = [];
    /// <summary>Gets redo states, with the next state at the end.</summary>
    public List<SkinSnapshot> Redo { get; init; } = [];

    /// <summary>Rejects unsupported or damaged document, layer and history state without guessing replacement data.</summary>
    public void Validate()
    {
        if (SchemaVersion != 3 || Revision < 0 || !Enum.IsDefined(Model) || !SkinLayout.SupportedSizes.Contains(Size) || Pixels is null || Pixels.Length != Size * Height || BackgroundName is null || Transaction is null
            || Layers is null || Undo is null || Redo is null || Undo.Count > 32 || Redo.Count > 32
            || Undo.Concat(Redo).Any(snapshot => snapshot is null || !SkinLayout.SupportedSizes.Contains(snapshot.Size) || !Enum.IsDefined(snapshot.Model) || snapshot.BackgroundName is null || snapshot.Transaction is null || snapshot.Pixels is null || snapshot.Pixels.Length != snapshot.Size * SkinLayout.Height(snapshot.Model, snapshot.Size) || snapshot.Layers is null))
        {
            throw new InvalidDataException("Invalid skin document dimensions, history, model or version.");
        }

        foreach (var snapshot in Undo.Concat(Redo).Prepend(new(Pixels, [.. Layers], Model, Size, BackgroundName, Transaction, TransparencyMask)))
        {
            if (snapshot.TransparencyMask is { } mask && mask.Length != SkinTransparency.Length(snapshot.Pixels.Length))
                throw new InvalidDataException("Invalid transparency mask dimensions.");

            if (snapshot.Layers.Any(layer => layer is null || string.IsNullOrWhiteSpace(layer.Id)
                    || layer.Id == BackgroundLayerId || layer.Name is null || layer.Pixels is null || layer.Pixels.Length != snapshot.Size * SkinLayout.Height(snapshot.Model, snapshot.Size)
                    || !double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
                || snapshot.Layers.Select(layer => layer.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Layers.Length)
            {
                throw new InvalidDataException("Invalid paint-layer IDs, pixel dimensions or opacity.");
            }

            foreach (var face in SkinLayout.Faces(snapshot.Model, snapshot.Size).Where(face => snapshot.Model != SkinModel.Cape && face.Surface.Layer == SkinLayer.Base))
            {
                for (var y = 0; y < face.Height; y++)
                {
                    for (var x = 0; x < face.Width; x++)
                    {
                        if ((snapshot.Pixels[((face.Y + y) * snapshot.Size) + face.X + x] & 255) != 255)
                            throw new InvalidDataException("Java skin background base-layer pixels must be fully opaque.");
                    }
                }
            }
        }
    }

    internal static SkinDocument Create(SkinModel model, uint color, int size = 64)
    {
        if (model != SkinModel.Cape && (color & 255) != 255)
            throw new ArgumentException("The background base color must be fully opaque.", nameof(color));
        SkinLayout.CheckSize(size);
        var pixels = new uint[size * SkinLayout.Height(model, size)];
        foreach (var face in SkinLayout.Faces(model, size).Where(face => face.Surface.Layer == SkinLayer.Base))
        {
            for (var y = 0; y < face.Height; y++)
            {
                for (var x = 0; x < face.Width; x++)
                    pixels[((face.Y + y) * size) + face.X + x] = color;
            }
        }

        return new() { Model = model, Size = size, Pixels = pixels, Layers = [] };
    }

    internal uint[] GetLayerPixels(string? layerId, bool writable = true)
    {
        if (layerId is null or BackgroundLayerId)
            return Pixels;
        var layer = Layers.FirstOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException($"Paint layer '{layerId}' was not found.", nameof(layerId));
        if (writable && layer.Locked)
            throw new InvalidOperationException($"Paint layer '{layerId}' is locked.");
        return layer.Pixels;
    }

    internal SkinDocument CopyForPreview(bool editMask) => this with
    {
        Pixels = (uint[])Pixels.Clone(),
        Layers = [.. Layers.Select(layer => layer.CopyPixels())],
        TransparencyMask = SkinTransparency.Copy(TransparencyMask, Pixels.Length, editMask),
        Undo = [],
        Redo = [],
    };

    internal SkinSnapshot CaptureState() => new((uint[])Pixels.Clone(), [.. Layers.Select(layer => layer.CopyPixels())], Model, Size, BackgroundName, Transaction,
        SkinTransparency.Copy(TransparencyMask, Pixels.Length));

    internal bool HasSameContent(SkinDocument other) =>
        Model == other.Model && Size == other.Size && BackgroundName == other.BackgroundName && Pixels.AsSpan().SequenceEqual(other.Pixels)
        && TransparencyMask.AsSpan().SequenceEqual(other.TransparencyMask) && Layers.Count == other.Layers.Count
        && Layers.Zip(other.Layers).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name
            && pair.First.Visible == pair.Second.Visible && pair.First.Opacity == pair.Second.Opacity && pair.First.Locked == pair.Second.Locked
            && pair.First.Pixels.AsSpan().SequenceEqual(pair.Second.Pixels));
}