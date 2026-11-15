// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Stores one visibility bit per atlas texel, independently of the paint layers.</summary>
internal static class SkinTransparency
{
    internal static int Length(int pixels) => (pixels + 7) / 8;

    internal static byte[]? Copy(byte[]? mask, int pixels, bool allocate = false) =>
        mask is not null ? (byte[])mask.Clone() : allocate ? new byte[Length(pixels)] : null;

    internal static bool Contains(byte[]? mask, int index) =>
        mask is not null && (mask[index / 8] & (1 << (index % 8))) != 0;

    internal static void Set(byte[] mask, int index, bool hidden)
    {
        var bit = 1 << (index % 8);
        var offset = index / 8;
        mask[offset] = (byte)(hidden ? mask[offset] | bit : mask[offset] & ~bit);
    }

    internal static byte[]? NonEmpty(byte[]? mask) => mask?.Any(value => value != 0) == true ? mask : null;
}