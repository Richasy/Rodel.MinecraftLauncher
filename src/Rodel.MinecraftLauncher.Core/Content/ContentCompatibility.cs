// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Separates the selected launch profile from the resolved content compatibility filters.</summary>
public sealed class ContentCompatibility
{
    /// <summary>Gets the selected launch profile identifier.</summary>
    public string? LaunchVersion { get; init; }
    /// <summary>Gets the explicit, inherited, or fallback Minecraft compatibility version.</summary>
    [JsonInclude]
    public string? MinecraftVersion { get; internal set; }
    /// <summary>Gets the explicit, configured, or metadata-detected loader family.</summary>
    [JsonInclude]
    public string? Loader { get; internal set; }
    /// <summary>Gets or sets the evidence source for the Minecraft version.</summary>
    public required string Source { get; set; }
    /// <summary>Gets recoverable resolution warnings without making metadata a launch prerequisite.</summary>
    public List<string> Warnings { get; init; } = [];
    /// <summary>Gets the evidence source when the loader was inferred from version metadata.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public string? LoaderSource { get; internal set; }
}