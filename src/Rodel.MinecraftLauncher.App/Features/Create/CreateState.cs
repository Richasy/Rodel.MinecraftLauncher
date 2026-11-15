// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Versions;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Installation;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Create;

internal sealed record CreateState
{
    internal string Name { get; init; } = string.Empty;
    internal string Version { get; init; } = string.Empty;
    internal string VersionText { get; init; } = string.Empty;
    internal bool VersionValidationRequested { get; init; }
    internal bool RefreshingCatalog { get; init; }
    internal bool Loading { get; init; }
    internal bool Active { get; init; }
    internal bool CancelRequested { get; init; }
    internal string? InstanceId { get; init; }
    internal VersionCatalog? Catalog { get; init; }
    internal JobStatus? Job { get; init; }
    internal IReadOnlyList<LauncherActivityEntry>? Activity { get; init; }
    internal IReadOnlyList<InstanceInstallationRecovery> Recoveries { get; init; } = [];
    internal string Error { get; init; } = string.Empty;
    internal string[] RecoveryNames => [.. Recoveries.Select(item => string.Format(Resources.CreateRecoveryItem, item.Name, item.Version))];
    internal string[] Versions => InstanceId is not null ? [Version] : [.. new GameVersionChoices(Catalog).Rows.Where(row => !row.Header).Select(row => row.Value)];
    internal bool Succeeded => Job?.Status == "succeeded";
    internal bool HasDetails => Active || Job is not null || InstanceId is not null || Error.Length > 0;
    internal bool CanConfirm => !Active && !Loading && !Succeeded && Name.Trim().Length > 0 && Version.Length > 0;
    private int? TotalFiles => Job?.Progress is { } progress ? progress.TotalFiles : Job?.Result?["totalFiles"]?.GetValue<int>();
    private int CompletedFiles => (Job?.Progress is { } progress ? progress.CompletedFiles : Job?.Result?["completedFiles"]?.GetValue<int>()) ?? 0;
    private int SharedReusedFiles => (Job?.Progress is { } progress ? progress.SharedReusedFiles : Job?.Result?["sharedReusedFiles"]?.GetValue<int>()) ?? 0;
    private long? TotalBytes => Job?.Progress is { } progress ? progress.TotalBytes : Job?.Result?["totalBytes"]?.GetValue<long>();
    private long Bytes => (Job?.Progress is { } progress ? progress.Bytes : Job?.Result?["bytes"]?.GetValue<long>()) ?? 0;
    private string? Stage => Job?.Progress is { } progress ? progress.Stage : Job?.Result?["stage"]?.ToString();
    internal double? Percent => Succeeded ? 100d : (WorkflowStage - 1 + Math.Clamp((Job?.Progress?.OverallPercent ?? 0d) / 100d, 0d, 1d)) * 100d / 3;
    internal int WorkflowStage => Succeeded ? 3 : Stage switch
    {
        "publishing" => 2,
        "java-discovery" or "java-install" or "download" => 3,
        _ => 1,
    };
    internal string Workflow => string.Format(Resources.CreateWorkflow, WorkflowStage, WorkflowStage switch
    {
        2 => Resources.CreateWorkflowPublish,
        3 => Resources.CreateWorkflowJava,
        _ => Resources.CreateWorkflowFiles
    });
    internal string Status => Active
        ? CancelRequested ? Resources.CreateCanceling : Job?.Progress?.Stage switch
        {
            "metadata" => Resources.CreateStageMetadata,
            "client" => Resources.CreateStageClient,
            "library" or "native" => Resources.CreateStageLibraries,
            "assetIndex" or "asset" or "logging" => Resources.CreateStageAssets,
            "publishing" => Resources.CreatePublishing,
            "java-discovery" => Resources.CreateJavaDiscovery,
            "java-install" or "download" => Resources.CreateJavaInstalling,
            _ => Resources.CreateInstalling,
        }
        : Job?.Status switch
        {
            "succeeded" => Resources.CreateCompleted,
            "canceled" => Resources.CreateCanceled,
            "failed" or "interrupted" => Resources.CreateFailed,
            _ => InstanceId is not null ? Resources.CreateRegistered : string.Empty,
        };
    internal string Warning
    {
        get
        {
            var messages = new List<string>();
            if (Job?.Result?["javaWarning"] is not null)
                messages.Add(Resources.CreateJavaFallback);
            var java = Job?.Result?["java"];
            if (java?["preferredMajor"]?.GetValue<int>() is { } preferred && java["selectedMajor"]?.GetValue<int>() is { } selected && preferred != selected)
                messages.Add(string.Format(Resources.CreateJavaMismatch, preferred, selected));
            else if (java?["compatibilityNote"] is not null)
                messages.Add(Resources.CreateJavaAdvisory);
            if (Job?.ActivityLogWarning is not null || Job?.Result?["activityLogWarning"] is not null)
                messages.Add(Resources.CreateLogUnavailable);
            return string.Join(Environment.NewLine, messages);
        }
    }

