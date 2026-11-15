// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Identifies one exact original block state, including every original state property.</summary>
public sealed record SeedBlockState
{
    /// <summary>Gets the namespaced original block ID.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets original state properties, including implicit defaults made explicit by the native runtime.</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
    /// <summary>Gets whether a compact saved codec omitted native default properties; rendering must resolve them from that original registry rather than guess.</summary>
    public bool DefaultsOmitted { get; set; }
}