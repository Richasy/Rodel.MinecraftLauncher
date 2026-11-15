// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Reads actual finite-world blocks; original formats with no recorded seed retain that absence.</summary>
internal sealed class FiniteWorld(int width, int height, int length, byte[] blocks, string? name, SeedPosition? spawn)
{
    internal string? Name { get; } = name;
    internal SeedPosition? Spawn { get; } = spawn;
    internal int Height => height;

    internal (string Block, string Biome, int Y, int Fluid)? Sample(int x, int z, int? slice)
    {
        if (x < 0 || z < 0 || x >= width || z >= length || slice < 0 || slice >= height) return null;
        var water = 0;
        for (var y = slice ?? (height - 1); y >= 0; y--)
        {
            var id = blocks[(((y * length) + z) * width) + x];
            var fluid = id is 8 or 9 ? 1 : id is 10 or 11 ? 2 : 0;
            if (slice is not null) return (SavedChunk.LegacyBlock(id), "unknown", y, fluid);
            if (fluid != 0) { water = fluid; continue; }

            if (id is not 0 and not 6 and not 37 and not 38 and not 39 and not 40)
                return (SavedChunk.LegacyBlock(id), "unknown", y, water);
        }

        return ("air", "unknown", 0, water);
    }

    internal static async Task<FiniteWorld> ReadAsync(string path, CancellationToken ct)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
        await using var disposal = source.ConfigureAwait(false);
        var prefix = new byte[2]; await source.ReadExactlyAsync(prefix, ct).ConfigureAwait(false); source.Position = 0;
        using var gzip = prefix[0] == 31 && prefix[1] == 139 ? new GZipStream(source, CompressionMode.Decompress, true) : null;
        var bytes = await WorldNbt.ReadBytesAsync(gzip ?? (Stream)source, ct).ConfigureAwait(false);
        if (bytes[0] == 10)
        {
            var root = WorldNbt.Parse(bytes); var map = root.Compound("Map");
            SeedPosition? spawn = null;
            if (map.GetValueOrDefault("Spawn") is List<object> { Count: 3 } pos
                && WorldNbt.Number(pos[0]) is { } x && WorldNbt.Number(pos[1]) is { } y && WorldNbt.Number(pos[2]) is { } z)
            {
                spawn = new() { Id = "spawn", Kind = "spawn", X = checked((int)x), Y = checked((int)y), Z = checked((int)z) };
            }

            return Create(map.Integer("Width"), map.Integer("Height"), map.Integer("Length"), map.GetValueOrDefault("Blocks") as byte[], root.Compound("About").Text("Name"), spawn);
        }

        if (bytes.Length >= 5 && BinaryPrimitives.ReadInt32BigEndian(bytes) == 0x271bb788)
        {
            if (bytes[4] == 2)
            {
                var data = ClassicObjectData.Read(bytes.AsMemory(5));
                var spawn = data.Integer("xSpawn") is { } x && data.Integer("ySpawn") is { } y && data.Integer("zSpawn") is { } z
                    ? new SeedPosition { Id = "spawn", Kind = "spawn", X = checked((int)x), Y = checked((int)y), Z = checked((int)z) } : null;
                // Classic's field names precede the current axis convention: depth is vertical.
                return Create(data.Integer("width"), data.Integer("depth"), data.Integer("height"), data.GetValueOrDefault("blocks") as byte[], data.Text("name"), spawn);
            }

            if (bytes[4] == 1)
            {
                var offset = 5;
                string ReadText() { var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)); offset += 2; var value = System.Text.Encoding.UTF8.GetString(bytes, offset, count); offset += count; return value; }

                var name = ReadText(); _ = ReadText(); offset += 8;
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2));
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4));
                return Create(width, height, length, bytes[(offset + 6)..], name, null);
            }

            throw new InvalidDataException("Unknown Classic world stream format.");
        }

        if (bytes.Length == 256 * 64 * 256) return Create(256, 64, 256, bytes, null, null);
        throw new InvalidDataException("The finite world has no recognized stored map shape.");
    }

    private static FiniteWorld Create(long? width, long? height, long? length, byte[]? blocks, string? name, SeedPosition? spawn)
    {
        if (width is not (> 0 and <= 32767) || height is not (> 0 and <= 32767) || length is not (> 0 and <= 32767)
            || blocks is null || width * height * length != blocks.Length)
        {
            throw new InvalidDataException("The finite world's dimensions do not match its bounded block array.");
        }

        return new((int)width, (int)height, (int)length, blocks, name, spawn);
    }
}