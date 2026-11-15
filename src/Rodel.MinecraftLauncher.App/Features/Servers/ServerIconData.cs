// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

/// <summary>Bounds the optional SLP PNG before handing it to the application's image decoder.</summary>
internal static class ServerIconData
{
    private const string Prefix = "data:image/png;base64,";
    private const int MaximumBytes = 64 * 1024;

    internal static byte[] Parse(string value)
    {
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || value.Length - Prefix.Length > (MaximumBytes + 2) / 3 * 4)
        {
            throw new InvalidDataException("Server favicon must be a bounded PNG data URI.");
        }

        var bytes = Convert.FromBase64String(value[Prefix.Length..]);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length is < 45 or > MaximumBytes || !bytes.AsSpan(0, 8).SequenceEqual(signature) || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) != 64 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) != 64)
        {
            throw new InvalidDataException("Server favicon must declare a 64 by 64 PNG image.");
        }

        return bytes;
    }
}