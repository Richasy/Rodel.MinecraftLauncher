// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using System.Text.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Exchanges exact selected RGBA pixels through a bounded, versioned Windows clipboard format.</summary>
internal static unsafe class SkinSelectionClipboard
{
    private static readonly uint Format = PInvoke.RegisterClipboardFormat("Rodel.Minecraft.Skin.Selection.v1");
    private const int MaximumBytes = 1024 * 1024;
    internal static bool Available => (Format != 0) && PInvoke.IsClipboardFormatAvailable(Format);

    internal static bool Write(nint owner, SkinAtlasBlock block)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(block, SkinJsonContext.Default.SkinAtlasBlock);
        if ((bytes.Length > MaximumBytes) || (Format == 0))
            return false;
        var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE | GLOBAL_ALLOC_FLAGS.GMEM_ZEROINIT, (nuint)bytes.Length);
        if (memory.Value == null)
            return false;
        try
        {
            var pointer = PInvoke.GlobalLock(memory);
            if (pointer == null)
                return false;
            try
            {
                bytes.CopyTo(new Span<byte>(pointer, bytes.Length));
            }
            finally
            {
                _ = PInvoke.GlobalUnlock(memory);
            }

            if (!PInvoke.OpenClipboard(new(owner)))
                return false;
            try
            {
                if (!PInvoke.EmptyClipboard() || PInvoke.SetClipboardData(Format, new HANDLE(memory.Value)).Value == null)
                    return false;
                // The clipboard owns a successfully transferred HGLOBAL; never wrap it in an owning SafeHandle.
                memory = default;
                return true;
            }
            finally
            {
                _ = PInvoke.CloseClipboard();
            }
        }
        finally
        {
            if (memory.Value != null)
                _ = PInvoke.GlobalFree(memory);
        }
    }

    internal static SkinAtlasBlock? Read(nint owner)
    {
        if (!Available || !PInvoke.OpenClipboard(new(owner)))
            return null;
        try
        {
            var memory = new HGLOBAL(PInvoke.GetClipboardData(Format).Value);
            if (memory.Value == null)
                return null;
            var length = PInvoke.GlobalSize(memory);
            if (length is 0 or > MaximumBytes)
                return null;
            var pointer = PInvoke.GlobalLock(memory);
            if (pointer == null)
                return null;
            try
            {
                // GlobalAlloc can round allocations upward; trailing allocation padding is not JSON data.
                var data = new ReadOnlySpan<byte>(pointer, (int)length);
                var end = data.IndexOf((byte)0);
                if (end >= 0)
                    data = data[..end];
                var result = JsonSerializer.Deserialize(data, SkinJsonContext.Default.SkinAtlasBlock);
                if (result?.Block is not { } block || block.Width is < 1 or > 128 || block.Height is < 1 or > 128 || (block.Pixels.Length != (block.Width * block.Height)) || (block.Mask is not null && (block.Mask.Length != block.Pixels.Length)))
                {
                    return null;
                }

                return result;
            }
            catch (JsonException)
            {
                return null;
            }
            finally
            {
                _ = PInvoke.GlobalUnlock(memory);
            }
        }
        finally
        {
            _ = PInvoke.CloseClipboard();
        }
    }
}