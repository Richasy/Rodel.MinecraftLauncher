// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal enum ContentAction
{
    Import,
    Enable,
    Disable,
    Remove,
    Install,
    BundledMods,
    RepairDependencies,
    ApplyReview,
    World,
    Policy,
    Use,
    StopUsing,
    RaisePriority,
    LowerPriority
}

internal sealed record ContentItem(
    string FileName,
    string Name,
    string Version,
    bool Enabled,
    bool MetadataWarning)
{
    internal string StoredPath { get; init; } = string.Empty;
    internal string Key { get; init; } = string.Empty;
    internal string? Sha1 { get; init; }
    internal string? IconPath { get; init; }
    internal byte[]? IconBytes { get; init; }
    internal string? IconWarning { get; init; }
    internal Func<CancellationToken, Task<(byte[]? Bytes, string? Warning)>>? ReadIcon { get; init; }
    internal string? ProjectId { get; init; }
    internal bool Pinned { get; init; }
    internal string? Channel { get; init; }
    internal bool? InUse { get; init; }
    internal int? Priority { get; init; }

    internal static ContentItem Read(ContentEntry item)
    {
        var fileName = item.FileName;
        var metadata = item.Metadata;
        var details = metadata is JsonArray array ? array.FirstOrDefault() : metadata;
        if (details is JsonObject obj && obj["quilt_loader"] is JsonObject quilt)
            details = quilt;
        var info = details as JsonObject;
        var name = Value((info?["metadata"] as JsonObject)?["name"]) ?? Value(info?["name"]) ?? Value(info?["displayName"]);
        var icon = (info?["metadata"] as JsonObject)?["icon"] ?? info?["icon"] ?? info?["logoFile"];
        var tracking = item.Tracking;
        return new(
            fileName,
            string.IsNullOrWhiteSpace(name) ? fileName : name,
            Value(info?["version"]) ?? Value((tracking?["upstream"] as JsonObject)?["version_number"]) ?? string.Empty,
            item.Enabled,
            item.MetadataWarning is not null)
        {
            StoredPath = item.Path,
            Sha1 = item.Sha1,
            IconPath = Value(icon) ?? (icon as JsonObject)?.OrderBy(pair => int.TryParse(pair.Key, out var size) ? Math.Abs(size - 128L) : long.MaxValue).Select(pair => Value(pair.Value)).FirstOrDefault(value => value is not null),
            ProjectId = Value(tracking?["platform"]) == "modrinth" ? Value(tracking?["projectId"]) : null,
            Pinned = tracking?["pinned"]?.GetValue<bool>() == true,
            Channel = Value(tracking?["channel"]),
        };
    }

    private static string? Value(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
}

internal sealed record ContentState
{
    internal string InstanceId { get; init; } = string.Empty;
    internal ContentKind Kind { get; init; }
    internal IReadOnlyList<ContentItem> Items { get; init; } = [];
    internal bool Loading { get; init; }
    internal bool Active { get; init; }
    internal bool InlineChange { get; init; }
    internal BundledModCatalog? BundledMods { get; init; }
    internal string BundledModsError { get; init; } = string.Empty;
    internal bool CancelRequested { get; init; }
    internal string ReadError { get; init; } = string.Empty;
    internal string SelectionError { get; init; } = string.Empty;
    internal ResourcePackSelectionSummary? ResourceSelection { get; init; }
    internal ShaderPackSelectionSummary? ShaderSelection { get; init; }
    internal string? ShaderEngine { get; init; }
    internal string Outcome { get; init; } = string.Empty;
    internal string OperationInstance { get; init; } = string.Empty;
    internal ContentKind OperationKind { get; init; }
    internal bool WorldOperation { get; init; }
    internal ContentResult? Result { get; init; }
    internal JsonObject? Failure { get; init; }
    internal JobStatus? Job { get; init; }
    internal string? JobId => Job?.Id;
    internal string Progress => Job?.Progress is { } progress && progress.TotalBytes is > 0 and var total ? string.Format(Resources.ContentDownloadProgress, AppImageCache.FormatSize(progress.Bytes ?? 0), AppImageCache.FormatSize(total)) : string.Empty;
    internal double? Percent => Job?.Progress is { } progress && progress.TotalBytes is > 0 and var total ? Math.Clamp((progress.Bytes ?? 0) * 100d / total, 0, 100) : null;
    internal string ErrorCode => Failure?["error"]?["code"]?.ToString() ?? string.Empty;
    internal bool HasReport => Result is not null || Failure is not null || Job is not null;
    internal bool HasActionDetails => HasReport && Outcome != "succeeded";
    internal string ReadableReport => string.Format(Resources.ContentReport, Status, Failure?["error"]?["message"]?.ToString() ?? Resources.ValueNotAvailable, (Result as ContentRemovalResult)?.RecoveryPath ?? Job?.Result?["recoveryPath"]?.ToString() ?? Resources.ValueNotAvailable);
    internal string RawReport => new JsonObject
    {
        ["result"] = Result is null ? Job?.Result?.DeepClone() : JsonSerializer.SerializeToNode(Result, ContentJsonContext.Result),
        ["failure"] = Failure?.DeepClone(),
        ["job"] = Job is null ? null : JsonSerializer.SerializeToNode(Job, ApplicationJsonContext.Job),
    }.ToJsonString(new() { WriteIndented = true });
    private string OperationCaption => string.Format(Resources.ContentTargetContext, OperationInstance, WorldOperation ? Resources.ContentWorlds : OperationKind.Title());
    internal string Status => Active
        ? CancelRequested
        ? Resources.LaunchCanceling
        : Failure is not null ? string.Format(Resources.ContentTaskReadFailed, Failure["error"]?["message"]?.ToString()) : Resources.ContentWorking
        : Outcome == "succeeded"
        ? string.Format(Resources.ContentSucceeded, OperationCaption)
        : Outcome == "canceled"
        ? string.Format(Resources.ContentCanceled, OperationCaption)
        : Failure is not null ? string.Format(Resources.ContentFailed, OperationCaption, ErrorCode switch
        {
            "INSTANCE_BUSY" or "CONTENT_INSTANCE_BUSY" => Localization.LauncherErrorText.Describe(Failure, Resources.InstanceBusyConflict),
            "IO_ERROR" => Resources.ContentFileAccessIssue,
            "FILE_NOT_FOUND" => Resources.ContentFileMissing,
            "CONTENT_BATCH_PARTIAL" => Resources.ContentPartialFailure,
            "CONTENT_NOT_MOD" => Resources.ContentModFileRequired,
            "CONTENT_NOT_CONTENT" or "CONTENT_IMPORT_UNRECOGNIZED" => Resources.ContentPackageRequired,
            "SHADER_CONFIG_CHANGED" or "GAME_SETTINGS_CHANGED" => Resources.ContentSelectionChanged,
            "SHADER_ENGINE_REQUIRED" => Resources.ContentShaderEngineMissing,
            "CONTENT_IMPORT_CONFLICT" => Resources.ContentImportNameConflict,
            "CONTENT_MOD_ID_CONFLICT" => Resources.ContentFilesDuplicateMod,
            _ => Resources.InstanceOperationFailed,
        }) : string.Empty;
}