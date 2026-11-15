// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;

namespace Rodel.MinecraftLauncher.Core.Multiplayer;

/// <summary>Reads and creates native Scaffolding room codes without interpreting adjacent text as environment metadata.</summary>
public static class RoomCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Extracts the first valid code, accepting the native ASCII case and I/O aliases; null means no valid code.</summary>
    public static string? Parse(string text)
    {
        for (var start = 0; start + 21 <= text.Length; start++)
        {
            if (text[start] is not ('U' or 'u') || text[start + 1] != '/')
                continue;
            UInt128 value = 0;
            var valid = true;
            for (var index = 20; index >= 2; index--)
            {
                if (index is 6 or 11 or 16)
                {
                    if (text[start + index] != '-') valid = false;
                    continue;
                }

                var character = text[start + index];
                if (character is >= 'a' and <= 'z') character = (char)(character - 'a' + 'A');
                character = character switch { 'I' => '1', 'O' => '0', _ => character };
                var digit = Alphabet.IndexOf(character);
                if (digit < 0) { valid = false; break; }

                value = (value * 34) + (uint)digit;
            }

            if (valid && value % 7 == 0)
                return Format(value);
        }

        return null;
    }

    /// <summary>Creates a cryptographically random native code using the upstream Base34 and divisibility rule.</summary>
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        UInt128 modulus = 1;
        for (var index = 0; index < 16; index++) modulus *= 34;
        var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt128BigEndian(bytes) % modulus;
        return Format(value - (value % 7));
    }

    private static string Format(UInt128 value)
    {
        Span<char> result = stackalloc char[21];
        result[0] = 'U'; result[1] = '/';
        for (var index = 2; index < result.Length; index++)
        {
            if (index is 6 or 11 or 16)
            {
                result[index] = '-';
            }
            else { result[index] = Alphabet[(int)(value % 34)]; value /= 34; }
        }

        return new string(result);
    }
}