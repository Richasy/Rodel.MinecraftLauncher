// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>A durable exact pack review and its eventual application or rollback outcome.</summary>
public sealed record PackReview : PackResult
{
    /// <summary>Gets the generated operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the owning instance.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets the reviewed game directory.</summary>
    public required string GameDirectory { get; init; }
    /// <summary>Gets or sets the durable transaction phase.</summary>
    public required string Phase { get; set; }
    /// <summary>Gets whether this review only completes original missing declarations.</summary>
    public bool MissingOnly { get; init; }
    /// <summary>Gets whether the frozen review changes files, source or optional selection.</summary>
    public bool HasChanges { get; init; }
    /// <summary>Gets the verified and staged incoming content.</summary>
    public required PackContentState Incoming { get; init; }
    /// <summary>Gets the exact original ownership document used for drift detection and rollback evidence.</summary>
    public required JsonObject PreviousLock { get; init; }
    /// <summary>Gets the frozen per-file decisions.</summary>
    public required IReadOnlyList<PackFileChange> Actions { get; init; }
    /// <summary>Gets the recorded creation timestamp.</summary>
    public required string CreatedAt { get; init; }
    /// <summary>Gets the durable report file.</summary>
    public required string ReportPath { get; init; }
    /// <summary>Gets the directory containing verified staged bytes.</summary>
    public required string IncomingDirectory { get; init; }
    /// <summary>Gets the advisory mod identity projection without replacing file decisions.</summary>
    public required PackChangeSummary ChangeSummary { get; init; }
    /// <summary>Gets or sets the safety backup made immediately before mutation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SafetyBackupId { get; set; }
    /// <summary>Gets or sets per-file write-ahead journal entries.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PackJournalEntry>? Journal { get; set; }
    /// <summary>Gets or sets paths actually changed during application.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ChangedPaths { get; set; }
    /// <summary>Gets or sets local conflicts and available staged alternatives.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PackFileChange>? Conflicts { get; set; }
    /// <summary>Gets or sets file availability after application.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Complete { get; set; }
    /// <summary>Gets or sets the current release comparison.</summary>
    public PackConsistency? Consistency { get; set; }
    /// <summary>Gets or sets the comparison invalidated by rollback.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackConsistency? AppliedConsistency { get; set; }
    /// <summary>Gets or sets edits deliberately retained during rollback.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PackRollbackConflict>? RollbackConflicts { get; set; }
    /// <summary>Gets or sets exact checked distribution evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackDistributionCheck? Distribution { get; set; }
    /// <summary>Gets or sets the selected online archive provenance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OnlinePackSelection? OnlineSource { get; set; }
    /// <summary>Gets or sets unknown durable transaction fields retained when rewriting its journal.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets or sets whether runtime preparation succeeded after applying the content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RuntimePrepared { get; set; }
    /// <summary>Gets or sets whether Java preparation succeeded after applying the content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? JavaPrepared { get; set; }
    /// <summary>Gets or sets the runtime-domain preparation result.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimePreparationResult? Runtime { get; set; }
    /// <summary>Gets or sets the actual Java selection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JavaSelectionReport? Java { get; set; }
    /// <summary>Gets or sets a recoverable Java acquisition failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaWarning { get; set; }
    /// <summary>Gets or sets the original failure that triggered compensation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackPreparationFailure? PreparationFailure { get; set; }
    /// <summary>Gets or sets the rollback failure, independently from the preparation failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LauncherOperationError? RollbackFailure { get; set; }
    /// <summary>Gets or sets the actual compensation outcome.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackReview? Rollback { get; set; }
}

/// <summary>A frozen file decision, or its conflicting application outcome.</summary>
/// <param name="Path">The managed relative path.</param>
/// <param name="Action">The intended add, replace, delete, preserve, unchanged or conflict action.</param>
/// <param name="CurrentSha1">The observed current hash.</param>
/// <param name="PreviousSha1">The previous publisher hash.</param>
/// <param name="IncomingSha1">The selected publisher hash.</param>
/// <param name="Reason">The decision explanation.</param>
/// <param name="IncomingFile">A staged alternative retained for a conflict.</param>
public sealed record PackFileChange(string Path, string Action, string? CurrentSha1, string? PreviousSha1, string? IncomingSha1, string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IncomingFile = null);

/// <summary>One write-ahead file mutation whose prepared state can be recovered after interruption.</summary>
/// <param name="Path">The relative file path.</param>
/// <param name="BeforeSha1">The hash before mutation.</param>
/// <param name="AfterSha1">The hash after mutation, or null for deletion.</param>
public sealed record PackJournalEntry(string Path, string? BeforeSha1, string? AfterSha1)
{
    /// <summary>Gets or sets prepared or committed without inferring whether an interrupted write occurred.</summary>
    public string State { get; set; } = "prepared";
}

/// <summary>A newer local edit which rollback did not overwrite.</summary>
/// <param name="Path">The relative path of the player's edit.</param>
/// <param name="Reason">Why rollback preserved it.</param>
/// <param name="PreviousFile">The retained previous file for manual recovery.</param>
public sealed record PackRollbackConflict(string Path, string Reason, string PreviousFile);

/// <summary>The exact source and manifest digest checked for an HMCL distribution.</summary>
/// <param name="CheckedSource">The actual manifest URL.</param>
/// <param name="ManifestSha256">The downloaded manifest digest.</param>
public sealed record PackDistributionCheck(string CheckedSource, string ManifestSha256);

/// <summary>Advisory mod identities grouped over exact file changes, retaining ambiguous cases as files.</summary>
/// <param name="Mods">Unambiguous recognized mod changes.</param>
/// <param name="Files">Changes without a reliable mod identity grouping.</param>
/// <param name="Warnings">Individual metadata read failures.</param>
/// <param name="UpdatedMods">Recognized mod updates.</param>
/// <param name="AddedMods">Recognized mod additions.</param>
/// <param name="RemovedMods">Recognized mod removals.</param>
/// <param name="OtherFiles">Remaining file changes.</param>
public sealed record PackChangeSummary(IReadOnlyList<PackModChange> Mods, IReadOnlyList<PackFileChange> Files, IReadOnlyList<PackMetadataWarning> Warnings,
    int UpdatedMods, int AddedMods, int RemovedMods, int OtherFiles);

/// <summary>An unambiguous mod identity change backed by its actual file decisions.</summary>
/// <param name="Action">The grouped update, add or delete action.</param>
/// <param name="Id">The declared mod identifier.</param>
/// <param name="Name">The declared display name.</param>
/// <param name="BeforeVersion">The previous declared version.</param>
/// <param name="AfterVersion">The new declared version.</param>
/// <param name="BeforePath">The previous package path.</param>
/// <param name="AfterPath">The new package path.</param>
/// <param name="Files">The file decisions establishing this change.</param>
public sealed record PackModChange(string Action, string Id, string Name, string? BeforeVersion, string? AfterVersion, string? BeforePath, string? AfterPath, IReadOnlyList<PackFileChange> Files);

/// <summary>A recoverable mod metadata read failure in a pack review.</summary>
/// <param name="Path">The affected package path.</param>
/// <param name="Message">The original read failure.</param>
public sealed record PackMetadataWarning(string Path, string Message);