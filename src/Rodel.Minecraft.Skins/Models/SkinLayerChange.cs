// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins;

/// <summary>Describes one paint-layer mutation, which shares history with ordinary drawing.</summary>
public sealed record SkinLayerChange
{
    /// <summary>Gets the layer operation.</summary>
    public required SkinLayerOperation Operation { get; init; }
    /// <summary>Gets the target ID, or the new unique ID for Add/Duplicate.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the source layer ID for duplication.</summary>
    public string? SourceId { get; init; }
    /// <summary>Gets a new layer label; null preserves the existing label.</summary>
    public string? Name { get; init; }
    /// <summary>Gets an optional visibility change.</summary>
    public bool? Visible { get; init; }
    /// <summary>Gets an optional layer opacity change.</summary>
    public double? Opacity { get; init; }
    /// <summary>Gets an optional paint-lock change.</summary>
    public bool? Locked { get; init; }
    /// <summary>Gets the final bottom-to-top index for reordering.</summary>
    public int? Index { get; init; }
}