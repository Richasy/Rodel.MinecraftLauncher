// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Identifies immutable, explicitly attached image bytes inside the launcher's managed AI store.</summary>
/// <param name="Id">The opaque snapshot identity, not an input or output path.</param>
/// <param name="Name">The player-visible file name without the source directory.</param>
/// <param name="MediaType">The PNG, JPEG or WebP media type detected from the snapshot.</param>
/// <param name="Length">The exact original byte length; images are not silently resized.</param>
/// <param name="Sha256">The content digest used to reject snapshot drift.</param>
public sealed record AiChatImage(string Id, string Name, string MediaType, int Length, string Sha256);

internal sealed record AiImageSnapshot(AiChatImage Image, byte[] Data);

/// <summary>Freezes reference images before turn submission so later file or clipboard changes cannot alter a request.</summary>
public sealed class AiImageStore(LauncherPaths paths)
{
    /// <summary>The product's maximum number of explicitly attached reference images in one turn.</summary>
    public const int MaximumImages = 3;

    /// <summary>Copies an explicitly selected local image into an immutable snapshot without changing the source.</summary>
    public async Task<AiChatImage> PrepareFileAsync(string filePath, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(Path.GetFullPath(filePath), ct).ConfigureAwait(false);
        return await PrepareAsync(bytes, Path.GetFileName(filePath), ct).ConfigureAwait(false);
    }

    /// <summary>Copies PNG, JPEG or WebP bytes into a snapshot; used for clipboard images and explicit file selection.</summary>
    public async Task<AiChatImage> PrepareAsync(ReadOnlyMemory<byte> bytes, string name, CancellationToken ct = default)
        => await PrepareAsync(bytes, name, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);

    /// <summary>Freezes explicit files in stable submission slots so an ambiguous receipt can be recovered with the same client key.</summary>
    /// <param name="filePaths">The explicitly selected files in their submitted order, at most three.</param>
    /// <param name="conversationId">The conversation owning this submission, not an additional file authorization.</param>
    /// <param name="clientKey">The submission identity; changing a previously frozen file produces an explicit conflict.</param>
    /// <param name="ct">Cancels file reads or atomic snapshot publication.</param>
    /// <returns>The original immutable references for unchanged inputs, in their submitted order.</returns>
    public async Task<AiChatImage[]> PrepareSubmissionAsync(IReadOnlyList<string> filePaths, string conversationId, string clientKey, CancellationToken ct = default)
    {
        var sources = filePaths.ToArray();
        if (sources.Length > MaximumImages) throw new ArgumentException("Attach at most three reference images to one turn.", nameof(filePaths));
        var images = new List<AiChatImage>();
        for (var index = 0; index < sources.Length; index++)
        {
            var binding = Encoding.UTF8.GetBytes(conversationId + "\n" + clientKey + "\n" + index.ToString(CultureInfo.InvariantCulture));
            var id = Convert.ToHexString(SHA256.HashData(binding).AsSpan(0, 16)).ToLowerInvariant();
            var bytes = await File.ReadAllBytesAsync(Path.GetFullPath(sources[index]), ct).ConfigureAwait(false);
            images.Add(await PrepareAsync(bytes, Path.GetFileName(sources[index]), id, ct).ConfigureAwait(false));
        }

        return [.. images];
    }

    private async Task<AiChatImage> PrepareAsync(ReadOnlyMemory<byte> bytes, string name, string id, CancellationToken ct)
    {
        var data = bytes.ToArray();
        var mediaType = MediaType(data);
        var image = new AiChatImage(id, Path.GetFileName(name), mediaType, data.Length, Convert.ToHexString(SHA256.HashData(data)));
        using var lease = await ResourceLease.AcquireAsync(FilePath(id), ct).ConfigureAwait(false);
        if (File.Exists(FilePath(id)))
        {
            var previous = Read(id);
            if (previous.Image != image || !previous.Data.AsSpan().SequenceEqual(data))
                throw new LauncherOperationException("AI_IDEMPOTENCY_CONFLICT", "This client key already belongs to different reference image content. Reuse the original input or use a new client key.");
            return previous.Image;
        }

        await JsonFiles.WriteAsync(FilePath(id), JsonSerializer.SerializeToNode(new AiImageSnapshot(image, data),
            AiImageJsonContext.Default.AiImageSnapshot)!, ct).ConfigureAwait(false);
        return image;
    }

    /// <summary>Reads snapshot metadata and verifies that the original reference image bytes still match it.</summary>
    public AiChatImage Describe(string id) => Read(id).Image;

    /// <summary>Returns caller-owned original image bytes for a thumbnail without reading the former source file.</summary>
    public byte[] ReadBytes(string id) => Read(id).Data;

    internal ChatMessage Message(string prompt, IEnumerable<AiChatImage> images)
    {
        var message = new ChatMessage(ChatRole.User, prompt);
        foreach (var image in images)
        {
            var snapshot = Read(image.Id);
            if (snapshot.Image != image)
                throw new LauncherOperationException("AI_IMAGE_SNAPSHOT_CHANGED", "A reference image no longer matches the submitted snapshot.");
            message.Contents.Add(new DataContent(snapshot.Data, image.MediaType) { Name = image.Name });
        }

        return message;
    }

    private AiImageSnapshot Read(string id)
    {
        var snapshot = JsonFiles.ReadObject(FilePath(id)).Deserialize(AiImageJsonContext.Default.AiImageSnapshot)
            ?? throw new InvalidDataException("The reference image snapshot is empty.");
        if (snapshot.Image.Id != id || snapshot.Image.Length != snapshot.Data.Length
            || snapshot.Image.MediaType != MediaType(snapshot.Data)
            || snapshot.Image.Sha256 != Convert.ToHexString(SHA256.HashData(snapshot.Data)))
        {
            throw new LauncherOperationException("AI_IMAGE_SNAPSHOT_CHANGED", "A reference image no longer matches its saved content digest.");
        }

        return snapshot;
    }

    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Specify a managed image snapshot identifier.", nameof(id));
        return Path.Combine(paths.Root, "ai", "images", id + ".json");
    }
    private static string MediaType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && data.Slice(12, 4).SequenceEqual("IHDR"u8)) return "image/png";
        if (data.Length >= 4 && data[0] == 255 && data[1] == 216 && data[2] == 255) return "image/jpeg";
        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new ArgumentException("Reference images must contain PNG, JPEG or WebP image data.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiImageSnapshot))]
internal sealed partial class AiImageJsonContext : JsonSerializerContext;