    internal string FileProgress => TotalFiles is > 0 and var total
        ? string.Format(
        Resources.CreateTotalFiles,
        CompletedFiles,
        total,
        Math.Clamp(CompletedFiles * 100d / total, 0d, 100d),
        SharedReusedFiles)
        : Active ? Resources.CreateCountingFiles : string.Empty;
    internal string CurrentFileProgress => Active && TotalBytes is > 0 and var total ? string.Format(WorkflowStage == 3 ? Resources.CreateJavaTransfer : Resources.CreateFileProgress, AppImageCache.FormatSize(Bytes), AppImageCache.FormatSize(total)) : string.Empty;
    internal string PreparationProgress => Active
        ? string.Join(Environment.NewLine, new[] { Status, FileProgress, CurrentFileProgress }.Where(text => text.Length > 0))
        : string.Empty;
    internal string ReadableReport => string.Format(
        Resources.CreateReadableReport,
        Name,
        Version,
        Status,
        Workflow,
        FileProgress,
        Warning.Length > 0 ? Warning : string.Empty,
        string.Join(Environment.NewLine, new[] { Error.Length > 0 ? string.Format(Resources.CreateIssueDetail, Error) : string.Empty, RecentActivity }.Where(text => text.Length > 0)));
    private string RecentActivity => Activity is { Count: > 0 } ? string.Format(Resources.CreateRecentActivity, string.Join(Environment.NewLine, Activity.TakeLast(8).Select(item => string.Format(Resources.CreateActivityLine, DateTimeOffset.TryParse(item.Time, out var time) ? time.ToLocalTime().ToString("HH:mm:ss") : string.Empty, item.Event switch
    {
        "instance.created" => Resources.CreateActivityRegistered,
        "installation.completed" => Resources.CreateFilesReady,
        "task.succeeded" => Resources.CreateTaskFinished,
        "installation.shared-reuse" => Resources.CreateActivityShared,
        "java.selected" => Resources.CreateActivityJavaSelected,
        "java.prepared" => Resources.CreateActivityJavaPrepared,
        "java.preparation-fallback" => Resources.CreateJavaFallback,
        "task.stage" => StageDescription(item.Stage),
        _ => Resources.CreateActivityOther,
    })))) : string.Empty;

    private static string StageDescription(string? stage) => stage switch
    {
        "completed" => Resources.CreateTaskFinished,
        "metadata" => Resources.CreateStageMetadata,
        "client" => Resources.CreateStageClient,
        "library" or "native" => Resources.CreateStageLibraries,
        "asset" or "assetIndex" or "logging" => Resources.CreateStageAssets,
        "publishing" => Resources.CreatePublishing,
        "java-discovery" => Resources.CreateJavaDiscovery,
        "java-install" or "download" => Resources.CreateJavaInstalling,
        _ => Resources.CreateInstalling,
    };
    internal string CatalogStatus => RefreshingCatalog || Loading
        ? Resources.CreateLoading
        : Catalog?.Versions is not { Count: > 0 }
        ? Resources.CreateCatalogUnavailable
        : Catalog.Cached ? Resources.CreateCached : Resources.CreateOnline;
    internal bool VersionInvalid => VersionValidationRequested && InstanceId is null && !Loading && Version.Length == 0;
    internal string VersionHint => VersionInvalid
        ? Versions.Any(version => version.Contains(VersionText, StringComparison.OrdinalIgnoreCase)) ? Resources.CreateChooseMatchingVersion : Resources.CreateNoMatchingVersion
        : string.Empty;
    internal string Report => new JsonObject
    {
        ["instanceId"] = InstanceId,
        ["version"] = Version,
        ["job"] = JsonSerializer.SerializeToNode(Job, ApplicationJsonContext.Job),
        ["activity"] = JsonSerializer.SerializeToNode(Activity, ApplicationJsonContext.Default.IReadOnlyListLauncherActivityEntry),
        ["error"] = Error
    }.ToJsonString(new() { WriteIndented = true });
}