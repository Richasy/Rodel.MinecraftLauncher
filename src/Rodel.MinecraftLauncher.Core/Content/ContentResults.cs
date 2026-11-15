// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Represents the closed set of content operation outcomes, including single-item and batch results.</summary>
[JsonDerivedType(typeof(ContentEntry))]
[JsonDerivedType(typeof(ContentRemovalResult))]
[JsonDerivedType(typeof(ContentDetachResult))]
[JsonDerivedType(typeof(ContentIdentityResult))]
[JsonDerivedType(typeof(ContentInstallPlan))]
[JsonDerivedType(typeof(ContentInstallReceipt))]
[JsonDerivedType(typeof(ContentUpdateSkipped))]
[JsonDerivedType(typeof(ContentBatchResult))]
public abstract class ContentResult;

/// <summary>Reports a recoverable removal without deleting the selected content permanently.</summary>
public sealed class ContentRemovalResult : ContentResult
{
    /// <summary>Gets whether the selected content was removed from its active location.</summary>
    public bool Removed { get; init; } = true;
    /// <summary>Gets the originally selected file name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the recoverable destination.</summary>
    public required string RecoveryPath { get; init; }
    /// <summary>Gets an additional cross-volume transfer recovery path.</summary>
    public string? TransferRecoveryPath { get; init; }
    /// <summary>Gets the game-settings subsystem's selection adjustment evidence.</summary>
    public GameSettingsChangeResult? ResourcePackSelection { get; init; }
    /// <summary>Gets a recoverable selection warning.</summary>
    public string? Warning { get; init; }
}

/// <summary>Reports provenance detachment independently from physical content changes.</summary>
public sealed class ContentDetachResult : ContentResult
{
    /// <summary>Gets the selected file name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets whether an existing tracking entry was detached.</summary>
    public bool Detached { get; init; }
    /// <summary>Gets whether physical content changed; detachment never changes files.</summary>
    public bool FilesChanged { get; init; }
}

/// <summary>Reports hash identification while retaining the provider's extensible provenance document.</summary>
public sealed class ContentIdentityResult : ContentResult
{
    /// <summary>Gets whether the provider matched the file.</summary>
    public bool Matched { get; init; }
    /// <summary>Gets the nonmatch reason, when supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
    /// <summary>Gets the queried digest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; init; }
    /// <summary>Gets the queried hash algorithm.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Algorithm { get; init; }
    /// <summary>Gets whether an unmatched package remains locally usable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UsableLocally { get; init; }
    /// <summary>Gets whether matching provenance was attached.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Attached { get; init; }
    /// <summary>Gets the source document, including original provider metadata.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentProvenance? Provenance { get; init; }
    /// <summary>Gets the actual tracked content after attachment.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentEntry? Content { get; set; }
    /// <summary>Gets incomplete-context guidance without blocking local use.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; set; }
    /// <summary>Gets the instance compatibility used by the caller.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentCompatibility? ContentCompatibility { get; set; }
}

/// <summary>Describes a verified source identity while retaining the entire provider release response.</summary>
public sealed record ContentProvenance
{
    /// <summary>Gets the verified platform.</summary>
    public required string Platform { get; init; }
    /// <summary>Gets the matched project.</summary>
    public required string ProjectId { get; init; }
    /// <summary>Gets the matched exact release.</summary>
    public required string VersionId { get; init; }
    /// <summary>Gets the matched download location, including an explicitly unknown URL.</summary>
    public string? DownloadUrl { get; init; }
    /// <summary>Gets the original provider response including unfamiliar fields.</summary>
    public required JsonObject Upstream { get; init; }
    /// <summary>Gets an explicitly supplied game compatibility context.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameVersion { get; init; }
    /// <summary>Gets an explicitly supplied loader context.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Loader { get; init; }
}

/// <summary>Freezes exact content candidates and dependencies against a tracking baseline.</summary>
public sealed class ContentInstallPlan : ContentResult
{
    /// <summary>Gets or sets unrecognized fields from a persisted review without deleting them on readback.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets the normalized source platform.</summary>
    public required string Platform { get; init; }
    /// <summary>Gets the first selected root project.</summary>
    public required string PrimaryProject { get; init; }
    /// <summary>Gets all selected roots when this is a regular installation plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PrimaryProjects { get; init; }
    /// <summary>Gets the target game directory.</summary>
    public required string GameDirectory { get; init; }
    /// <summary>Gets the effective channel, including the unrestricted any value.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EffectiveChannel { get; init; }
    /// <summary>Gets the exact content-lock baseline.</summary>
    public required string LockFingerprint { get; init; }
    /// <summary>Gets the resolved candidates in dependency order.</summary>
    public required IReadOnlyList<ContentPlanNode> Nodes { get; init; }
    /// <summary>Gets the dependency provenance edges.</summary>
    public required IReadOnlyList<ContentPlanEdge> Edges { get; init; }
    /// <summary>Gets classified conflict evidence, including original provider dependency values.</summary>
    public required List<ContentPlanFinding> Conflicts { get; init; }
    /// <summary>Gets classified advisory evidence without discarding provider details.</summary>
    public required List<ContentPlanFinding> Warnings { get; init; }
    /// <summary>Gets or sets whether the frozen selection has unresolved conflicts.</summary>
    public bool CanApply { get; set; }
    /// <summary>Gets the compatibility filters used for a tracked update.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentUpdateContext? UpdateContext { get; set; }
    /// <summary>Gets the caller's resolved instance compatibility.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentCompatibility? ContentCompatibility { get; set; }
}

