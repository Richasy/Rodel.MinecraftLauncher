// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.WindowsAppSDK;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Collects explicitly picked or pasted images while Core freezes and validates the actual reference bytes.</summary>
internal static class AiImagePicker
{
    private static string[] Extensions => [".png", ".jpg", ".jpeg", ".webp"];

    internal static async Task<AiChatImage[]> PickAsync(Window window, AssistantService service, int remaining)
    {
        var picker = window.CreateFileOpenPicker();
        foreach (var extension in Extensions)
            picker.FileTypeFilter.Add(extension);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
            return [];
        CheckCount(files.Count, remaining);
        return await FromFilesAsync(service, [.. files.Select(file => file.Path)], remaining);
    }

    internal static bool CanDrop(IReadOnlyList<string> files, int remaining) => files.Count > 0 && files.Count <= remaining
        && files.All(file => File.Exists(file) && Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

    internal static async Task<AiChatImage[]> FromFilesAsync(AssistantService service, IReadOnlyList<string> files, int remaining)
    {
        CheckCount(files.Count, remaining);
        var images = new List<AiChatImage>();
        foreach (var file in files)
            images.Add(await service.Images.PrepareFileAsync(file));
        return [.. images];
    }

    internal static bool HasClipboardImages()
    {
        var content = Clipboard.GetContent();
        return content.Contains(StandardDataFormats.Bitmap) || content.Contains(StandardDataFormats.StorageItems);
    }

    internal static async Task<AiChatImage[]?> TryPasteAsync(AssistantService service, int remaining)
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Bitmap))
        {
            CheckCount(1, remaining);
            var reference = await content.GetBitmapAsync();
            using var source = await reference.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(source);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            using var reader = new DataReader(output.GetInputStreamAt(0));
            await reader.LoadAsync(checked((uint)output.Size));
            var bytes = new byte[checked((int)output.Size)];
            reader.ReadBytes(bytes);
            return [await service.Images.PrepareAsync(bytes, Resources.SkinAiPastedImage)];
        }

        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var files = (await content.GetStorageItemsAsync()).OfType<StorageFile>().Where(file => Extensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (files.Length == 0)
                return null;
            CheckCount(files.Length, remaining);
            var images = new List<AiChatImage>();
            foreach (var file in files)
                images.Add(await service.Images.PrepareFileAsync(file.Path));
            return [.. images];
        }

        return null;
    }

    private static void CheckCount(int count, int remaining)
    {
        if (count > remaining)
            throw new InvalidOperationException(Resources.SkinAiImagesLimit);
    }
}