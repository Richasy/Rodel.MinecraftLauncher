// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Identifies the launcher build and its execution environment.</summary>
/// <param name="Name">Product identifier.</param>
/// <param name="Version">Build version.</param>
/// <param name="DynamicCodeSupported">Whether this process supports dynamic code generation.</param>
public sealed record LauncherVersion(string Name, string Version, bool DynamicCodeSupported);

/// <summary>Reports removal of an instance registration and whether owned files were requested for deletion.</summary>
/// <param name="InstanceId">Removed registration identifier.</param>
/// <param name="DeletedFiles">Whether the removal included owned files.</param>
public sealed record InstanceRemovalResult(string InstanceId, bool DeletedFiles);

/// <summary>Identifies the actual activity preventing an instance mutation without conflating a task with a running game.</summary>
/// <param name="Reason">The observed blocker: game, task, content-download, or unknown-task.</param>
/// <param name="JobId">The conflicting durable task, absent when the game itself is active.</param>
public sealed record InstanceActivityConflict(string Reason, string? JobId = null);

/// <summary>Describes a cache file without opening its contents.</summary>
/// <param name="Path">Absolute file path.</param>
/// <param name="Bytes">Observed file length.</param>
/// <param name="LastWriteUtc">Observed UTC modification timestamp.</param>
public sealed record CacheFileSummary(string Path, long Bytes, string LastWriteUtc);

/// <summary>Reports a cache entry whose metadata could not be read.</summary>
/// <param name="Path">Affected path.</param>
/// <param name="Warning">Original readable failure description.</param>
public sealed record CacheReadWarning(string Path, string Warning);

/// <summary>Reports the physical cache inventory independently of reclamation eligibility.</summary>
/// <param name="Directory">Cache root.</param>
/// <param name="Bytes">Total readable bytes.</param>
/// <param name="Files">Individually readable files.</param>
/// <param name="Warnings">Failures retained without discarding the readable inventory.</param>
public sealed record CacheInventory(string Directory, long Bytes, IReadOnlyList<CacheFileSummary> Files, IReadOnlyList<CacheReadWarning> Warnings);

/// <summary>Combines the cache inventory with a non-destructive reclamation assessment.</summary>
/// <param name="Directory">Cache root.</param>
/// <param name="Bytes">Total readable cache bytes.</param>
/// <param name="Files">Readable cache files.</param>
/// <param name="Warnings">Inventory read failures.</param>
/// <param name="ReclaimableBytes">Bytes eligible under the reclamation plan.</param>
/// <param name="Reclamation">Non-destructive reclamation report.</param>
/// <param name="OwnedInstalledArtifactBytes">Bytes of owned installed artifacts.</param>
/// <param name="OwnedInstalledArtifactFiles">Number of owned installed artifacts.</param>
public sealed record CacheStatus(string Directory, long Bytes, IReadOnlyList<CacheFileSummary> Files, IReadOnlyList<CacheReadWarning> Warnings, long ReclaimableBytes, CachePruneResult Reclamation, long OwnedInstalledArtifactBytes, int OwnedInstalledArtifactFiles);

/// <summary>Source-generated serialization metadata for application read summaries.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LauncherVersion))]
[JsonSerializable(typeof(InstanceRemovalResult))]
[JsonSerializable(typeof(InstanceActivityConflict))]
[JsonSerializable(typeof(LogExportResult))]
[JsonSerializable(typeof(LauncherLogExportResult))]
[JsonSerializable(typeof(LauncherLogManifest))]
[JsonSerializable(typeof(DiagnosticBundleManifest))]
[JsonSerializable(typeof(CacheInventory))]
[JsonSerializable(typeof(CacheStatus))]
[JsonSerializable(typeof(JobStatus))]
[JsonSerializable(typeof(LauncherProgress))]
[JsonSerializable(typeof(IReadOnlyList<JobStatus>))]
[JsonSerializable(typeof(IdentityRecord))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonNode))]
[JsonSerializable(typeof(IdentitySelection))]
[JsonSerializable(typeof(ExternalAuthProvider))]
[JsonSerializable(typeof(ExternalLoginResult))]
[JsonSerializable(typeof(ExternalLoginComponent))]
[JsonSerializable(typeof(ExternalLaunchRecovery))]
[JsonSerializable(typeof(IReadOnlyList<IdentityEntry>))]
[JsonSerializable(typeof(CrashAnalysis))]
[JsonSerializable(typeof(SessionDiagnosis))]
[JsonSerializable(typeof(InstanceDiagnosis))]
[JsonSerializable(typeof(IReadOnlyList<LauncherActivityEntry>))]
[JsonSerializable(typeof(UpdateCheck))]
[JsonSerializable(typeof(CachePruneResult))]
public partial class ApplicationJsonContext : JsonSerializerContext
{
    private static readonly Lazy<JsonSerializerOptions> ProgressOptions = new(CreateProgressOptions);

    private static JsonSerializerOptions CreateProgressOptions()
    {
        var options = new JsonSerializerOptions(Default.Options)
        {
            TypeInfoResolver = Default.WithAddedModifier(ConfigureProgress)
        };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Gets task metadata retaining absent versus explicitly unknown progress totals.</summary>
    public static JsonTypeInfo<JobStatus?> Job => (JsonTypeInfo<JobStatus?>)ProgressOptions.Value.GetTypeInfo(typeof(JobStatus));
    /// <summary>Gets task-list metadata using the same progress contract as individual tasks.</summary>
    public static JsonTypeInfo<IReadOnlyList<JobStatus>> Jobs => (JsonTypeInfo<IReadOnlyList<JobStatus>>)ProgressOptions.Value.GetTypeInfo(typeof(IReadOnlyList<JobStatus>));
    /// <summary>Gets operation progress metadata without adding fields belonging to other phases.</summary>
    public static JsonTypeInfo<LauncherProgress?> Progress => (JsonTypeInfo<LauncherProgress?>)ProgressOptions.Value.GetTypeInfo(typeof(LauncherProgress));

    private static void ConfigureProgress(JsonTypeInfo info)
    {
        if (info.Type != typeof(LauncherProgress)) return;
        foreach (var property in info.Properties)
        {
            property.ShouldSerialize = property.Name switch
            {
                "totalBytes" => (value, field) => field is not null || ((LauncherProgress)value).IncludesByteTotal,
                "totalFiles" => (value, field) => field is not null || ((LauncherProgress)value).IncludesFileTotal,
                "displayName" => (value, field) => field is not null || ((LauncherProgress)value).IncludesDisplayName,
                "sourceUri" => (value, field) => field is not null || ((LauncherProgress)value).IncludesSourceUri,
                "processorIndex" or "processorCount" => (value, field) => field is not null || ((LauncherProgress)value).IncludesProcessors,
                _ => property.ShouldSerialize
            };
        }
    }
}