/// <summary>Describes one exact downloadable candidate, retaining third-party dependency and release metadata.</summary>
public sealed record ContentPlanNode
{
    /// <summary>Gets or sets additional frozen-review fields not interpreted by this version.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets the normalized source platform.</summary>
    public required string Platform { get; init; }
    /// <summary>Gets the canonical project identifier.</summary>
    public required string ProjectId { get; init; }
    /// <summary>Gets the exact release identifier.</summary>
    public required string VersionId { get; init; }
    /// <summary>Gets the verified safe destination name.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the content directory kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets the optional world scope.</summary>
    public string? World { get; init; }
    /// <summary>Gets the provider's download URL.</summary>
    public string? DownloadUrl { get; init; }
    /// <summary>Gets the expected SHA-1 digest.</summary>
    public string? Sha1 { get; init; }
    /// <summary>Gets the expected SHA-512 digest.</summary>
    public string? Sha512 { get; init; }
    /// <summary>Gets the expected download length.</summary>
    public long? Size { get; init; }
    /// <summary>Gets the selected game compatibility filter.</summary>
    public string? GameVersion { get; init; }
    /// <summary>Gets the selected loader filter.</summary>
    public string? Loader { get; init; }
    /// <summary>Gets whether this candidate was resolved as a dependency.</summary>
    public bool Dependency { get; init; }
    /// <summary>Gets the original provider dependency declarations.</summary>
    public JsonNode? Dependencies { get; init; }
    /// <summary>Gets the original extensible release response.</summary>
    public required JsonObject Upstream { get; init; }
    /// <summary>Gets the original tracking records replaced by this candidate.</summary>
    public IReadOnlyList<JsonObject> ExistingFiles { get; set; } = [];
    /// <summary>Gets or sets the planned add, replacement or reuse action.</summary>
    public string Change { get; set; } = "add";
    /// <summary>Gets or sets the already inspected local content reused without downloading.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentEntry? ReuseLocal { get; set; }
    /// <summary>Gets or sets the exact untracked local file replaced by a verified or explicitly selected source after plan confirmation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentEntry? LocalReplacement { get; set; }
    /// <summary>Gets or sets whether an explicitly selected search source must preserve the local native mod IDs during commit.</summary>
    public bool VerifyReplacementModIds { get; set; }
    /// <summary>Gets or sets the explicitly requested enabled state; absence preserves existing state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DesiredEnabled { get; set; }
    /// <summary>Gets or sets the provider's display title.</summary>
    public string? Title
    {
        get;
        set { field = value; PresentationRead = true; }
    }
    /// <summary>Gets or sets the provider's icon URL.</summary>
    public string? IconUrl { get; set; }
    /// <summary>Gets the verified download or local source used during commit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
    /// <summary>Gets verified mod identifiers supplied by a dependency repair candidate.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Provides { get; set; }
    /// <summary>Gets display names of verified dependency consumers.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RequestedBy { get; set; }
    [JsonIgnore]
    internal bool PresentationRead { get; set; }
}

/// <summary>Records why an exact project version appears in a plan.</summary>
public sealed record ContentPlanEdge
{
    /// <summary>Gets the consumer project, or null for a root.</summary>
    public string? RequiredBy { get; init; }
    /// <summary>Gets the required project.</summary>
    public required string ProjectId { get; init; }
    /// <summary>Gets the dependency's declared exact version, when supplied.</summary>
    public string? RequestedVersion
    {
        get;
        init { field = value; RequestedVersionPresent = true; }
    }
    /// <summary>Gets the selected exact release.</summary>
    public required string SelectedVersion { get; init; }
    [JsonIgnore]
    internal bool RequestedVersionPresent { get; init; }
}

