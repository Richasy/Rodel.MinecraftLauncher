// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes an absolute-atlas transfer; source and destination masks are independent of face-local selections.</summary>
public sealed record SkinAtlasTransfer
{
    /// <summary>Gets the selected source texels for Move or Cut; Paste consumes Block instead.</summary>
    public bool[]? SourceMask { get; init; }
    /// <summary>Gets frozen clipboard pixels for Paste, including the distinction between transparent pixels and holes.</summary>
    public SkinPixelBlock? Block { get; init; }
    /// <summary>Gets the absolute destination left edge of the captured bounding rectangle.</summary>
    public int X { get; init; }
    /// <summary>Gets the absolute destination top edge of the captured bounding rectangle.</summary>
    public int Y { get; init; }
    /// <summary>Gets the destination Minecraft base or outer surface.</summary>
    public SkinLayer Layer { get; init; }
    /// <summary>Gets an optional Size² mask limiting legal destinations, such as the visible body parts frozen at gesture start.</summary>
    public bool[]? DestinationMask { get; init; }

    internal SkinAtlasTransfer Freeze() => this with
    {
        SourceMask = SourceMask?.ToArray(),
        DestinationMask = DestinationMask?.ToArray(),
        Block = Block is { } block ? block with { Pixels = [.. block.Pixels], Mask = block.Mask?.ToArray() } : null,
    };
}

/// <summary>Contains an exact clipboard rectangle and its original absolute-atlas position.</summary>
/// <param name="X">The source atlas left edge.</param>
/// <param name="Y">The source atlas top edge.</param>
/// <param name="Block">The included raw layer pixels and footprint.</param>
public sealed record SkinAtlasBlock(int X, int Y, SkinPixelBlock Block);