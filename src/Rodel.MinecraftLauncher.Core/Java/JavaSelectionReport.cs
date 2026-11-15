// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Explains a Java selection using typed recommendations, observed candidates and non-blocking diagnostics.</summary>
public sealed record JavaSelectionReport
{
    /// <summary>Gets the recommended major version, or null when no preference is known.</summary>
    [JsonPropertyName("preferredMajor")]
    public int? PreferredMajor { get; init; }
    /// <summary>Gets the declared or inferred Mojang runtime component, when available.</summary>
    [JsonPropertyName("runtimeComponent")]
    public string? RuntimeComponent { get; init; }
    /// <summary>Gets the basis of this selection or its automatic preparation.</summary>
    [JsonPropertyName("source")]
    public JavaSelectionSource Source { get; init; } = JavaSelectionSource.AvailableFallback;
    /// <summary>Gets the selected executable path, or null when none was selected.</summary>
    [JsonPropertyName("selectedPath")]
    public string? SelectedPath { get; init; }
    /// <summary>Gets the selected major version, or null when unknown or unselected.</summary>
    [JsonPropertyName("selectedMajor")]
    public int? SelectedMajor { get; init; }
    /// <summary>Gets the selected complete version, or null when unknown or unselected.</summary>
    [JsonPropertyName("selectedVersion")]
    public string? SelectedVersion { get; init; }
    /// <summary>Gets whether compatibility recommendations remain advisory rather than launch admission rules.</summary>
    [JsonPropertyName("advisory")]
    public bool Advisory { get; } = true;
    /// <summary>Gets a known compatibility recommendation without rejecting the selected executable.</summary>
    [JsonPropertyName("compatibilityNote")]
    public string? CompatibilityNote { get; init; }
    /// <summary>Gets the deduplicated probe and metadata warnings captured for this selection.</summary>
    [JsonPropertyName("warnings")]
    public string[] Warnings { get; init; } = [];
    /// <summary>Gets the ordered candidate snapshot used by launch configuration and reports.</summary>
    [JsonPropertyName("candidates")]
    public JavaSelectionCandidate[] Candidates { get; init; } = [];
}