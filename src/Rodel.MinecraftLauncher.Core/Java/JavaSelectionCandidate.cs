// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Contains the candidate facts used by a selection report, excluding raw probe output and unrelated registration data.</summary>
public sealed record JavaSelectionCandidate
{
    /// <summary>Gets the executable path identifying this candidate.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }
    /// <summary>Gets the complete reported version, or null when unavailable.</summary>
    [JsonPropertyName("version")]
    public string? FullVersion { get; init; }
    /// <summary>Gets the reported Java major version, or null when unknown.</summary>
    [JsonPropertyName("major")]
    public int? MajorVersion { get; init; }
    /// <summary>Gets whether the executable passed its basic process probe, not whether every game is compatible.</summary>
    [JsonPropertyName("usable")]
    public bool IsUsable { get; init; }
    /// <summary>Gets the normalized CPU architecture, when reported.</summary>
    [JsonPropertyName("architecture")]
    public string? Architecture { get; init; }
    /// <summary>Gets the JVM bitness used by memory recommendations.</summary>
    [JsonPropertyName("bitness")]
    public int? Bitness { get; init; }
    /// <summary>Gets the observed default file encoding used by launch configuration.</summary>
    [JsonPropertyName("fileEncoding")]
    public string? FileEncoding { get; init; }
    /// <summary>Gets the observed native encoding used for Java's COMPAT file-encoding mode.</summary>
    [JsonPropertyName("nativeEncoding")]
    public string? NativeEncoding { get; init; }
}