// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;
namespace Rodel.Minecraft.Seeds;

/// <summary>Identifies installed version metadata and an optional explicit Java executable; no player identity or save is used.</summary>
public sealed record SeedRuntimeProfile
{
    /// <summary>Gets the directory containing the selected version metadata and shared-dependency references.</summary>
    [JsonRequired]
    public string MinecraftDirectory { get; set; } = string.Empty;
    /// <summary>Gets the installed profile whose original client version is resolved before opening the worker.</summary>
    [JsonRequired]
    public string VersionId { get; set; } = string.Empty;
    /// <summary>Gets an optional explicit Java executable or Java home; null uses existing Java discovery.</summary>
    public string? JavaPath { get; set; }
}