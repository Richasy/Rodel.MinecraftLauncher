// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Worlds;

/// <summary>A closed set of world-operation outcomes, serialized without changing the established flat result shapes.</summary>
[JsonDerivedType(typeof(WorldImportResult))]
[JsonDerivedType(typeof(WorldExportResult))]
[JsonDerivedType(typeof(WorldRestoreResult))]
[JsonDerivedType(typeof(WorldArchiveRecord))]
public abstract record WorldOperationResult;

/// <summary>Reports an imported save without implying that its source was moved or removed.</summary>
/// <param name="Folder">Selected save folder.</param>
/// <param name="Directory">Published save directory.</param>
/// <param name="SourceRetained">Whether the original source was retained.</param>
public sealed record WorldImportResult(string Folder, string Directory, bool SourceRetained) : WorldOperationResult;

/// <summary>Reports a verified world archive published to the requested destination.</summary>
/// <param name="Archive">Published archive path.</param>
/// <param name="Files">Archived file count.</param>
/// <param name="Bytes">Uncompressed bytes copied.</param>
public sealed record WorldExportResult(string Archive, int Files, long Bytes) : WorldOperationResult;

/// <summary>Reports a restored save and the retained pre-restore recovery record.</summary>
/// <param name="Folder">Restored save folder.</param>
/// <param name="BackupId">Applied backup identifier.</param>
/// <param name="RecoveryId">Retained original save identifier.</param>
public sealed record WorldRestoreResult(string Folder, string BackupId, string RecoveryId) : WorldOperationResult;

/// <summary>Describes a discovered save and the metadata that could actually be read.</summary>
public sealed record WorldSummary
{
    /// <summary>Gets the save folder name.</summary>
    public required string Folder { get; init; }
    /// <summary>Gets the NBT display name, falling back to the folder name.</summary>
    public required string Name { get; set; }
    /// <summary>Gets the absolute save directory.</summary>
    public required string Directory { get; init; }
    /// <summary>Gets the stored game mode, when present.</summary>
    public int? GameType { get; set; }
    /// <summary>Gets the stored data version, when present.</summary>
    public int? DataVersion { get; set; }
    /// <summary>Gets whether hardcore was enabled, when present.</summary>
    public bool? Hardcore { get; set; }
    /// <summary>Gets the last-played Unix timestamp in milliseconds, when present.</summary>
    public long? LastPlayed { get; set; }
    /// <summary>Gets the recorded Minecraft version name, when present.</summary>
    public string? Version { get; set; }
    /// <summary>Gets the existing save icon path, when present.</summary>
    public string? Icon { get; set; }
    /// <summary>Gets the metadata read failure without hiding the usable save folder.</summary>
    public string? Warning { get; set; }
}

/// <summary>A persisted backup or recovery record, retaining unknown and noncanonical stored values.</summary>
public sealed record WorldArchiveRecord : WorldOperationResult
{
    /// <summary>Gets the archive or recovery identifier, when represented as a string.</summary>
    public string? Id { get; init; }
    /// <summary>Gets the owning instance identifier.</summary>
    public string? InstanceId { get; init; }
    /// <summary>Gets the original save folder.</summary>
    public string? World { get; init; }
    /// <summary>Gets the recorded display name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets the originating world operation.</summary>
    public string? Operation { get; init; }
    /// <summary>Gets the stored creation timestamp without normalizing its representation.</summary>
    public string? CreatedAt { get; init; }
    /// <summary>Gets the stored recovery or backup state.</summary>
    public string? State { get; init; }
    /// <summary>Gets the recorded storage format.</summary>
    public string? Storage { get; init; }
    /// <summary>Gets the original uncompressed file size.</summary>
    public long? OriginalBytes { get; init; }
    /// <summary>Gets the compressed archive size.</summary>
    public long? ArchiveBytes { get; init; }
    /// <summary>Gets the recorded archive checksum.</summary>
    public string? Sha256 { get; init; }
    /// <summary>Gets a recoverable warning, including interrupted operations.</summary>
    public string? Warning { get; init; }
    /// <summary>Gets a metadata read warning retained when the archive was created.</summary>
    public string? MetadataWarning { get; init; }
    /// <summary>Gets the selected save folder after a recorded recovery.</summary>
    public string? RestoredFolder { get; init; }
    /// <summary>Gets a record read failure; other records remain available.</summary>
    public string? Error { get; init; }
    /// <summary>Gets original fields not represented losslessly by the typed properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    /// <summary>Gets whether the record contains any non-null error, including an unfamiliar persisted error value.</summary>
    [JsonIgnore]
    public bool HasError => Error is not null || (AdditionalData.TryGetValue("error", out var error) && error.ValueKind != JsonValueKind.Null);
    /// <summary>Gets readable error text without discarding unfamiliar persisted error details.</summary>
    [JsonIgnore]
    public string? ErrorText => Error ?? (AdditionalData.TryGetValue("error", out var error) && error.ValueKind != JsonValueKind.Null ? error.ToString() : null);

    internal static WorldArchiveRecord Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        return new()
        {
            Id = StoredReportFields.Text(fields, "id"),
            InstanceId = StoredReportFields.Text(fields, "instanceId"),
            World = StoredReportFields.Text(fields, "world"),
            Name = StoredReportFields.Text(fields, "name"),
            Operation = StoredReportFields.Text(fields, "operation"),
            CreatedAt = StoredReportFields.Text(fields, "createdAt"),
            State = StoredReportFields.Text(fields, "state"),
            Storage = StoredReportFields.Text(fields, "storage"),
            OriginalBytes = StoredReportFields.Value<long>(fields, "originalBytes"),
            ArchiveBytes = StoredReportFields.Value<long>(fields, "archiveBytes"),
            Sha256 = StoredReportFields.Text(fields, "sha256"),
            Warning = StoredReportFields.Text(fields, "warning"),
            MetadataWarning = StoredReportFields.Text(fields, "metadataWarning"),
            RestoredFolder = StoredReportFields.Text(fields, "restoredFolder"),
            Error = StoredReportFields.Text(fields, "error"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}

/// <summary>Source-generated serialization for world summaries and extensible persisted records.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IReadOnlyList<WorldSummary>))]
[JsonSerializable(typeof(IReadOnlyList<WorldArchiveRecord>))]
[JsonSerializable(typeof(WorldOperationResult))]
[JsonSerializable(typeof(WorldArchiveRecord))]
public sealed partial class WorldJsonContext : JsonSerializerContext;