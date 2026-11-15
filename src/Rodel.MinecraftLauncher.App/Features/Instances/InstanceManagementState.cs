// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Instances;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

internal enum InstanceAction
{
    Rename,
    Copy,
    Configure,
    Icon,
    Backup,
    Restore,
    Export,
    Delete,
    Content
}

internal sealed record ManagedInstance(InstanceRecord Instance, HomeInstanceDetails? Details, string? Error)
{
    internal string Id => Instance.Id;
    internal string Name => Instance.Name;
    internal string Version => Details?.Version ?? Instance.VersionId ?? Resources.VersionNotConfigured;
    internal string Runtime => string.Format(Resources.InstanceRuntimeSummary, Version, Details?.LoaderDisplay ?? Resources.ReadingConfiguration);
    internal string Configuration => Details?.Configuration ?? Resources.ReadingConfiguration;
    internal bool CanLaunch => Error is null && Details is { CanLaunch: true };
    internal string LaunchBlockReason => Details?.LaunchBlockReason ?? Resources.ReadingConfiguration;
    internal string Content => Details?.ContentIssue is not null
        ? Resources.ContentReadFailed
        : Details?.EnabledMods is { } count ? string.Format(Resources.EnabledMods, count) : Resources.ValueNotAvailable;
}

internal sealed record InstanceManagementState
{
    internal InstanceRecord? Instance { get; init; }
    internal InstanceAction Action { get; init; }
    internal string Name { get; init; } = string.Empty;
    internal bool Active { get; init; }
    internal bool CancelRequested { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string ErrorCode { get; init; } = string.Empty;
    internal bool DeleteFiles { get; init; } = true;
    internal bool Conflict { get; init; }
    internal JobStatus? Job { get; init; }
    internal JsonNode? Result { get; init; }
    internal IReadOnlyList<BackupManifest> Backups { get; init; } = [];
    internal IReadOnlyList<BackupManifest> BackupReadErrors { get; init; } = [];
    internal string BackupWarning => BackupReadErrors.Count > 0 ? string.Format(Resources.InstanceBackupReadWarning, BackupReadErrors.Count) : string.Empty;
    internal int BackupIndex { get; init; } = -1;
    internal LaunchSettingsDraft? Draft { get; init; }
    internal string StatusCode { get; init; } = string.Empty;
    internal bool Succeeded => StatusCode == "succeeded";
    internal bool HasDetails => Job is not null || (Action != InstanceAction.Content && Result is not null) || Error.Length > 0 || BackupReadErrors.Count > 0;
    internal string Title => Action switch
    {
        InstanceAction.Rename => Resources.InstanceRenameHeading,
        InstanceAction.Copy => Resources.InstanceCopy,
        InstanceAction.Configure => Resources.CurrentInstanceSettings,
        InstanceAction.Icon => Resources.InstanceIcon,
        InstanceAction.Backup => Resources.InstanceBackup,
        InstanceAction.Restore => Resources.InstanceRestore,
        InstanceAction.Export => Resources.InstanceExport,
        InstanceAction.Delete => Resources.InstanceRemoveHeading,
        _ => Resources.InstanceContentDetails,
    };
    internal string Explanation => Action switch
    {
        InstanceAction.Copy => Resources.InstanceCopyScope,
        InstanceAction.Backup => Resources.InstanceBackupScope,
        InstanceAction.Restore => Resources.InstanceRestoreScope,
        InstanceAction.Export => Resources.InstanceExportScope,
        InstanceAction.Delete => DeleteFiles ? Resources.InstanceDeleteScope : Resources.InstanceUnregisterScope,
        InstanceAction.Icon => Resources.InstanceIconHelp,
        _ => string.Empty,
    };
    internal string Status => Active
        ? CancelRequested ? Resources.LaunchCanceling : Resources.InstanceOperationBusy
        : Conflict
        ? Resources.InstanceBusyConflict
        : Error.Length > 0 ? FailureDescription : StatusCode switch
        {
            "succeeded" => Resources.InstanceOperationSucceeded,
            "canceled" => Resources.InstanceOperationCanceled,
            "failed" or "interrupted" => FailureDescription,
            _ => string.Empty,
        };
    internal string FailureCode => ErrorCode.Length > 0 ? ErrorCode : Job?.Failure?["error"]?["code"]?.ToString() ?? string.Empty;
    internal string FailureMessage => Error.Length > 0 ? Error : Job?.Failure?["error"]?["message"]?.ToString() ?? Job?.Error ?? string.Empty;
    internal string FailureReport => FailureCode.Length > 0 || FailureMessage.Length > 0
        ? string.Format(Resources.InstanceOperationFailureReport,
            FailureCode.Length > 0 ? FailureCode : Resources.ValueNotAvailable,
            FailureMessage.Length > 0 ? FailureMessage : Resources.ValueNotAvailable)
        : string.Empty;
    private string FailureDescription => FailureCode switch
    {
        "IO_ERROR" => Resources.InstanceFileAccessIssue,
        "FILE_NOT_FOUND" => Resources.InstanceFileMissingIssue,
        "INSTANCE_FILES_IN_USE" => Resources.InstanceFilesReferencedIssue,
        "INSTANCE_DELETE_SCOPE" => Resources.InstanceDeleteScopeIssue,
        "INSTANCE_REMOVAL_MISMATCH" => Resources.InstanceRemovalMismatchIssue,
        "INSTANCE_FILES_NOT_OWNED" => Resources.InstanceFilesNotOwnedIssue,
        _ => Resources.InstanceOperationFailed,
    };
    internal string Progress => Job?.Progress is { } progress && progress.TotalBytes is > 0 and var total ? string.Format(Resources.ImportCopyProgress, (progress.Bytes ?? 0) / 1048576d, total / 1048576d) : string.Empty;
    internal double? Percent => Job?.Progress is { } progress && progress.TotalBytes is > 0 and var total ? Math.Clamp((progress.Bytes ?? 0) * 100d / total, 0, 100) : null;
    internal string ReadableReport => string.Join(Environment.NewLine + Environment.NewLine,
        new[] { SummaryReport, FailureReport }.Where(value => value.Length > 0));
    private string SummaryReport => BackupReadErrors.Count > 0 ? string.Format(
        Resources.InstanceBackupReadReport,
        Instance?.Name ?? string.Empty,
        Title,
        Status,
        Job?.Id ?? Resources.ValueNotAvailable,
        BackupReadErrors.Count) : string.Format(
        Resources.InstanceOperationReport,
        Instance?.Name ?? string.Empty,
        Title,
        Status,
        Job?.Id ?? Resources.ValueNotAvailable);
    internal string RawReport => new JsonObject
    {
        ["job"] = JsonSerializer.SerializeToNode(Job, ApplicationJsonContext.Job),
        ["result"] = Result?.DeepClone(),
        ["error"] = Error,
        ["errorCode"] = FailureCode,
        ["unreadableBackups"] = JsonSerializer.SerializeToNode(BackupReadErrors, BackupJsonContext.Default.IReadOnlyListBackupManifest)
    }.ToJsonString(new() { WriteIndented = true });
    internal string[] BackupNames => [.. Backups.Select(item => string.Format(Resources.InstanceBackupItem, DateTimeOffset.TryParse(item.CreatedAt, out var date) ? date.ToLocalTime().ToString("g") : Resources.ValueNotAvailable, item.Id))];
}