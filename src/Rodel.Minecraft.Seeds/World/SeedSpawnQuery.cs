// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Seeds;

/// <summary>Requests the new world's shared Overworld spawn through the exact original initialization path.</summary>
public sealed record SeedSpawnQuery
{
    /// <summary>Gets the installed original version and Java selection.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public SeedRuntimeProfile Profile { get; set; } = null!;
    /// <summary>Gets the signed seed or original text-seed input.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public string Seed { get; set; } = string.Empty;
    /// <summary>Gets the Overworld preset and optional fixed biome; other dimensions have no initial world spawn.</summary>
    public SeedWorldSettings World { get; set; } = new();
}