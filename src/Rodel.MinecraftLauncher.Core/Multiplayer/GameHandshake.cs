// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Restores the original server address in a proxied first handshake, leaving gameplay and mod suffixes opaque.</summary>
internal static class GameHandshake
{
    internal static async Task ForwardAsync(Stream game, Stream upstream, string host, int port, GameStatusProtocol protocolKind, CancellationToken ct)
    {
        var first = new byte[1];
        await game.ReadExactlyAsync(first, ct).ConfigureAwait(false);
        if (protocolKind == GameStatusProtocol.Legacy && first[0] == 0xfe) // Preserve legacy status negotiation.
        {
            await upstream.WriteAsync(first, ct).ConfigureAwait(false);
            return;
        }

        if (protocolKind is GameStatusProtocol.Legacy or GameStatusProtocol.Unknown && first[0] == 2)
        {
            // A two-byte modern frame cannot contain a login handshake; legacy login remains
            // identifiable when the remote server disables status queries.
            // 1.3–1.6: packet 02, protocol byte, UTF-16 username, UTF-16 host, int port.
            var protocol = new byte[1];
            await game.ReadExactlyAsync(protocol, ct).ConfigureAwait(false);
            var username = await LegacyStringAsync(game, ct).ConfigureAwait(false);
            var address = await LegacyStringAsync(game, ct).ConfigureAwait(false);
            await game.ReadExactlyAsync(new byte[4], ct).ConfigureAwait(false);
            var original = Encoding.BigEndianUnicode.GetString(address.AsSpan(2));
            var separator = original.IndexOf('\0');
            var replacement = host + (separator < 0 ? string.Empty : original[separator..]);
            if (replacement.Length > 32767) throw new InvalidDataException("Legacy handshake host is too long.");
            using var output = new MemoryStream();
            output.Write(first); output.Write(protocol); output.Write(username);
            var size = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(size, (ushort)replacement.Length);
            output.Write(size); output.Write(Encoding.BigEndianUnicode.GetBytes(replacement));
            var destination = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(destination, port);
            output.Write(destination);
            await upstream.WriteAsync(output.ToArray(), ct).ConfigureAwait(false);
            return;
        }

        var header = new List<byte> { first[0] };
        while ((header[^1] & 128) != 0 && header.Count < 5)
        {
            await game.ReadExactlyAsync(first, ct).ConfigureAwait(false);
            header.Add(first[0]);
        }

        var offset = 0;
        var length = ReadVarInt([.. header], ref offset);
        if (length is < 1 or > 65536) throw new InvalidDataException("Invalid first game frame.");
        var body = new byte[length];
        await game.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        offset = 0;
        if (ReadVarInt(body, ref offset) != 0)
        {
            await upstream.WriteAsync(header.ToArray(), ct).ConfigureAwait(false);
            await upstream.WriteAsync(body, ct).ConfigureAwait(false);
            return;
        }

        _ = ReadVarInt(body, ref offset); // Protocol number is copied without interpretation.
        var hostLengthOffset = offset;
        var hostLength = ReadVarInt(body, ref offset);
        if (hostLength < 0 || hostLength > body.Length - offset - 3) throw new InvalidDataException("Invalid handshake address.");
        var suffixOffset = Array.IndexOf(body, (byte)0, offset, hostLength);
        var suffixLength = suffixOffset < 0 ? 0 : offset + hostLength - suffixOffset;
        var replacementHost = Encoding.UTF8.GetBytes(host);
        using var rewritten = new MemoryStream();
        rewritten.Write(body.AsSpan(0, hostLengthOffset));
        WriteVarInt(rewritten, replacementHost.Length + suffixLength);
        rewritten.Write(replacementHost);
        if (suffixLength > 0) rewritten.Write(body.AsSpan(suffixOffset, suffixLength));
        rewritten.Write([(byte)(port >> 8), (byte)port]);
        rewritten.Write(body.AsSpan(offset + hostLength + 2));
        using var frame = new MemoryStream();
        WriteVarInt(frame, checked((int)rewritten.Length));
        rewritten.Position = 0;
        rewritten.CopyTo(frame);
        await upstream.WriteAsync(frame.ToArray(), ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> LegacyStringAsync(Stream stream, CancellationToken ct)
    {
        var size = new byte[2];
        await stream.ReadExactlyAsync(size, ct).ConfigureAwait(false);
        var characters = BinaryPrimitives.ReadUInt16BigEndian(size);
        if (characters > 32767) throw new InvalidDataException("Legacy handshake string is too long.");
        var value = new byte[2 + (characters * 2)];
        size.CopyTo(value, 0);
        await stream.ReadExactlyAsync(value.AsMemory(2), ct).ConfigureAwait(false);
        return value;
    }

    private static int ReadVarInt(byte[] bytes, ref int offset)
    {
        var number = 0;
        for (var shift = 0; shift < 35 && offset < bytes.Length; shift += 7)
        {
            var part = bytes[offset++];
            if (shift == 28 && part > 15) throw new InvalidDataException("Oversized game VarInt.");
            number |= (part & 127) << shift;
            if ((part & 128) == 0) return number;
        }

        throw new InvalidDataException("Invalid game VarInt.");
    }

    private static void WriteVarInt(Stream stream, int number)
    {
        var value = unchecked((uint)number);
        do { var part = (byte)(value & 127); value >>= 7; stream.WriteByte(value == 0 ? part : (byte)(part | 128)); } while (value != 0);
    }
}