// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Reads the actual original game and packaged adapter bytecode requirements without changing game Java preferences.</summary>
internal static class SeedRuntimeJava
{
    internal static int RequiredMajor(JsonObject metadata, string clientJar, byte[] adapter)
    {
        var required = int.TryParse(metadata["javaVersion"]?["majorVersion"]?.ToString(), out var declared) && declared > 0 ? declared : 8;
        using (var original = ZipFile.OpenRead(clientJar)) required = Math.Max(required, RequiredMajor(original));
        using var bytes = new MemoryStream(adapter, writable: false);
        using var worker = new ZipArchive(bytes, ZipArchiveMode.Read);
        return Math.Max(8, Math.Max(required, RequiredMajor(worker)));
    }

    private static int RequiredMajor(ZipArchive archive)
    {
        var required = 0;
        var count = 0;
        Span<byte> header = stackalloc byte[8];
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".class", StringComparison.Ordinal) || entry.FullName.StartsWith("META-INF/versions/", StringComparison.Ordinal)) continue;
            if (++count > 50000 || entry.Length < header.Length) throw new InvalidDataException("The original Java class headers exceed the bounded adapter scan.");
            using var input = entry.Open();
            input.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0xcafebabe) throw new InvalidDataException("The original runtime contains an invalid Java class header.");
            var major = BinaryPrimitives.ReadUInt16BigEndian(header[6..]);
            if (major < 45) throw new InvalidDataException("The original runtime declares an invalid Java class version.");
            required = Math.Max(required, major - 44);
        }

        return required;
    }
}