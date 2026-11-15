// Copyright (c) Richasy. All rights reserved.

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>Encodes the cropped premultiplied BGRA image with the Windows PNG codec.</summary>
internal static class ImagePng
{
    internal static async Task<byte[]> EncodeAsync(byte[] pixels)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            ImageCrop.OutputSize,
            ImageCrop.OutputSize,
            96,
            96,
            pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[checked((int)stream.Size)];
        reader.ReadBytes(bytes);
        return bytes;
    }
}