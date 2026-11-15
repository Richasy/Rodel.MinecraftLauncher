// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Runtime;
using Rodel.MinecraftLauncher.Core.Worlds;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Durable, typed requests for long-running launcher workflows. They contain no command tokens.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LaunchTask), "launch")]
[JsonDerivedType(typeof(MultiplayerTask), "multiplayer")]
[JsonDerivedType(typeof(InstallInstanceTask), "install-instance")]
[JsonDerivedType(typeof(InstallLoaderTask), "install-loader")]
[JsonDerivedType(typeof(ChangeRuntimeTask), "change-runtime")]
[JsonDerivedType(typeof(PreparePackWorkflowTask), "prepare-pack-workflow")]
[JsonDerivedType(typeof(PrepareMigrationTask), "prepare-migration")]
[JsonDerivedType(typeof(RecoverContentTask), "recover-content")]
[JsonDerivedType(typeof(InstallContentTask), "install-content")]
[JsonDerivedType(typeof(RepairDependenciesTask), "repair-dependencies")]
[JsonDerivedType(typeof(ApplyContentReviewTask), "apply-content-review")]
[JsonDerivedType(typeof(UpdateContentTask), "update-content")]
[JsonDerivedType(typeof(ImportPackTask), "import-pack")]
[JsonDerivedType(typeof(CompletePackTask), "complete-pack")]
[JsonDerivedType(typeof(UpdatePackTask), "update-pack")]
[JsonDerivedType(typeof(ApplyPackUpdateTask), "apply-pack-update")]
[JsonDerivedType(typeof(ReviewPackDistributionTask), "review-pack-distribution")]
[JsonDerivedType(typeof(ReviewPackUpdateTask), "review-pack-update")]
[JsonDerivedType(typeof(RollbackPackUpdateTask), "rollback-pack-update")]
[JsonDerivedType(typeof(InstallOnlinePackTask), "install-online-pack")]
[JsonDerivedType(typeof(UpdateOnlinePackTask), "update-online-pack")]
[JsonDerivedType(typeof(ReviewOnlinePackTask), "review-online-pack")]
[JsonDerivedType(typeof(ImportInstanceTask), "import-instance")]
[JsonDerivedType(typeof(InstallJavaTask), "install-java")]
[JsonDerivedType(typeof(ImportJavaTask), "import-java")]
[JsonDerivedType(typeof(RemoveJavaTask), "remove-java")]
[JsonDerivedType(typeof(CopyInstanceTask), "copy-instance")]
[JsonDerivedType(typeof(CreateBackupTask), "create-backup")]
[JsonDerivedType(typeof(RestoreBackupTask), "restore-backup")]
[JsonDerivedType(typeof(ExportInstanceTask), "export-instance")]
[JsonDerivedType(typeof(RemoveInstanceTask), "remove-instance")]
[JsonDerivedType(typeof(ManageWorldTask), "manage-world")]
[JsonDerivedType(typeof(MapSeedTask), "map-seed")]
[JsonDerivedType(typeof(SearchSeedsTask), "search-seeds")]
[JsonDerivedType(typeof(PrepareSeedRuntimeTask), "prepare-seed-runtime")]
public abstract record LauncherTaskRequest;

/// <summary>Owns one room in the task host until explicitly stopped or canceled; it never resumes by relaunching a game.</summary>
/// <param name="Request">The room identity, instance and game policy; invitations are protected before task persistence.</param>
public sealed record MultiplayerTask(Multiplayer.MultiplayerRequest Request) : LauncherTaskRequest;

