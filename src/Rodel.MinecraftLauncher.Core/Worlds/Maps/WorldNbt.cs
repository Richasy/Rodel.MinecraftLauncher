// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Reads bounded Java NBT without instantiating game types or rewriting the source document.</summary>
internal static class WorldNbt
{
    internal const int MaximumBytes = 64 * 1024 * 1024;

    internal static async Task<Dictionary<string, object>> ReadFileAsync(string path, CancellationToken ct, bool map = false, bool containers = false)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var disposal = file.ConfigureAwait(false);
        var prefix = new byte[2];
        await file.ReadExactlyAsync(prefix, ct).ConfigureAwait(false);
        file.Position = 0;
        using var gzip = prefix[0] == 0x1f && prefix[1] == 0x8b ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true) : null;
        return Parse(await ReadBytesAsync(gzip ?? (Stream)file, ct).ConfigureAwait(false), map, containers);
    }

    internal static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var block = new byte[65536];
        while (true)
        {
            var count = await stream.ReadAsync(block, ct).ConfigureAwait(false);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("NBT exceeds the bounded world read size.");
            buffer.Write(block, 0, count);
        }
    }

    internal static Dictionary<string, object> Parse(byte[] bytes, bool map = false, bool containers = false)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("NBT exceeds the bounded world read size.");
        return new Reader(bytes, map, containers).Read();
    }

    internal static Dictionary<string, object> Compound(this Dictionary<string, object> value, string key) =>
        value.GetValueOrDefault(key) as Dictionary<string, object> ?? [];

    internal static string? Text(this Dictionary<string, object> value, string key) => value.GetValueOrDefault(key) as string;

    internal static long? Integer(this Dictionary<string, object> value, string key) => Number(value.GetValueOrDefault(key));

    internal static long? Number(object? value) => value switch { sbyte n => n, short n => n, int n => n, long n => n, _ => null };

    internal static double? Decimal(object? value) => value switch
    {
        double n when double.IsFinite(n) => n,
        float n when float.IsFinite(n) => n,
        _ => Number(value),
    };

    private sealed class Reader(byte[] bytes, bool map, bool containers)
    {
        private int _position, _nodes;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > bytes.Length - _position) throw new InvalidDataException("Truncated Java NBT.");
            var result = bytes.AsSpan(_position, count);
            _position += count;
            return result;
        }

        private byte Byte() => Take(1)[0];
        private int Int() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

        private string Text()
        {
            var bytes = Take(BinaryPrimitives.ReadUInt16BigEndian(Take(2)));
            // Registry IDs and tag names dominate chunks and are ASCII; avoid a builder and second character buffer for each one.
            if (Ascii.IsValid(bytes)) return Encoding.UTF8.GetString(bytes);
            // Java DataInput uses modified UTF-8, including separately encoded surrogate code units.
            var result = new StringBuilder(bytes.Length);
            for (var i = 0; i < bytes.Length;)
            {
                var a = bytes[i++];
                if (a < 128)
                {
                    result.Append((char)a);
                }
                else if ((a & 0xe0) == 0xc0 && i < bytes.Length)
                {
                    result.Append((char)(((a & 31) << 6) | (bytes[i++] & 63)));
                }
                else if ((a & 0xf0) == 0xe0 && i + 1 < bytes.Length)
                {
                    result.Append((char)(((a & 15) << 12) | ((bytes[i] & 63) << 6) | (bytes[i + 1] & 63)));
                    i += 2;
                }
                else if ((a & 0xf8) == 0xf0 && i + 2 < bytes.Length)
                {
                    result.Append(char.ConvertFromUtf32(((a & 7) << 18) | ((bytes[i] & 63) << 12) | ((bytes[i + 1] & 63) << 6) | (bytes[i + 2] & 63)));
                    i += 3;
                }
                else
                {
                    throw new InvalidDataException("Invalid Java NBT text.");
                }
            }

            return result.ToString();
        }

        internal Dictionary<string, object> Read()
        {
            if (Byte() != 10) throw new InvalidDataException("The Java NBT root is not a compound.");
            _ = Text();
            return (Dictionary<string, object>)Payload(10, 0);
        }

        private int Length(int elementBytes)
        {
            var length = Int();
            if (length < 0 || (long)length * elementBytes > bytes.Length - _position)
                throw new InvalidDataException("Invalid Java NBT collection length.");
            return length;
        }

        private object Payload(byte type, int depth)
        {
            if (depth > 128 || ++_nodes > 2_000_000) throw new InvalidDataException("Java NBT exceeds the bounded nesting or node count.");
            switch (type)
            {
                case 1: return (sbyte)Byte();
                case 2: return BinaryPrimitives.ReadInt16BigEndian(Take(2));
                case 3: return Int();
                case 4: return BinaryPrimitives.ReadInt64BigEndian(Take(8));
                case 5: return BinaryPrimitives.ReadSingleBigEndian(Take(4));
                case 6: return BinaryPrimitives.ReadDoubleBigEndian(Take(8));
                case 7: return Take(Length(1)).ToArray();
                case 8: return Text();
                case 9:
                    var child = Byte();
                    var count = Length(1);
                    if (child == 0 && count != 0) throw new InvalidDataException("Invalid Java NBT list type.");
                    var items = new List<object>(count);
                    for (var i = 0; i < count; i++) items.Add(Payload(child, depth + 1));
                    return items;
                case 10:
                    var compound = new Dictionary<string, object>(StringComparer.Ordinal);
                    while (true)
                    {
                        var tag = Byte();
                        if (tag == 0) return compound;
                        var key = Text();
                        if ((map && key is ("BlockLight" or "SkyLight" or "Heightmaps" or "HeightMap" or "entities" or "Entities"
                            or "block_ticks" or "fluid_ticks" or "PostProcessing" or "TileTicks" or "UpgradeData" or "CarvingMasks"))
                            || (map && !containers && key is ("block_entities" or "TileEntities")))
                        {
                            Skip(tag, depth + 1);
                        }
                        else
                        {
                            compound[key] = Payload(tag, depth + 1);
                        }
                    }
                case 11:
                    var ints = new int[Length(4)];
                    for (var i = 0; i < ints.Length; i++) ints[i] = Int();
                    return ints;
                case 12:
                    var longs = new long[Length(8)];
                    for (var i = 0; i < longs.Length; i++) longs[i] = BinaryPrimitives.ReadInt64BigEndian(Take(8));
                    return longs;
                default: throw new InvalidDataException("Unknown Java NBT tag type.");
            }
        }

        private void Skip(byte type, int depth)
        {
            if (depth > 128 || ++_nodes > 2_000_000) throw new InvalidDataException("Java NBT exceeds the bounded nesting or node count.");
            switch (type)
            {
                case 1: _ = Take(1); break;
                case 2: _ = Take(2); break;
                case 3 or 5: _ = Take(4); break;
                case 4 or 6: _ = Take(8); break;
                case 7: _ = Take(Length(1)); break;
                case 8: _ = Take(BinaryPrimitives.ReadUInt16BigEndian(Take(2))); break;
                case 9:
                    var child = Byte(); var count = Length(1);
                    if (child == 0 && count != 0) throw new InvalidDataException("Invalid Java NBT list type.");
                    for (var i = 0; i < count; i++) Skip(child, depth + 1);
                    break;
                case 10:
                    while (true)
                    {
                        var tag = Byte(); if (tag == 0) break;
                        _ = Text(); Skip(tag, depth + 1);
                    }

                    break;
                case 11: _ = Take(Length(4) * 4); break;
                case 12: _ = Take(Length(8) * 8); break;
                default: throw new InvalidDataException("Unknown Java NBT tag type.");
            }
        }
    }
}