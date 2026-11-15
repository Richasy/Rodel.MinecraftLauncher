// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Describes one existing content file or directory without replacing its original metadata.</summary>
public sealed class ContentEntry : ContentResult
{
    /// <summary>Gets or sets the path relative to the game directory, or the original absolute path when necessary.</summary>
    public required string Path { get; set; }
    /// <summary>Gets or sets the logical file name presented to content operations.</summary>
    public required string FileName { get; set; }
    /// <summary>Gets the content directory kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets whether this entry is currently enabled.</summary>
    [JsonInclude]
    public bool Enabled { get; internal set; }
    /// <summary>Gets whether the entry has a manageable content name or directory layout.</summary>
    public bool IsContent { get; init; }
    /// <summary>Gets whether the expected package layout was recognized.</summary>
    [JsonInclude]
    public bool FormatRecognized { get; internal set; }
    /// <summary>Gets whether the entry is a directory.</summary>
    public bool Directory { get; init; }
    /// <summary>Gets the legacy mod-specific content classification, present only for mods.</summary>
    public bool? IsMod { get; init; }
    /// <summary>Gets the SHA-1 digest when file hashing was attempted.</summary>
    [JsonInclude]
    public string? Sha1 { get; internal set; }
    /// <summary>Gets the file size when it could be read.</summary>
    [JsonInclude]
    public long? Size { get; internal set; }
    /// <summary>Gets whether an archive could be opened, when archive inspection was attempted.</summary>
    [JsonInclude]
    public bool? ArchiveReadable { get; internal set; }
    /// <summary>Gets the unmodified package metadata, which can contain any JSON value.</summary>
    [JsonInclude]
    public JsonNode? Metadata { get; internal set; }
    /// <summary>Gets the source metadata entry name, when available.</summary>
    [JsonInclude]
    public string? MetadataFormat { get; internal set; }
    /// <summary>Gets a recoverable metadata inspection warning.</summary>
    [JsonInclude]
    public string? MetadataWarning { get; internal set; }
    /// <summary>Gets a recoverable file access warning.</summary>
    [JsonInclude]
    public string? ReadWarning { get; internal set; }
    /// <summary>Gets the original extensible tracking record, when the entry is tracked.</summary>
    [JsonInclude]
    public JsonObject? Tracking { get; internal set; }
    /// <summary>Gets the active directory retained for a disabled directory package.</summary>
    [JsonInclude]
    public string? ActiveDirectory { get; internal set; }
    /// <summary>Gets the active name retained for a disabled directory package.</summary>
    [JsonInclude]
    public string? ActiveFileName { get; internal set; }
    /// <summary>Gets whether import reused already identical local content.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Reused { get; internal set; }
    /// <summary>Gets a recoverable cross-volume transfer location.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TransferRecoveryPath { get; internal set; }
    /// <summary>Gets the settings subsystem's resource selection adjustment evidence.</summary>
    [JsonInclude]
    public GameSettingsChangeResult? ResourcePackSelection
    {
        get;
        internal set { field = value; SelectionAttempted = true; }
    }
    /// <summary>Gets a recoverable post-commit warning.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; internal set; }
    /// <summary>Gets the tracked source platform on mutation receipts.</summary>
    [JsonInclude]
    public string? Platform { get; internal set; }
    /// <summary>Gets the tracked project identifier on mutation receipts.</summary>
    [JsonInclude]
    public string? ProjectId { get; internal set; }
    /// <summary>Gets the exact tracked version identifier.</summary>
    [JsonInclude]
    public string? VersionId { get; internal set; }
    /// <summary>Gets the tracked source download URL.</summary>
    [JsonInclude]
    public string? DownloadUrl { get; internal set; }
    /// <summary>Gets the recorded game compatibility filter.</summary>
    [JsonInclude]
    public string? GameVersion { get; internal set; }
    /// <summary>Gets the recorded loader compatibility filter.</summary>
    [JsonInclude]
    public string? Loader { get; internal set; }
    /// <summary>Gets the stored pin policy.</summary>
    [JsonInclude]
    public bool? Pinned { get; internal set; }
    /// <summary>Gets the stored update channel.</summary>
    [JsonInclude]
    public string? Channel { get; internal set; }
    /// <summary>Gets the original release metadata retained by platform installation.</summary>
    [JsonInclude]
    public JsonNode? Upstream { get; internal set; }
    /// <summary>Gets the original provider dependency declarations.</summary>
    [JsonInclude]
    public JsonNode? Dependencies { get; internal set; }
    /// <summary>Gets whether a tracked installation was required as a dependency.</summary>
    [JsonInclude]
    public bool? Dependency { get; internal set; }
    /// <summary>Gets the additionally verified SHA-512 digest.</summary>
    [JsonInclude]
    public string? Sha512 { get; internal set; }
    /// <summary>Gets the provider display title stored with the package.</summary>
    [JsonInclude]
    public string? Title { get; internal set; }
    /// <summary>Gets the provider icon URL stored with the package.</summary>
    [JsonInclude]
    public string? IconUrl { get; internal set; }
    /// <summary>Gets prior resource-pack selection names retained across state changes.</summary>
    [JsonInclude]
    public IReadOnlyList<string>? SelectionAliases { get; internal set; }
    /// <summary>Gets the verified mod identifiers supplied by a dependency repair.</summary>
    [JsonInclude]
    public IReadOnlyList<string>? Provides { get; internal set; }
    /// <summary>Gets the dependency consumers displayed when this package was reviewed.</summary>
    [JsonInclude]
    public IReadOnlyList<string>? RequestedBy { get; internal set; }
    /// <summary>Gets additional persisted tracking fields exposed by disabled-directory listings.</summary>
    [JsonExtensionData]
    [JsonInclude]
    public Dictionary<string, JsonElement>? AdditionalData { get; internal set; }

