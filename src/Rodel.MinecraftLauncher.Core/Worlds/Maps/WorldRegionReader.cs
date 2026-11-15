// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Identifies one observed source file; changes revoke its session instead of mixing saved revisions.</summary>
/// <param name="Path">Observed absolute source file.</param>
/// <param name="Length">Observed byte length.</param>
/// <param name="Modified">Observed UTC modification time.</param>
internal sealed record WorldFileStamp(string Path, long Length, DateTime Modified)
{
    internal static WorldFileStamp Read(string path)
    {
        var info = new FileInfo(path);
        return new(path, info.Length, info.LastWriteTimeUtc);
    }

    internal void EnsureUnchanged()
    {
        var info = new FileInfo(Path);
        if (!info.Exists || info.Length != Length || info.LastWriteTimeUtc != Modified)
            throw new WorldSnapshotChangedException();
    }
}

/// <summary>Revokes a stale read without replacing the last complete visible map.</summary>
internal sealed class WorldSnapshotChangedException() : IOException("The save changed while it was being read. Refresh the world map to read its new saved data.");

/// <summary>Reads Anvil/McRegion/external chunks and legacy per-chunk files through ordinary bounded file handles.</summary>
internal static class WorldRegionReader
{
    internal static async Task<SavedChunk?> ReadAsync(string dimension, int x, int z, Dictionary<string, WorldFileStamp> files,
        CancellationToken ct, bool containers = false)
    {
        var region = "r." + (x >> 5) + "." + (z >> 5);
        foreach (var extension in new[] { ".mca", ".mcr" })
        {
            var path = Path.Combine(dimension, "region", region + extension);
            if (!files.TryGetValue(path, out var stamp)) continue;
            stamp.EnsureUnchanged();
            var root = await ReadRegionAsync(stamp, x, z, files, containers, ct).ConfigureAwait(false);
            stamp.EnsureUnchanged();
            if (root is not null) return new(root, x, z);
        }

        var legacy = Path.Combine(dimension, Base36(x & 63), Base36(z & 63), "c." + Base36(x) + "." + Base36(z) + ".dat");
        if (!files.TryGetValue(legacy, out var old))
        {
            if (!File.Exists(legacy)) return null;
            old = WorldFileStamp.Read(legacy);
            files[legacy] = old;
        }

        old.EnsureUnchanged();
        var chunk = await WorldNbt.ReadFileAsync(legacy, ct, map: true, containers: containers).ConfigureAwait(false);
        old.EnsureUnchanged();
        return new(chunk, x, z);
    }

    private static async Task<Dictionary<string, object>?> ReadRegionAsync(WorldFileStamp stamp, int x, int z,
        Dictionary<string, WorldFileStamp> files, bool containers, CancellationToken ct)
    {
        var file = new FileStream(stamp.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        await using var disposal = file.ConfigureAwait(false);
        var location = new byte[4];
        file.Position = (((z & 31) * 32) + (x & 31)) * 4;
        await file.ReadExactlyAsync(location, ct).ConfigureAwait(false);
        var sector = (location[0] << 16) | (location[1] << 8) | location[2];
        var sectors = location[3];
        if (sector == 0 && sectors == 0) return null;
        if (sector < 2 || sectors == 0 || ((long)sector + sectors) * 4096 > file.Length)
            throw new InvalidDataException("The saved region contains an invalid chunk location.");
        file.Position = (long)sector * 4096;
        var header = new byte[5];
        await file.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1 || length > (sectors * 4096) - 4 || length > WorldNbt.MaximumBytes)
            throw new InvalidDataException("The saved region contains an invalid chunk length.");
        byte[] payload;
        var compression = header[4];
        if ((compression & 128) != 0)
        {
            var external = Path.Combine(Path.GetDirectoryName(stamp.Path)!, "c." + x + "." + z + ".mcc");
            if (!files.TryGetValue(external, out var externalStamp)) throw new WorldSnapshotChangedException();
            externalStamp.EnsureUnchanged();
            if (externalStamp.Length > WorldNbt.MaximumBytes) throw new InvalidDataException("The external chunk exceeds the world read size.");
            payload = await File.ReadAllBytesAsync(external, ct).ConfigureAwait(false);
            externalStamp.EnsureUnchanged();
        }
        else
        {
            payload = new byte[length - 1];
            await file.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        }

        using var input = new MemoryStream(payload, writable: false);
        using Stream decoded = (compression & 127) switch
        {
            1 => new GZipStream(input, CompressionMode.Decompress),
            2 => new ZLibStream(input, CompressionMode.Decompress),
            3 => new MemoryStream(payload, writable: false),
            4 => new MemoryStream(ReadLz4(payload), writable: false),
            _ => throw new InvalidDataException("The saved chunk uses an unknown compression method."),
        };
        return WorldNbt.Parse(await WorldNbt.ReadBytesAsync(decoded, ct).ConfigureAwait(false), map: true, containers: containers);
    }