/// <summary>Reports a committed dependency group and its recovery evidence.</summary>
public sealed class ContentInstallReceipt : ContentResult
{
    /// <summary>Gets the committed group identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the durable recovery journal path.</summary>
    public required string Journal { get; init; }
    /// <summary>Gets the actual installed content, including retained tracking extensions.</summary>
    public required IReadOnlyList<ContentEntry> Installed { get; init; }
    /// <summary>Gets selection coordination evidence from game settings.</summary>
    public GameSettingsChangeResult? ResourcePackSelection { get; init; }
    /// <summary>Gets a recoverable selection warning.</summary>
    public string? Warning { get; init; }
    /// <summary>Gets the actual first root package.</summary>
    public required ContentEntry Primary { get; init; }
    /// <summary>Gets or sets the normalized source platform.</summary>
    public string? Platform { get; set; }
    /// <summary>Gets or sets the reviewed advisory evidence.</summary>
    public IReadOnlyList<ContentPlanFinding> Warnings { get; set; } = [];
    /// <summary>Gets or sets the reviewed dependency provenance.</summary>
    public IReadOnlyList<ContentPlanEdge> Edges { get; set; } = [];
    /// <summary>Gets whether this operation applied an update.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Updated { get; set; }
    /// <summary>Gets the reviewed update filters.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentUpdateContext? UpdateContext { get; set; }
    /// <summary>Gets the resolved instance compatibility.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentCompatibility? ContentCompatibility { get; set; }
}

/// <summary>Retains selected and recorded compatibility independently for an update.</summary>
public sealed record ContentUpdateContext
{
    /// <summary>Gets whether filters came from the current instance or recorded source.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the selected game version.</summary>
    public string? MinecraftVersion { get; init; }
    /// <summary>Gets the selected loader.</summary>
    public string? Loader { get; init; }
    /// <summary>Gets the previously recorded game version.</summary>
    public string? RecordedMinecraftVersion { get; init; }
    /// <summary>Gets the previously recorded loader.</summary>
    public string? RecordedLoader { get; init; }
    /// <summary>Gets incomplete-context guidance.</summary>
    public string? Warning { get; init; }
}

/// <summary>Explains an update that made no file changes.</summary>
public sealed class ContentUpdateSkipped : ContentResult
{
    /// <summary>Gets source suggestions discovered from native mod metadata; none is selected automatically.</summary>
    public IReadOnlyList<ContentSourceCandidate> SourceCandidates { get; init; } = [];
    /// <summary>Gets whether an update occurred; skipped updates remain false.</summary>
    public bool Updated { get; init; }
    /// <summary>Gets the machine-readable skip reason.</summary>
    public required string Reason { get; init; }
    /// <summary>Gets the selected installed file.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets optional recoverable upstream or context guidance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; init; }
    /// <summary>Gets explicit evidence that existing content was retained.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LocalRetained { get; init; }
    /// <summary>Gets the update compatibility filters, when resolved.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentUpdateContext? UpdateContext { get; init; }
}

/// <summary>Describes a Modrinth search suggestion without claiming that the installed file belongs to that project.</summary>
public sealed record ContentSourceCandidate
{
    /// <summary>Gets the platform's project identifier, independently of the native mod ID.</summary>
    public required string ProjectId { get; init; }
    /// <summary>Gets the project's display title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets the project author's public display name, when available.</summary>
    public string? Author { get; init; }
    /// <summary>Gets the project's human-readable URL identifier.</summary>
    public string? Slug { get; init; }
    /// <summary>Gets the optional public project icon URL supplied by the search provider.</summary>
    public string? IconUrl { get; init; }
}

/// <summary>Reports each committed item in a content batch without hiding partial failure.</summary>
public sealed class ContentBatchResult : ContentResult
{
    /// <summary>Gets the per-item outcomes in execution order.</summary>
    public required IReadOnlyList<ContentItemOutcome> Items { get; init; }
    /// <summary>Gets the count of unsuccessful items.</summary>
    public int FailedCount { get; init; }
    /// <summary>Gets the count of successfully retained operations.</summary>
    public int SucceededCount { get; init; }
}

/// <summary>Relates one batch input to its concrete result or open operation failure evidence.</summary>
[JsonDerivedType(typeof(ContentItemSuccess))]
[JsonDerivedType(typeof(ContentItemFailure))]
public abstract class ContentItemOutcome
{
    /// <summary>Gets the original batch item for ordinary mutations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Item { get; init; }
}

/// <summary>Relates a successful batch item to its concrete content result.</summary>
public sealed class ContentItemSuccess : ContentItemOutcome
{
    /// <summary>Gets the source path for a reviewed local import.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
    /// <summary>Gets the selected content kind for a reviewed import.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }
    /// <summary>Gets the stable operation status.</summary>
    public LauncherResultStatus Status { get; init; } = LauncherResultStatus.Succeeded;
    /// <summary>Gets the concrete successful outcome.</summary>
    public required ContentResult Data { get; init; }
}

/// <summary>Retains the standard operation failure contract for a failed batch item.</summary>
public sealed class ContentItemFailure : ContentItemOutcome
{
    /// <summary>Gets the operation envelope schema version.</summary>
    public int SchemaVersion { get; init; }
    /// <summary>Gets the actual failed or canceled status.</summary>
    public LauncherResultStatus Status { get; init; }
    /// <summary>Gets extensible failure evidence supplied by the failed operation.</summary>
    public JsonNode? Data { get; init; }
    /// <summary>Gets the standard classified operation error.</summary>
    public LauncherOperationError? Error { get; init; }
}