    [JsonIgnore]
    internal bool ReadFailed { get; set; }
    [JsonIgnore]
    internal bool DisabledDirectory { get; set; }
    [JsonIgnore]
    internal bool MetadataPresent { get; set; }
    [JsonIgnore]
    internal bool SelectionAttempted { get; set; }
    [JsonIgnore]
    internal HashSet<string> TrackingFields { get; } = [];

    internal JsonObject ToRecord() => JsonSerializer.SerializeToNode(this, ContentJsonContext.Entry)!.AsObject();
}

/// <summary>Reports the recognized package kinds for one selected source without changing it.</summary>
public sealed record ContentSourceDetection
{
    /// <summary>Gets the normalized source path.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the recognizable package interpretations in discovery order.</summary>
    public required IReadOnlyList<ContentEntry> Candidates { get; init; }
}

/// <summary>Reports advisory mod metadata and dependency findings without blocking game launch.</summary>
public sealed record ContentAnalysis
{
    /// <summary>Gets the indexed mod declarations, including unmodified third-party values.</summary>
    public required IReadOnlyList<ContentModDeclaration> Mods { get; init; }
    /// <summary>Gets advisory findings with typed classifications and original dependency predicate values.</summary>
    public required IReadOnlyList<ContentAnalysisFinding> Findings { get; init; }
    /// <summary>Gets the actual inventory used for the analysis.</summary>
    public required IReadOnlyList<ContentEntry> Files { get; init; }
    /// <summary>Gets whether findings are advisory rather than launch prerequisites.</summary>
    public bool AdvisoryOnly { get; init; } = true;
    /// <summary>Gets the limits of this metadata index.</summary>
    public string Limitations { get; init; } = "Version ranges and nested archives are not fully evaluated. Findings never block launching; loader diagnostics remain authoritative.";
}

/// <summary>Represents a mod declaration indexed from third-party metadata.</summary>
public sealed class ContentModDeclaration
{
    /// <summary>Gets the declared mod identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the original version value without coercing third-party metadata.</summary>
    public JsonNode? Version { get; init; }
    /// <summary>Gets the original display name value.</summary>
    public JsonNode? Name { get; init; }
    /// <summary>Gets the owning top-level package name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the metadata loader family.</summary>
    public required string Loader { get; init; }
    /// <summary>Gets or sets the declared dependency requirements.</summary>
    public required IReadOnlyList<ContentDependencyDeclaration> Depends { get; set; }
    /// <summary>Gets the original declared breaking relations.</summary>
    public JsonNode? Breaks { get; init; }
    /// <summary>Gets the original declared conflicting relations.</summary>
    public JsonNode? Conflicts { get; internal set; }
    /// <summary>Gets the original nested archive declarations.</summary>
    public JsonNode? NestedArchives { get; init; }
    /// <summary>Gets original loader-declared dependency aliases without treating them as separate installed mods.</summary>
    public JsonNode? Provides { get; init; }
    /// <summary>Gets the path within the owning archive when this declaration came from a nested package.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NestedPath { get; internal set; }
}

/// <summary>Represents an indexed dependency while preserving the provider's range value.</summary>
public sealed record ContentDependencyDeclaration
{
    /// <summary>Gets the declared identifier without coercing its metadata value.</summary>
    public required JsonNode Id { get; init; }
    /// <summary>Gets the original version range value.</summary>
    public JsonNode? Range { get; init; }
    /// <summary>Gets whether this dependency is required on the client.</summary>
    public bool Required { get; init; }
    /// <summary>Gets the TOML dependency relation, when that metadata format supplies one.</summary>
    public string? Type { get; init; }
    /// <summary>Gets the TOML dependency side, when that metadata format supplies one.</summary>
    public string? Side { get; init; }
    [JsonIgnore]
    internal bool Toml { get; init; }
}