    private static byte[] ReadLz4(byte[] input)
    {
        using var output = new MemoryStream();
        var offset = 0;
        while (offset < input.Length)
        {
            if (input.Length - offset < 21 || !input.AsSpan(offset, 8).SequenceEqual("LZ4Block"u8))
                throw new InvalidDataException("Invalid saved LZ4 block header.");
            var method = input[offset + 8] & 0xf0;
            var compressed = BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(offset + 9));
            var length = BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(offset + 13));
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(offset + 17));
            offset += 21;
            if (compressed == 0 && length == 0) return output.ToArray();
            if (compressed < 0 || length <= 0 || length > WorldNbt.MaximumBytes - output.Length || compressed > input.Length - offset)
                throw new InvalidDataException("Invalid saved LZ4 block size.");
            var block = new byte[length];
            var source = input.AsSpan(offset, compressed);
            if (method == 0x10 && compressed == length) source.CopyTo(block);
            else if (method == 0x20) DecodeLz4Block(source, block);
            else throw new InvalidDataException("Invalid saved LZ4 compression method.");
            if (XxHash(block) != checksum) throw new InvalidDataException("The saved LZ4 block checksum is invalid.");
            output.Write(block);
            offset += compressed;
        }

        throw new InvalidDataException("The saved LZ4 stream is truncated.");
    }

    private static void DecodeLz4Block(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var read = 0; var write = 0;
        while (read < input.Length)
        {
            var token = input[read++];
            var literal = token >> 4;
            if (literal == 15) literal += Extended(input, ref read);
            if (literal > input.Length - read || literal > output.Length - write) throw new InvalidDataException("Invalid LZ4 literal range.");
            input.Slice(read, literal).CopyTo(output[write..]);
            read += literal; write += literal;
            if (read == input.Length) break;
            if (input.Length - read < 2) throw new InvalidDataException("Invalid LZ4 match offset.");
            var distance = BinaryPrimitives.ReadUInt16LittleEndian(input[read..]); read += 2;
            var match = token & 15;
            if (match == 15) match += Extended(input, ref read);
            match += 4;
            if (distance == 0 || distance > write || match > output.Length - write) throw new InvalidDataException("Invalid LZ4 match range.");
            for (var i = 0; i < match; i++) { output[write] = output[write - distance]; write++; }
        }

        if (write != output.Length) throw new InvalidDataException("The LZ4 output is incomplete.");
    }

    private static int Extended(ReadOnlySpan<byte> input, ref int position)
    {
        var length = 0;
        while (true)
        {
            if (position >= input.Length || length > WorldNbt.MaximumBytes) throw new InvalidDataException("Invalid LZ4 extended length.");
            var next = input[position++]; length += next;
            if (next != 255) return length;
        }
    }

    private static uint XxHash(ReadOnlySpan<byte> bytes)
    {
        const uint p1 = 2654435761, p2 = 2246822519, p3 = 3266489917, p4 = 668265263, p5 = 374761393, seed = 0x9747b28c;
        static uint Round(uint value, uint input) => BitOperations.RotateLeft(value + (input * p2), 13) * p1;
        var offset = 0;
        uint hash;
        if (bytes.Length >= 16)
        {
            var a = unchecked(seed + p1 + p2); var b = unchecked(seed + p2); var c = seed; var d = unchecked(seed - p1);
            while (offset <= bytes.Length - 16)
            {
                a = Round(a, BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]));
                b = Round(b, BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]));
                c = Round(c, BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 8)..]));
                d = Round(d, BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 12)..])); offset += 16;
            }

            hash = BitOperations.RotateLeft(a, 1) + BitOperations.RotateLeft(b, 7) + BitOperations.RotateLeft(c, 12) + BitOperations.RotateLeft(d, 18);
        }
        else
        {
            hash = unchecked(seed + p5);
        }

        hash += (uint)bytes.Length;
        while (offset <= bytes.Length - 4)
        {
            hash = BitOperations.RotateLeft(hash + (BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]) * p3), 17) * p4;
            offset += 4;
        }

        while (offset < bytes.Length) hash = BitOperations.RotateLeft(hash + (bytes[offset++] * p5), 11) * p1;
        hash ^= hash >> 15; hash *= p2; hash ^= hash >> 13; hash *= p3; hash ^= hash >> 16;
        return hash;
    }

    private static string Base36(int value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";
        var number = Math.Abs((long)value);
        var text = string.Empty;
        while (number != 0) { text = digits[(int)(number % 36)] + text; number /= 36; }

        return value < 0 ? "-" + text : text;
    }
}