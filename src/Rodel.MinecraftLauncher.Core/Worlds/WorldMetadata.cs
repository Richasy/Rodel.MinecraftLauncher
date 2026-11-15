// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Worlds;

internal static class WorldMetadata
{
    internal static async Task ReadAsync(string file, WorldSummary result, CancellationToken ct)
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var first = input.ReadByte();
        var second = input.ReadByte();
        input.Position = 0;
        using var compressed = first == 0x1f && second == 0x8b ? new GZipStream(input, CompressionMode.Decompress, leaveOpen: true) : null;
        using var buffer = new MemoryStream();
        var block = new byte[65536];
        var stream = compressed ?? (Stream)input;
        while (true)
        {
            var count = await stream.ReadAsync(block, ct).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > 64 * 1024 * 1024)
                throw new InvalidDataException("World metadata exceeds the 64 MiB read limit.");
            buffer.Write(block, 0, count);
        }

        new Reader(buffer.ToArray(), result, ct).Read();
    }

    private sealed class Reader(byte[] bytes, WorldSummary result, CancellationToken ct)
    {
        private int _position;
        private ReadOnlySpan<byte> Take(int count)
        {
            ct.ThrowIfCancellationRequested();
            if (count < 0 || count > bytes.Length - _position) throw new InvalidDataException("Truncated NBT value.");
            var value = bytes.AsSpan(_position, count);
            _position += count;
            return value;
        }
        private byte Byte() => Take(1)[0];
        private int Int() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
        private string Text()
        {
            var data = Take(BinaryPrimitives.ReadUInt16BigEndian(Take(2)));
            var text = new StringBuilder(data.Length);
            for (var i = 0; i < data.Length;)
            {
                var a = data[i++];
                if (a < 128)
                {
                    text.Append((char)a);
                }
                else if ((a & 0xe0) == 0xc0 && i < data.Length)
                {
                    text.Append((char)(((a & 31) << 6) | (data[i++] & 63)));
                }
                else if ((a & 0xf0) == 0xe0 && i + 1 < data.Length)
                {
                    text.Append((char)(((a & 15) << 12) | ((data[i] & 63) << 6) | (data[i + 1] & 63)));
                    i += 2;
                }
                else if ((a & 0xf8) == 0xf0 && i + 2 < data.Length)
                {
                    var scalar = ((a & 7) << 18) | ((data[i] & 63) << 12) | ((data[i + 1] & 63) << 6) | (data[i + 2] & 63);
                    text.Append(char.ConvertFromUtf32(scalar));
                    i += 3;
                }
                else
                {
                    throw new InvalidDataException("Invalid NBT string.");
                }
            }

            return text.ToString();
        }
        internal void Read()
        {
            if (Byte() != 10) throw new InvalidDataException("World metadata is not an NBT compound.");
            _ = Text();
            Payload(10, string.Empty, 0);
        }
        private void Payload(byte type, string path, int depth)
        {
            if (depth > 128) throw new InvalidDataException("NBT nesting exceeds the read limit.");
            switch (type)
            {
                case 1:
                    var b = Byte();
                    if (path == "Data/hardcore") result.Hardcore = b != 0;
                    break;
                case 2: _ = Take(2); break;
                case 3:
                    var n = Int();
                    if (path == "Data/GameType") result.GameType = n;
                    if (path == "Data/DataVersion") result.DataVersion = n;
                    break;
                case 4:
                    var value = BinaryPrimitives.ReadInt64BigEndian(Take(8));
                    if (path == "Data/LastPlayed") result.LastPlayed = value;
                    break;
                case 5: _ = Take(4); break;
                case 6: _ = Take(8); break;
                case 7: _ = Take(Int()); break;
                case 8:
                    var text = Text();
                    if (path == "Data/LevelName") result.Name = text;
                    if (path == "Data/Version/Name") result.Version = text;
                    break;
                case 9:
                    var child = Byte();
                    var count = Int();
                    if (count < 0 || count > bytes.Length || (child == 0 && count != 0))
                        throw new InvalidDataException("Invalid NBT list length.");
                    for (var i = 0; i < count; i++) Payload(child, path + "/[]", depth + 1);
                    break;
                case 10:
                    while (true)
                    {
                        var tag = Byte();
                        if (tag == 0) break;
                        var name = Text();
                        Payload(tag, path.Length == 0 ? name : path + "/" + name, depth + 1);
                    }

                    break;
                case 11: _ = Take(checked(Int() * 4)); break;
                case 12: _ = Take(checked(Int() * 8)); break;
                default: throw new InvalidDataException($"Unsupported NBT tag {type}.");
            }
        }
    }
}