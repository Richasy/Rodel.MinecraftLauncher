// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Import;

internal sealed record ImportProfile(string Id, string Name, string Version);

internal sealed record ImportState
{
    internal string DestinationDirectory { get; init; } = string.Empty;
    internal string Directory { get; init; } = string.Empty;
    internal string SharedInput { get; init; } = string.Empty;
    internal string GameInput { get; init; } = string.Empty;
    internal string LauncherConfig { get; init; } = string.Empty;
    internal bool PreviewDirty { get; init; }
    internal bool NeedsGameDirectory => Preview?.NeedsGameDirectory == true;
    internal IReadOnlyList<string> GameDirectoryCandidates => Preview?.GameDirectoryCandidates ?? [];
    internal string Name { get; init; } = string.Empty;
    internal string Version { get; init; } = string.Empty;
    internal InstanceImportPreview? Preview { get; init; }
    internal JobStatus? Job { get; init; }
    internal bool HasDetails => Active || Job is not null || Error.Length > 0;
    internal string Error { get; init; } = string.Empty;
    internal string ErrorCode { get; init; } = string.Empty;
    internal string ErrorSummary => Error.Length == 0 || Job?.Status == "canceled" ? string.Empty : ErrorCode switch
    {
        "FILE_NOT_FOUND" => Resources.ImportDirectoryMissing,
        "INVALID_DATA" => Resources.ImportInvalidData,
        "INVALID_ARGUMENT" => Resources.ImportInvalidSelection,
        "OBSERVATION" => Resources.ImportObservationPending,
        _ => Resources.ImportOperationError,
    };
    internal bool Previewing { get; init; }
    internal bool Active { get; init; }
    internal bool CancelRequested { get; init; }
    internal string? RegisteredId { get; init; }
    internal string Profile => Preview?.ProfileId ?? string.Empty;
    internal string GameDirectory => Preview?.GameDirectory ?? string.Empty;
    internal string MinecraftDirectory => Preview?.MinecraftDirectory ?? string.Empty;
    internal bool SharedDirectory => string.Equals(Path.TrimEndingDirectorySeparator(GameDirectory), Path.TrimEndingDirectorySeparator(MinecraftDirectory), StringComparison.OrdinalIgnoreCase);
    internal string SourceName => Preview?.Format switch
    {
        "minecraft-profiles" => Resources.ImportSourceProfiles,
        "prism" => Resources.ImportSourcePrism,
        "pcl" => Resources.ImportSourcePcl,
        "hmcl" => Resources.ImportSourceHmcl,
        _ => Resources.ImportSourceDirectory,
    };
    internal string JavaPath => Preview?.Settings["javaPath"]?.ToString() ?? string.Empty;
    internal string MemoryDescription => Preview?.Settings["maxMemoryMb"]?.ToString() is { } memory ? string.Format(Resources.ImportMemory, memory) : Resources.ImportMemoryDefault;
    internal IReadOnlyList<ImportProfile> Profiles => (Preview?.SourceSettings["profiles"] as JsonArray)?.Select(p => new ImportProfile(p!["id"]!.ToString(), p["name"]?.ToString() ?? p["id"]!.ToString(), p["versionId"]?.ToString() ?? string.Empty)).ToArray() ?? [];
    internal IReadOnlyList<string> Versions => Preview?.LocalVersions ?? [];
    internal bool NeedsProfile => Preview?.Format == "minecraft-profiles" && Profiles.Count > 1 && Profile.Length == 0;
    internal bool NeedsVersion => Versions.Count > 1 && string.IsNullOrWhiteSpace(Version);
    internal bool CanConfirm => Preview is not null && Error.Length == 0 && !Previewing && !Active && Job is null && !PreviewDirty && !NeedsGameDirectory && !NeedsProfile && !NeedsVersion && !string.IsNullOrWhiteSpace(Name);
    internal double? CopyPercent => Job?.Progress?.TotalBytes is > 0 and var total ? Math.Clamp((Job.Progress.Bytes ?? 0L) * 100d / total, 0d, 100d) : null;
    internal string CopyStatus => Job?.Progress?.Stage switch
    {
        "import-copy" => string.Format(Resources.ImportCopyProgress, (Job.Progress?.Bytes ?? 0L) / 1048576d, (Job.Progress?.TotalBytes ?? 0L) / 1048576d),
        "import-register" => Resources.ImportRegistering,
        _ => Resources.ImportScanning,
    };
    internal string Status => Active ? (CancelRequested ? Resources.ImportCanceling : CopyStatus) : Job?.Status switch
    {
        "succeeded" => Resources.ImportSucceeded,
        "canceled" => RegisteredId is null ? Resources.ImportCanceled : Resources.ImportCanceledRegistered,
        "failed" or "interrupted" => RegisteredId is null ? Resources.ImportFailed : Resources.ImportFailedRegistered,
        _ => Previewing ? Resources.ImportPreviewing : string.Empty,
    };
    internal string Report => string.Format(
        Resources.ImportReport,
        Directory,
        Name,
        Version.Length > 0 ? Version : Resources.ValueNotAvailable,
        Preview is null ? string.Empty : JsonSerializer.SerializeToNode(Preview, InstanceJsonContext.Default.InstanceImportPreview)!.ToJsonString(new() { WriteIndented = true }),
        Job is null ? string.Empty : JsonSerializer.SerializeToNode(Job, ApplicationJsonContext.Job)!.ToJsonString(new() { WriteIndented = true }),
        Error);
}