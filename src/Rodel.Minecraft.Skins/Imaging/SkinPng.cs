// Copyright (c) Richasy. All rights reserved.

using StbImageSharp;
using StbImageWriteSharp;
using System.Buffers.Binary;

namespace Rodel.Minecraft.Skins;

/// <summary>Imports standard Java skin PNGs and exports exact RGBA textures without native dependencies.</summary>
public static class SkinPng
{
    /// <summary>Decodes a character or rectangular cape atlas without changing alpha, RGB or atlas coordinates.</summary>
    public static SkinPixelBlock DecodeLayer(byte[] png, SkinModel model = SkinModel.Classic) => DecodePixels(png, allowLegacy: false, cape: model == SkinModel.Cape);

    /// <summary>Encodes one layer's original RGBA pixels at the document size, independently of visibility, lock and layer opacity.</summary>
    public static byte[] EncodeLayer(SkinDocument document, string layerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(layerId);
        return EncodePixels(SkinLayers.Pixels(document, layerId), document.Size, document.Height);
    }

    /// <summary>Encodes a validated character or cape document at its actual width and height, retaining alpha.</summary>
    public static byte[] Encode(SkinDocument document)
    {
        return EncodePixels(SkinCompositor.Pixels(document), document.Size, document.Height);
    }

    /// <summary>Exports at the original width or reduces an HD composite to width 64 using nearest-neighbor pixel centers; height follows the model and the source is unchanged.</summary>
    /// <param name="document">The committed layers to flatten before sampling.</param>
    /// <param name="size">The original dimension, or 64 for a reduced HD export.</param>
    /// <returns>A PNG retaining the sampled RGBA values without interpolation.</returns>
    public static byte[] Encode(SkinDocument document, int size)
    {
        if (size == document.Size) return Encode(document);
        if (size != 64 || document.Size != 128)
            throw new ArgumentOutOfRangeException(nameof(size), "Export at the original size or reduce a 128x128 skin to 64x64.");

        var source = SkinCompositor.Pixels(document);
        var pixels = new uint[64 * SkinLayout.Height(document.Model, 64)];
        for (var y = 0; y < SkinLayout.Height(document.Model, 64); y++)
        {
            for (var x = 0; x < 64; x++)
                pixels[(y * 64) + x] = source[(((y * 2) + 1) * 128) + (x * 2) + 1];
        }

        return EncodePixels(pixels, 64, SkinLayout.Height(document.Model, 64));
    }

    /// <summary>Imports character PNGs, including legacy 64x32 limbs, or explicitly selected 64x32/128x64 capes without character alpha normalization.</summary>
    public static SkinDocument Decode(byte[] png, SkinModel model)
    {
        var image = DecodePixels(png, allowLegacy: model != SkinModel.Cape, cape: model == SkinModel.Cape);
        var width = image.Width;
        var height = image.Height;
        if (model != SkinModel.Cape && height == 32 && model != SkinModel.Classic)
            throw new InvalidDataException("Legacy 64x32 skins use classic arms; convert before choosing slim.");
        var document = SkinDocument.Create(model, 0x808080FFu, width);
        image.Pixels.CopyTo(document.Pixels, 0);

        if (model != SkinModel.Cape) NormalizeSkin(document, height);
        document.Validate();
        return document;
    }

    private static SkinPixelBlock DecodePixels(byte[] png, bool allowLegacy, bool cape = false)
    {
        if (png.Length is < 33 or > 4194304 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("Choose a PNG skin no larger than 4 MiB.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (cape ? !((width == 64 || width == 128) && height == width / 2) : !((width == 64 && (height == 64 || (allowLegacy && height == 32))) || (width == 128 && height == 128)))
            throw new InvalidDataException(cape ? "Capes must be 64x32 or 128x64." : allowLegacy ? "Skins must be 64x64, 128x128, or legacy 64x32." : "Layer PNGs must be 64x64 or 128x128.");
        var image = ImageResult.FromMemory(png, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        if (image.Width != width || image.Height != height)
            throw new InvalidDataException("PNG decoded dimensions do not match its header.");
        var pixels = new uint[width * height];
        for (var i = 0; i < width * height; i++)
        {
            var offset = i * 4;
            pixels[i] = ((uint)image.Data[offset] << 24) | ((uint)image.Data[offset + 1] << 16)
                | ((uint)image.Data[offset + 2] << 8) | image.Data[offset + 3];
        }

        return new(width, height, pixels);
    }

    private static void NormalizeSkin(SkinDocument document, int height)
    {
        var model = document.Model;
        var width = document.Size;
        if (height == 32)
        {
            // Old RGB skins use an opaque unused hat region; Java clears it unless any pixel is transparent.
            var hasTransparency = Enumerable.Range(0, 32).Any(y =>
                Enumerable.Range(32, 32).Any(x => (document.Pixels[(y * 64) + x] & 255) < 128));
            if (!hasTransparency)
            {
                for (var y = 0; y < 32; y++)
                {
                    for (var x = 32; x < 64; x++)
                        document.Pixels[(y * 64) + x] &= 0xFFFFFF00;
                }
            }

            // Legacy format has a head overlay, but no body or limb outer layers.
            foreach (var part in new[] { SkinPart.LeftArm, SkinPart.LeftLeg })
            {
                foreach (var direction in Enum.GetValues<SkinFace>())
                {
                    var sourceDirection = direction switch { SkinFace.Left => SkinFace.Right, SkinFace.Right => SkinFace.Left, _ => direction };
                    var source = SkinLayout.Face(model, new() { Part = part == SkinPart.LeftArm ? SkinPart.RightArm : SkinPart.RightLeg, Layer = SkinLayer.Base, Face = sourceDirection });
                    var destination = SkinLayout.Face(model, new() { Part = part, Layer = SkinLayer.Base, Face = direction });
                    for (var y = 0; y < destination.Height; y++)
                    {
                        for (var x = 0; x < destination.Width; x++)
                        {
                            document.Pixels[((destination.Y + y) * 64) + destination.X + x] =
                                document.Pixels[((source.Y + y) * 64) + source.X + source.Width - 1 - x];
                        }
                    }
                }
            }

            foreach (var face in SkinLayout.Faces(model).Where(face => face.Surface.Layer == SkinLayer.Outer && face.Surface.Part != SkinPart.Head))
            {
                for (var y = 0; y < face.Height; y++)
                {
                    for (var x = 0; x < face.Width; x++)
                        document.Pixels[((face.Y + y) * 64) + face.X + x] = 0;
                }
            }
        }
        // Match Java's mandatory opaque base rather than rejecting otherwise usable imported skins.
        foreach (var face in SkinLayout.Faces(model, width).Where(face => face.Surface.Layer == SkinLayer.Base))
        {
            for (var y = 0; y < face.Height; y++)
            {
                for (var x = 0; x < face.Width; x++)
                    document.Pixels[((face.Y + y) * width) + face.X + x] |= 255;
            }
        }

    }

    internal static byte[] EncodePixels(uint[] pixels, int width, int height)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = (byte)(pixels[i] >> 24);
            rgba[(i * 4) + 1] = (byte)(pixels[i] >> 16);
            rgba[(i * 4) + 2] = (byte)(pixels[i] >> 8);
            rgba[(i * 4) + 3] = (byte)pixels[i];
        }

        using var output = new MemoryStream();
        new ImageWriter().WritePng(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        return output.ToArray();
    }
}