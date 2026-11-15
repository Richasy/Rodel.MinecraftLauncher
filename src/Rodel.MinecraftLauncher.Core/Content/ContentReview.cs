// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Stores the exact local or online content selection awaiting confirmation.</summary>
public sealed class ContentReview
{
    /// <summary>Gets or sets unrecognized persisted review fields without rewriting or interpreting them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets the durable review identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the owning instance.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets the local or online review mode.</summary>
    public required string Mode { get; init; }
    /// <summary>Gets the update operation marker when applicable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }
    /// <summary>Gets the content kind of an installed update review.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }
    /// <summary>Gets the owning game directory.</summary>
    public required string GameDirectory { get; init; }
    /// <summary>Gets the actual runtime context checked for drift.</summary>
    public required ContentCompatibility Context { get; init; }
    /// <summary>Gets the reviewed physical inventory fingerprint.</summary>
    public required string Fingerprint { get; init; }
    /// <summary>Gets the physical and tracking snapshot used to admit independent concurrent online additions.</summary>
    public IReadOnlyList<ContentEntry>? OnlineBaseline { get; init; }
    /// <summary>Gets the complete reviewed source and policy records, including missing tracked files.</summary>
    public JsonObject? OnlineTracking { get; init; }
    /// <summary>Gets the reviewed tracking and policy baseline for updates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LockFingerprint { get; init; }
    /// <summary>Gets the exact online installation plan; no fresh lookup is performed during application.</summary>
    public ContentInstallPlan? Plan { get; init; }
    /// <summary>Gets per-file local inspection or installed update checks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ContentReviewItem>? Items { get; init; }
    /// <summary>Gets extensible combined-plan failure evidence for an update review.</summary>
    public LauncherOperationResult? PlanningFailure { get; init; }
    /// <summary>Gets whether local imports or updates have applicable selected changes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanApply { get; init; }
    /// <summary>Gets whether a saved review drifted from the current runtime or physical inventory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stale { get; set; }
}

/// <summary>Describes a reviewed local source or the update status of an installed file.</summary>
public sealed class ContentReviewItem
{
    /// <summary>Gets or sets unselected online sources found from this local file's native mod metadata.</summary>
    public IReadOnlyList<ContentSourceCandidate> SourceCandidates { get; set; } = [];
    /// <summary>Gets or sets unrecognized persisted item fields retained during review readback.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets the local source path, absent for an installed update.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
    /// <summary>Gets the selected file name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets recognized interpretations of a local source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ContentEntry>? Candidates { get; init; }
    /// <summary>Gets or sets a local review conflict.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Issue { get; set; }
    /// <summary>Gets or sets the selected local content kind.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }
    /// <summary>Gets or sets the selected source inspection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentEntry? Inspection { get; set; }
    /// <summary>Gets or sets the exact local source digest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFingerprint { get; set; }
    /// <summary>Gets or sets the planned local import or reuse action.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; set; }
    /// <summary>Gets or sets the exact installed mod files replaced only after this local review is confirmed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ContentEntry>? Replacements { get; set; }
    /// <summary>Gets or sets the recorded, hash-matched or explicitly selected project for an installed update.</summary>
    public string? ProjectId { get; set; }
    /// <summary>Gets the tracked exact release.</summary>
    public string? CurrentVersionId { get; init; }
    /// <summary>Gets the provider's original display version value.</summary>
    public JsonNode? CurrentVersion { get; init; }
    /// <summary>Gets the physical enabled state.</summary>
    public bool? Enabled { get; init; }
    /// <summary>Gets the stored pin policy.</summary>
    public bool? Pinned { get; init; }
    /// <summary>Gets the stored update channel.</summary>
    public string? Channel { get; init; }
    /// <summary>Gets or sets the skipped, available or failed update status.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }
    /// <summary>Gets or sets the machine-readable skip reason.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
    /// <summary>Gets or sets recoverable update guidance.</summary>
    public string? Warning
    {
        get;
        set { field = value; WarningPresent = true; }
    }
    /// <summary>Gets or sets the exact selected release.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetVersionId { get; set; }
    /// <summary>Gets or sets the original provider display version.</summary>
    public JsonNode? TargetVersion { get; set; }
    /// <summary>Gets or sets the target archive name.</summary>
    public string? TargetFileName { get; set; }
    /// <summary>Gets or sets extensible per-file failure evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LauncherOperationResult? Failure { get; set; }
    [JsonIgnore]
    internal bool WarningPresent { get; set; }
}

/// <summary>Combines per-file update checks with one frozen root/dependency plan.</summary>
/// <param name="Items">Per-file checks, including skipped and failed entries.</param>
/// <param name="Plan">The combined exact candidate plan, or null when none could be prepared.</param>
/// <param name="LockFingerprint">The tracking and policy baseline used for all checks.</param>
/// <param name="PlanningFailure">Extensible combined-plan failure evidence.</param>
/// <param name="CanApply">Whether the combined selection can be applied.</param>
public sealed record ContentUpdatesPlan(IReadOnlyList<ContentReviewItem> Items, ContentInstallPlan? Plan, string LockFingerprint, LauncherOperationResult? PlanningFailure, bool CanApply);

/// <summary>Reports the actual number of installed files checked for updates.</summary>
/// <param name="CompletedFiles">Number of completed checks, including failed or skipped files.</param>
/// <param name="TotalFiles">Number of requested files.</param>
public sealed record ContentCheckProgress(int CompletedFiles, int TotalFiles)
{
    /// <summary>Gets the stable progress stage understood by task observers.</summary>
    public string Stage { get; } = "content-update-check";
}

/// <summary>Freezes a dependency repair after inspecting actual downloaded mod declarations.</summary>
public sealed class DependencyRepairReview
{
    /// <summary>Gets or sets unrecognized persisted review fields retained without interpretation.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets the saved review identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the owning instance.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets the diagnostic session providing dependency evidence.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the actual runtime context checked before applying.</summary>
    public required ContentCompatibility Context { get; init; }
    /// <summary>Gets the reviewed mod inventory digest.</summary>
    public required string InventoryFingerprint { get; init; }
    /// <summary>Gets the frozen verified dependency plan.</summary>
    public required ContentInstallPlan Plan { get; init; }
    /// <summary>Gets unresolved requirements with original metadata range values.</summary>
    public required IReadOnlyList<ContentPlanFinding> Unresolved { get; init; }
    /// <summary>Gets whether planning changed installed files; it only downloads into cache.</summary>
    public bool InstalledFilesChanged { get; init; }
    /// <summary>Gets the supported inference boundaries.</summary>
    public string Limitations { get; init; } = "Fabric only; non-simple version predicates and ambiguous matches remain unresolved. Game/loader compatibility comes from Modrinth metadata; the loader remains authoritative.";
}