// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Bounded JSON framing shared by actual AI-runtime and App-control pipe consumers.</summary>
internal static class FramedJsonPipe
{
    internal const int MaximumFrameBytes = 2 * 1024 * 1024;

    internal static async Task<T> ReadAsync<T>(Stream stream, JsonTypeInfo<T> type, string errorPrefix, CancellationToken ct, int maximumFrameBytes = MaximumFrameBytes)
    {
        var bytes = await ReadBytesAsync(stream, errorPrefix, ct, maximumFrameBytes).ConfigureAwait(false);
        return JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("A JSON object is required.");
    }

    internal static Task WriteAsync<T>(Stream stream, T frame, JsonTypeInfo<T> type, string errorPrefix, CancellationToken ct)
        => WriteBytesAsync(stream, JsonSerializer.SerializeToUtf8Bytes(frame, type), errorPrefix, ct);

    internal static async Task<JsonObject> ReadAsync(Stream stream, string errorPrefix, CancellationToken ct)
    {
        var bytes = await ReadBytesAsync(stream, errorPrefix, ct).ConfigureAwait(false);
        return JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new LauncherOperationException(errorPrefix + "_FRAME_INVALID", "Expected a host JSON object.");
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, string errorPrefix, CancellationToken ct, int maximumFrameBytes = MaximumFrameBytes)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > Math.Min(maximumFrameBytes, MaximumFrameBytes))
            throw new LauncherOperationException(errorPrefix + "_FRAME_INVALID", "The host frame exceeds the supported message size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return bytes;
    }

    internal static async Task WriteAsync(Stream stream, JsonObject frame, string errorPrefix, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) frame.WriteTo(writer);
        await WriteBytesAsync(stream, buffer.GetBuffer().AsMemory(0, (int)buffer.Length), errorPrefix, ct).ConfigureAwait(false);
    }

    private static async Task WriteBytesAsync(Stream stream, ReadOnlyMemory<byte> bytes, string errorPrefix, CancellationToken ct)
    {
        if (bytes.Length is <= 0 or > MaximumFrameBytes)
            throw new LauncherOperationException(errorPrefix + "_FRAME_TOO_LARGE", "The response is too large for one host frame.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}