public sealed record LaunchTask(string InstanceId, LaunchOptions Options) : LauncherTaskRequest;
public sealed record InstallInstanceTask(string InstanceId, InstanceInstallationOptions Options) : LauncherTaskRequest;
public sealed record InstallLoaderTask(string InstanceId, string Loader, LoaderInstallationOptions Options) : LauncherTaskRequest;
public sealed record ChangeRuntimeTask(string InstanceId, RuntimeChangeOptions Options) : LauncherTaskRequest;
/// <summary>Runs retained pack completion, runtime and Java preparation as one observable, cancelable attempt.</summary>
/// <param name="InstanceId">The retained pack instance.</param>
/// <param name="Options">The selected local sources, network permission and preparation scope.</param>
/// <param name="Archive">An optional location of the original archive for missing-file completion.</param>
public sealed record PreparePackWorkflowTask(string InstanceId, PackWorkflowOptions Options, string? Archive = null) : LauncherTaskRequest;
public sealed record PrepareMigrationTask(string InstanceId) : LauncherTaskRequest;
public sealed record RecoverContentTask(string InstanceId) : LauncherTaskRequest;
public sealed record InstallContentTask(string InstanceId, string ProjectId, ContentWorkflowOptions Options) : LauncherTaskRequest;
public sealed record RepairDependenciesTask(string InstanceId, string ReviewId) : LauncherTaskRequest;
public sealed record ApplyContentReviewTask(string InstanceId, string ReviewId) : LauncherTaskRequest;
public sealed record UpdateContentTask(string InstanceId, string[] Files, ContentWorkflowOptions Options) : LauncherTaskRequest;
public sealed record ImportPackTask(string Archive, PackWorkflowOptions Options) : LauncherTaskRequest;
/// <summary>Completes a retained pack's missing files without importing another instance or preparing its runtime.</summary>
/// <param name="InstanceId">The existing pack instance to repair.</param>
/// <param name="Archive">The original archive, or null to reuse the persisted source path.</param>
/// <param name="Download">Whether declared downloads may be attempted; local files are tried first.</param>
/// <param name="ManualDirectory">The optional local files directory with declared paths or explicit CurseForge mappings.</param>
public sealed record CompletePackTask(string InstanceId, string? Archive = null, bool Download = true, string? ManualDirectory = null) : LauncherTaskRequest;
public sealed record UpdatePackTask(string InstanceId, string Archive, PackWorkflowOptions Options) : LauncherTaskRequest;
public sealed record ApplyPackUpdateTask(string OperationId, PackWorkflowOptions Options) : LauncherTaskRequest;
/// <summary>Stages a frozen HMCL distribution review without applying it or launching the client.</summary>
/// <param name="InstanceId">The retained HMCL client whose stored source is checked.</param>
public sealed record ReviewPackDistributionTask(string InstanceId) : LauncherTaskRequest;
/// <summary>Stages an archive update and its exact files for later confirmation without applying it.</summary>
/// <param name="InstanceId">The retained pack client.</param>
/// <param name="Archive">The player-selected new release archive.</param>
/// <param name="Options">File acquisition and optional-content selections.</param>
public sealed record ReviewPackUpdateTask(string InstanceId, string Archive, PackWorkflowOptions Options) : LauncherTaskRequest;
/// <summary>Restores the current applied pack operation while preserving later player edits.</summary>
/// <param name="OperationId">The current applied transaction to roll back.</param>
public sealed record RollbackPackUpdateTask(string OperationId) : LauncherTaskRequest;
public sealed record InstallOnlinePackTask(string ProjectId, PackWorkflowOptions Options) : LauncherTaskRequest;
public sealed record UpdateOnlinePackTask(string InstanceId, PackWorkflowOptions Options) : LauncherTaskRequest;
/// <summary>Resolves an online release and stages its exact files for later application without reselecting latest.</summary>
/// <param name="InstanceId">The retained online pack client.</param>
/// <param name="Options">The project/version and acquisition choices for this review.</param>
public sealed record ReviewOnlinePackTask(string InstanceId, PackWorkflowOptions Options) : LauncherTaskRequest;
public sealed record ImportInstanceTask(string Name, string Directory, InstanceImportOptions Options) : LauncherTaskRequest;
public sealed record InstallJavaTask(string Component, string? Architecture = null, string? ExpectedVersion = null) : LauncherTaskRequest;
public sealed record ImportJavaTask(string Source, string? Id = null) : LauncherTaskRequest;
public sealed record RemoveJavaTask(string Id) : LauncherTaskRequest;

public sealed record CopyInstanceTask(string InstanceId, string Name) : LauncherTaskRequest;
public sealed record CreateBackupTask(string InstanceId) : LauncherTaskRequest;
public sealed record RestoreBackupTask(string InstanceId, string BackupId) : LauncherTaskRequest;
public sealed record ExportInstanceTask(string InstanceId, string Destination) : LauncherTaskRequest;
/// <summary>Removes an instance registration and its backups, optionally also deleting its owned game files.</summary>
public sealed record RemoveInstanceTask(string InstanceId, bool DeleteFiles) : LauncherTaskRequest;
public sealed record ManageWorldTask(string InstanceId, WorldAction Action, string? World = null, string? Source = null, string? Destination = null) : LauncherTaskRequest;

/// <summary>Generates and atomically caches one original-version map tile.</summary>
/// <param name="Query">Complete original runtime, seed, generation settings and sampling bounds.</param>
public sealed record MapSeedTask(Rodel.Minecraft.Seeds.SeedMapQuery Query) : LauncherTaskRequest;
/// <summary>Executes a retained seed search using its durable committed cursor.</summary>
/// <param name="SearchId">Existing search identity created before dispatch.</param>
public sealed record SearchSeedsTask(string SearchId) : LauncherTaskRequest;
/// <summary>Prepares original game dependencies and a compatible managed Java runtime without creating an instance.</summary>
/// <param name="Version">Exact version with a verified seed adapter.</param>
public sealed record PrepareSeedRuntimeTask(string Version = "26.3") : LauncherTaskRequest;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LauncherTaskRequest))]
internal sealed partial class LauncherTaskJsonContext : JsonSerializerContext;