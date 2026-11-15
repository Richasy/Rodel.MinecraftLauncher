// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Buffers.Binary;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Owns validated original PNG bytes so preview, model selection and retries cannot observe a different source file.</summary>
public sealed class SkinPngImport
{
    private readonly byte[] _bytes;
    internal SkinPngImport(byte[] bytes, SkinModel model = SkinModel.Classic)
    {
        var document = SkinPng.Decode(bytes, model);
        _bytes = bytes;
        Model = model;
        Size = document.Size;
        SourceHeight = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
    }

    /// <summary>Gets the explicitly selected import model; a 64x32 cape is never guessed from a legacy skin.</summary>
    public SkinModel Model { get; }

    /// <summary>Gets the texture width; character legacy images become 64x64 and capes retain half-height.</summary>
    public int Size { get; }
    /// <summary>Gets the validated input height, including 32 for the legacy format.</summary>
    public int SourceHeight { get; }
    /// <summary>Gets whether the source uses the Classic-only 64x32 format.</summary>
    public bool IsLegacy => Model != SkinModel.Cape && SourceHeight == 32;
    /// <summary>Decodes an independent document from the original bytes using the explicit character or cape model; caller mutations never affect later decodes.</summary>
    public SkinDocument Decode(SkinModel model) => SkinPng.Decode(_bytes, model);

    /// <summary>Decodes original model-sized layer pixels without Java skin alpha normalization; legacy images are rejected.</summary>
    public SkinPixelBlock DecodeLayer() => SkinPng.DecodeLayer(_bytes, Model);
}