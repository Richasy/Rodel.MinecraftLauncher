// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.App.Features.Updates;

internal sealed record ReleaseManifest
{
    /// <summary>Gets the supported manifest contract revision.</summary>
    public int SchemaVersion { get; init; }
    /// <summary>Gets the canonical four-part application version.</summary>
    public string Version { get; init; } = string.Empty;
    /// <summary>Gets the source commit used to build every installer.</summary>
    public string SourceCommit { get; init; } = string.Empty;
    /// <summary>Gets localized plain-text release notes keyed by launcher language.</summary>
    public Dictionary<string, string> Notes { get; init; } = [];
    /// <summary>Gets exact installer identities keyed by native Windows architecture.</summary>
    public Dictionary<string, ReleaseInstaller> Installers { get; init; } = [];
}

internal sealed record ReleaseInstaller
{
    /// <summary>Gets the exact GitHub asset filename.</summary>
    public string FileName { get; init; } = string.Empty;
    /// <summary>Gets the lowercase SHA256 digest of the complete installer.</summary>
    public string Sha256 { get; init; } = string.Empty;
    /// <summary>Gets the expected complete download size in bytes.</summary>
    public long Size { get; init; }
}

internal sealed record GitHubRelease
{
    /// <summary>Gets the published version tag.</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;
    /// <summary>Gets whether this release is unpublished.</summary>
    public bool Draft { get; init; }
    /// <summary>Gets whether this release is outside the stable channel.</summary>
    public bool Prerelease { get; init; }
    /// <summary>Gets the assets actually attached to the release.</summary>
    public GitHubAsset[] Assets { get; init; } = [];
}

internal sealed record GitHubAsset
{
    /// <summary>Gets the attached filename.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets GitHub's public download URL.</summary>
    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; init; } = string.Empty;
    /// <summary>Gets GitHub's reported asset size.</summary>
    public long Size { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class ReleaseJsonContext : JsonSerializerContext;

internal sealed record AvailableUpdate(ReleaseManifest Manifest, ReleaseInstaller Installer, Uri DownloadUri)
{
    internal string Notes(string language) => Manifest.Notes.TryGetValue(Core.Application.LauncherLanguages.Resolve(language), out var notes) && !string.IsNullOrWhiteSpace(notes)
        ? notes : Manifest.Notes.GetValueOrDefault("en") ?? string.Empty;
}
