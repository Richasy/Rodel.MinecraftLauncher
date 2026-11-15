// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A snapshot of a launcher operation's phase, measured work and optional task ownership.</summary>
public sealed record LauncherProgress
{
    /// <summary>Gets the current operation phase.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stage { get; init; }
    /// <summary>Gets the CLI operation associated with a host event.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }
    /// <summary>Gets the owning task identifier, when dispatched as a task.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JobId { get; init; }
    /// <summary>Gets the protocol event name added by the reporting host.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; init; }
    /// <summary>Gets the UTC observation time supplied by the reporting host.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Time { get; init; }
    /// <summary>Gets the current local file path.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    /// <summary>Gets the current file's optional display name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets the optional download source, without inventing one for local work.</summary>
    public string? SourceUri { get; init; }
    /// <summary>Gets the bytes actually processed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Bytes { get; init; }
    /// <summary>Gets the known byte total; null means the reported transfer has no known total.</summary>
    public long? TotalBytes { get; init; }
    /// <summary>Gets completion of the containing batch or ordered preparation phases, never the current file's byte percentage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OverallPercent { get; init; }
    /// <summary>Gets whether the current file was reused rather than downloaded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Reused { get; init; }
    /// <summary>Gets the number of files discovered for a directory copy.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Files { get; init; }
    /// <summary>Gets the completed installation or content file count.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CompletedFiles { get; init; }
    /// <summary>Gets the known installation file total, retaining an explicitly unknown total.</summary>
    public int? TotalFiles { get; init; }
    /// <summary>Gets the completed files reused from shared dependencies.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SharedReusedFiles { get; init; }
    /// <summary>Gets the loader's current progress message.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
    /// <summary>Gets the current loader processor index, if that phase has an index.</summary>
    public int? ProcessorIndex { get; init; }
    /// <summary>Gets the loader processor count, if known.</summary>
    public int? ProcessorCount { get; init; }
    /// <summary>Gets the instance undergoing pack preparation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the pack operation being prepared.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationId { get; init; }
    /// <summary>Gets the selected online pack project.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; init; }
    /// <summary>Gets the selected online pack version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionId { get; init; }
    /// <summary>Gets the pack workflow snapshot at the time this event was emitted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackPreparation? PackPreparation { get; init; }
    /// <summary>Gets an activity-log failure that did not invalidate the operation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivityLogWarning { get; init; }
    /// <summary>Gets a progress persistence or interpretation failure without claiming the operation failed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProgressWarning { get; init; }
    /// <summary>Gets unknown persisted fields, malformed known values and explicit nulls.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal bool IncludesByteTotal { get; init; }
    internal bool IncludesFileTotal { get; init; }
    internal bool IncludesDisplayName { get; init; }
    internal bool IncludesSourceUri { get; init; }
    internal bool IncludesProcessors { get; init; }

    internal static LauncherProgress Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        PackPreparation? preparation = null;
        string? preparationWarning = null;
        if (fields["packPreparation"] is JsonObject storedPreparation)
        {
            try
            {
                preparation = storedPreparation.Deserialize(PackJsonContext.Default.PackPreparation);
                fields.Remove("packPreparation");
            }
            catch (JsonException exception)
            {
                preparationWarning = exception.Message;
            }
        }

        var result = new LauncherProgress
        {
            Stage = StoredReportFields.Text(fields, "stage"),
            OverallPercent = StoredReportFields.Value<double>(fields, "overallPercent"),
            Operation = StoredReportFields.Text(fields, "operation"),
            JobId = StoredReportFields.Text(fields, "jobId"),
            Event = StoredReportFields.Text(fields, "event"),
            Time = StoredReportFields.Text(fields, "time"),
            Path = StoredReportFields.Text(fields, "path"),
            DisplayName = StoredReportFields.Text(fields, "displayName"),
            SourceUri = StoredReportFields.Text(fields, "sourceUri"),
            Bytes = StoredReportFields.Value<long>(fields, "bytes"),
            TotalBytes = StoredReportFields.Value<long>(fields, "totalBytes"),
            Reused = StoredReportFields.Value<bool>(fields, "reused"),
            Files = StoredReportFields.Value<int>(fields, "files"),
            CompletedFiles = StoredReportFields.Value<int>(fields, "completedFiles"),
            TotalFiles = StoredReportFields.Value<int>(fields, "totalFiles"),
            SharedReusedFiles = StoredReportFields.Value<int>(fields, "sharedReusedFiles"),
            Message = StoredReportFields.Text(fields, "message"),
            ProcessorIndex = StoredReportFields.Value<int>(fields, "processorIndex"),
            ProcessorCount = StoredReportFields.Value<int>(fields, "processorCount"),
            InstanceId = StoredReportFields.Text(fields, "instanceId"),
            OperationId = StoredReportFields.Text(fields, "operationId"),
            ProjectId = StoredReportFields.Text(fields, "projectId"),
            VersionId = StoredReportFields.Text(fields, "versionId"),
            PackPreparation = preparation,
            ActivityLogWarning = StoredReportFields.Text(fields, "activityLogWarning"),
            ProgressWarning = StoredReportFields.Text(fields, "progressWarning"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
        if (preparationWarning is null) return result;
        result.AdditionalData.Remove("progressWarning");
        return result with { ProgressWarning = string.Join(Environment.NewLine, new[] { result.ProgressWarning, preparationWarning }.Where(value => value is not null)) };
    }
}