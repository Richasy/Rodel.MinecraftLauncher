// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Backups;

/// <summary>A manifest file entry with its original extensible metadata.</summary>
public sealed record BackupFile
{
    /// <summary>Gets the archive-relative file path.</summary>
    public string? Path { get; init; }
    /// <summary>Gets the uncompressed length in bytes.</summary>
    public long? Length { get; init; }
    /// <summary>Gets the recorded SHA-256 digest.</summary>
    public string? Sha256 { get; init; }
    /// <summary>Gets original unknown or noncanonical manifest fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static BackupFile Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        return new()
        {
            Path = StoredReportFields.Text(fields, "path"),
            Length = StoredReportFields.Value<long>(fields, "length"),
            Sha256 = StoredReportFields.Text(fields, "sha256"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}

/// <summary>Describes a linked path materialized into a backup without changing its original target.</summary>
public sealed record BackupExpandedLink
{
    /// <summary>Gets the recorded path relative to the game directory.</summary>
    public string? Path { get; init; }
    /// <summary>Gets the original resolved link target.</summary>
    public string? Target { get; init; }
    /// <summary>Gets original unknown or noncanonical link metadata.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static BackupExpandedLink Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        return new()
        {
            Path = StoredReportFields.Text(fields, "path"),
            Target = StoredReportFields.Text(fields, "target"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}

/// <summary>A stored backup manifest or a per-archive read failure; unfamiliar metadata remains intact.</summary>
public sealed record BackupManifest
{
    /// <summary>Gets the archive identifier.</summary>
    public string? Id { get; init; }
    /// <summary>Gets the stored manifest format version.</summary>
    public int? Format { get; init; }
    /// <summary>Gets the owning instance identifier.</summary>
    public string? InstanceId { get; init; }
    /// <summary>Gets the complete persisted instance snapshot, including unknown instance metadata.</summary>
    public JsonObject? Instance { get; init; }
    /// <summary>Gets the recorded backup scope.</summary>
    public string? Scope { get; init; }
    /// <summary>Gets whether redownloadable content was excluded.</summary>
    public bool? ExcludeRedownloadable { get; init; }
    /// <summary>Gets the creation timestamp without changing its stored representation.</summary>
    public string? CreatedAt { get; init; }
    /// <summary>Gets whether the snapshot was taken without a running game.</summary>
    public bool? Consistent { get; internal set; }
    /// <summary>Gets the restore behavior recorded by the backup.</summary>
    public string? RestoreMode { get; init; }
    /// <summary>Gets file records when the persisted array contains objects; unfamiliar arrays remain in additional data.</summary>
    public IReadOnlyList<BackupFile>? Files { get; init; }
    /// <summary>Gets materialized links when the saved array contains objects; unfamiliar arrays remain in additional data.</summary>
    public IReadOnlyList<BackupExpandedLink>? ExpandedLinks { get; init; }
    /// <summary>Gets an advisory warning stored in the manifest.</summary>
    public string? Warning { get; internal set; }
    /// <summary>Gets an individual archive read failure without suppressing other backups.</summary>
    public string? Error { get; init; }
    /// <summary>Gets original unknown fields and values not losslessly represented by typed properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    /// <summary>Gets whether the manifest contains any non-null error, including an unfamiliar persisted error value.</summary>
    [JsonIgnore]
    public bool HasError => Error is not null || (AdditionalData.TryGetValue("error", out var error) && error.ValueKind != JsonValueKind.Null);

    internal static BackupManifest Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        IReadOnlyList<BackupFile>? files = null;
        if (fields["files"] is JsonArray array && array.All(item => item is JsonObject))
        {
            files = [.. array.Select(item => BackupFile.Read(item!.AsObject()))];
            fields.Remove("files");
        }

        IReadOnlyList<BackupExpandedLink>? links = null;
        if (fields["expandedLinks"] is JsonArray linkArray && linkArray.All(item => item is JsonObject))
        {
            links = [.. linkArray.Select(item => BackupExpandedLink.Read(item!.AsObject()))];
            fields.Remove("expandedLinks");
        }

        JsonObject? instance = null;
        if (fields["instance"] is JsonObject instanceObject)
        {
            instance = (JsonObject)instanceObject.DeepClone();
            fields.Remove("instance");
        }

        return new()
        {
            Id = StoredReportFields.Text(fields, "id"),
            Format = StoredReportFields.Value<int>(fields, "format"),
            InstanceId = StoredReportFields.Text(fields, "instanceId"),
            Instance = instance,
            Scope = StoredReportFields.Text(fields, "scope"),
            ExcludeRedownloadable = StoredReportFields.Value<bool>(fields, "excludeRedownloadable"),
            CreatedAt = StoredReportFields.Text(fields, "createdAt"),
            Consistent = StoredReportFields.Value<bool>(fields, "consistent"),
            RestoreMode = StoredReportFields.Text(fields, "restoreMode"),
            Files = files,
            ExpandedLinks = links,
            Warning = StoredReportFields.Text(fields, "warning"),
            Error = StoredReportFields.Text(fields, "error"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}

/// <summary>Source-generated serialization for backup summaries and their retained extension data.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(IReadOnlyList<BackupManifest>))]
[JsonSerializable(typeof(BackupRestoreResult))]
[JsonSerializable(typeof(BackupRestoreState))]
public sealed partial class BackupJsonContext : JsonSerializerContext;

/// <summary>A linked source materialized during an overlay restore.</summary>
/// <param name="Source">The original linked entry.</param>
/// <param name="Target">Its resolved source target, which remains unchanged.</param>
public sealed record BackupMaterializedLink(string Source, string Target);

/// <summary>Reports the applied overlay and the retained directories needed to undo it.</summary>
/// <param name="OperationId">Restore operation identifier.</param>
/// <param name="BackupId">Applied backup identifier.</param>
/// <param name="InstanceId">Restored instance identifier.</param>
/// <param name="SafetyBackupId">Fresh backup created before replacement.</param>
/// <param name="RollbackDirectory">Retained original directory.</param>
/// <param name="RecoveryFile">Persisted restore state.</param>
/// <param name="Mode">Actual restore mode.</param>
/// <param name="RestoredFiles">Number of restored manifest files.</param>
/// <param name="MaterializedLinks">Links copied into independent files or directories.</param>
/// <param name="Warning">Optional materialization warning.</param>
public sealed record BackupRestoreResult(string OperationId, string BackupId, string InstanceId, string SafetyBackupId,
    string RollbackDirectory, string RecoveryFile, string Mode, int RestoredFiles,
    IReadOnlyList<BackupMaterializedLink> MaterializedLinks, string? Warning = null);

/// <summary>A returned restore journal with explicit fields and retained unfamiliar persisted values.</summary>
public sealed record BackupRestoreState
{
    /// <summary>Gets the restore operation identifier.</summary>
    public string? OperationId { get; init; }
    /// <summary>Gets the applied archive identifier.</summary>
    public string? BackupId { get; init; }
    /// <summary>Gets the pre-restore safety archive.</summary>
    public string? SafetyBackupId { get; init; }
    /// <summary>Gets the restored game directory.</summary>
    public string? GameDirectory { get; init; }
    /// <summary>Gets the staging location recorded by the operation.</summary>
    public string? StagingDirectory { get; init; }
    /// <summary>Gets the original retained directory.</summary>
    public string? RollbackDirectory { get; init; }
    /// <summary>Gets the actual journal phase.</summary>
    public string? Phase { get; init; }
    /// <summary>Gets the displaced post-restore directory after rollback.</summary>
    public string? ReplacedDirectory { get; init; }
    /// <summary>Gets unknown or noncanonical journal values without discarding them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static BackupRestoreState Read(JsonObject source)
    {
        var fields = source.DeepClone().AsObject();
        return new()
        {
            OperationId = StoredReportFields.Text(fields, "operationId"),
            BackupId = StoredReportFields.Text(fields, "backupId"),
            SafetyBackupId = StoredReportFields.Text(fields, "safetyBackupId"),
            GameDirectory = StoredReportFields.Text(fields, "gameDirectory"),
            StagingDirectory = StoredReportFields.Text(fields, "stagingDirectory"),
            RollbackDirectory = StoredReportFields.Text(fields, "rollbackDirectory"),
            Phase = StoredReportFields.Text(fields, "phase"),
            ReplacedDirectory = StoredReportFields.Text(fields, "replacedDirectory"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}