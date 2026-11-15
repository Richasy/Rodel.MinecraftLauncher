// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>The retained content preparation of a pack, separate from runtime or game readiness.</summary>
public sealed record PackContentState : PackInspection
{
    /// <summary>Gets the ownership document schema version.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets or sets the files whose verified bytes belong to this pack.</summary>
    public IReadOnlyList<PackFileRecord> Files { get; set; } = [];
    /// <summary>Gets or sets declarations which have not been acquired.</summary>
    public List<PackMissingFile> Missing { get; set; } = [];
    /// <summary>Gets or sets declarations intentionally omitted according to client or player policy.</summary>
    public IReadOnlyList<PackSkippedFile> Skipped { get; set; } = [];
    /// <summary>Gets the effective optional-file policy and retained path decisions.</summary>
    public PackOptionalState OptionalSelection { get; init; } = new();
    /// <summary>Gets or sets decisions evaluated during initial import.</summary>
    public IReadOnlyList<PackOptionalChoice> OptionalChoices { get; set; } = [];
    /// <summary>Gets or sets whether all selected declarations have verified bytes.</summary>
    public bool Complete { get; set; }
    /// <summary>Gets actual acquisition counts and network permission for this attempt.</summary>
    public PackAcquisition? Acquisition { get; init; }
    /// <summary>Gets or sets the selected online archive provenance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OnlinePackSelection? OnlineSource { get; set; }
    /// <summary>Gets or sets the transaction which currently owns the installed baseline.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationId { get; set; }
    /// <summary>Gets or sets the last application comparison against the selected release.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackConsistency? Consistency { get; set; }
    /// <summary>Gets or sets explicit source detachment without deleting original provenance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SourceDetached { get; set; }
    /// <summary>Gets or sets unknown persisted ownership fields retained through content maintenance.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
    /// <summary>Gets whether the retained HMCL manifest has a usable HTTP(S) source.</summary>
    [JsonIgnore]
    public bool HasDistributionSource => Format == "hmcl" && HmclPack.DistributionRoot(Manifest) is not null;
}

/// <summary>One verified pack-owned file; raw publisher metadata is not used as the receipt itself.</summary>
public sealed record PackFileRecord
{
    /// <summary>Gets the owned relative destination.</summary>
    public required string Path { get; init; }
    /// <summary>Gets the recognized content kind, if any.</summary>
    public string? Kind { get; init; }
    /// <summary>Gets whether this tracked path is enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets the verified SHA-1.</summary>
    public string? Sha1 { get; init; }
    /// <summary>Gets the verified SHA-512.</summary>
    public string? Sha512 { get; init; }
    /// <summary>Gets the actual byte length.</summary>
    public long? Size { get; init; }
    /// <summary>Gets the known publisher platform.</summary>
    public string? Platform { get; init; }
    /// <summary>Gets upstream metadata, including arrays, scalars and null from independently tracked content.</summary>
    public JsonNode? Metadata { get; init; }
    /// <summary>Gets the original publisher download value, which need not be usable when local verified bytes were supplied.</summary>
    [JsonPropertyName("downloadUrl")]
    public JsonNode? RawDownloadUrl { get; set; }
    /// <summary>Reads the retained URL only for an operation that actually needs a publisher reference.</summary>
    [JsonIgnore]
    public string? DownloadUrl => RawDownloadUrl?.GetValue<string>();
    /// <summary>Gets or sets the declared project identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; set; }
    /// <summary>Gets or sets the declared publisher file identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionId { get; set; }
    /// <summary>Gets or sets whether platform hashes proved the local file identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IdentityVerified { get; set; }
    /// <summary>Gets or sets whether platform metadata supplied the category.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClassificationVerified { get; set; }
    /// <summary>Gets or sets a category fallback explanation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClassificationWarning { get; set; }
    /// <summary>Gets or sets an unverifiable identity explanation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; set; }
    /// <summary>Gets or sets the actual local or download acquisition.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Acquisition { get; set; }
    /// <summary>Gets or sets unknown persisted tracking fields, not a substitute for known receipt fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>A selected declaration which still needs bytes or a local conflict resolved.</summary>
public sealed record PackMissingFile
{
    /// <summary>Gets the declared relative destination, when known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    /// <summary>Gets the file acquisition or conflict state.</summary>
    public required string State { get; init; }
    /// <summary>Gets the original acquisition failure explanation.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets the CurseForge project identifier for manual matching.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ProjectId { get; init; }
    /// <summary>Gets the CurseForge file identifier for manual matching.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FileId { get; init; }
    /// <summary>Gets a machine-readable availability reason when supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; init; }
    /// <summary>Gets the explicit project:file manual mapping key.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManualKey { get; init; }
    /// <summary>Gets the matching manual fallback file name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManualFileName { get; init; }
    /// <summary>Gets the original file declaration needed for exact completion and export.</summary>
    public required JsonObject Metadata { get; init; }
}

/// <summary>A publisher declaration retained together with the launcher's exclusion reason.</summary>
/// <param name="Declaration">The complete publisher declaration and normalized acquisition fields.</param>
/// <param name="SkipReason">The explicit client or player policy which omitted this declaration.</param>
[JsonConverter(typeof(PackSkippedFileConverter))]
public sealed record PackSkippedFile(PackFileDeclaration Declaration, string SkipReason);

internal sealed class PackSkippedFileConverter : JsonConverter<PackSkippedFile>
{
    /// <inheritdoc/>
    public override PackSkippedFile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var metadata = JsonNode.Parse(ref reader)!.AsObject();
        var reason = metadata["skipReason"]!.GetValue<string>();
        metadata.Remove("skipReason");
        return new(new(metadata), reason);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PackSkippedFile value, JsonSerializerOptions options)
    {
        var metadata = value.Declaration.ToMetadata();
        metadata["skipReason"] = value.SkipReason;
        metadata.WriteTo(writer, options);
    }
}

/// <summary>The effective all-policy plus explicit per-path decisions for optional client files.</summary>
public sealed record PackOptionalState
{
    /// <summary>Gets the fallback policy for an undecided optional path.</summary>
    public string Policy { get; init; } = "include-all";
    /// <summary>Gets decisions retained for individual optional paths.</summary>
    public Dictionary<string, bool> Paths { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Determines the effective decision without changing required or unsupported declarations.</summary>
    public bool Includes(string path) => Paths.TryGetValue(path, out var selected) ? selected : Policy != "exclude-all";
}

/// <summary>Reports one evaluated optional-file decision.</summary>
/// <param name="Path">The relative declared file path.</param>
/// <param name="Selected">Whether this optional file was selected.</param>
public sealed record PackOptionalChoice(string Path, bool Selected);

/// <summary>Reports actual acquisition work without implying runtime readiness.</summary>
/// <param name="NetworkAllowed">Whether this attempt allowed downloads.</param>
/// <param name="LocalDeclaredFiles">Declarations acquired from local or embedded bytes.</param>
/// <param name="DownloadedDeclaredFiles">Declarations transferred over the network.</param>
/// <param name="ArchiveFiles">Files extracted from the source archive.</param>
/// <param name="MissingDeclaredFiles">Selected declarations still missing.</param>
/// <param name="SkippedDeclaredFiles">Declarations intentionally omitted.</param>
public sealed record PackAcquisition(bool NetworkAllowed, int LocalDeclaredFiles, int DownloadedDeclaredFiles, int ArchiveFiles, int MissingDeclaredFiles, int SkippedDeclaredFiles);

/// <summary>Separates file availability from agreement with the publisher's release.</summary>
/// <param name="FilesAvailable">All selected declarations have bytes.</param>
/// <param name="MatchesRelease">Installed files match this release without local differences.</param>
/// <param name="Differences">Files whose actual and declared hashes differ.</param>
public sealed record PackConsistency(bool FilesAvailable, bool MatchesRelease, IReadOnlyList<PackFileDifference> Differences);

/// <summary>An actual file difference remaining after application.</summary>
/// <param name="Path">The relative file path.</param>
/// <param name="Action">The action taken or preserved.</param>
/// <param name="ExpectedSha1">The selected release hash.</param>
/// <param name="ActualSha1">The retained installed hash.</param>
public sealed record PackFileDifference(string Path, string Action, string? ExpectedSha1, string? ActualSha1);

/// <summary>A pack content result with typed recovery information, independent of later preparation.</summary>
public sealed record PackState : PackResult
{
    /// <summary>Gets the retained instance registration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstanceSummary? Instance { get; set; }
    /// <summary>Gets the retained pack content state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackContentState? Pack { get; set; }
    /// <summary>Gets whether selected pack files are available.</summary>
    public bool Complete { get; set; }
    /// <summary>Gets exact missing declarations.</summary>
    public IReadOnlyList<PackMissingFile> Missing { get; set; } = [];
    /// <summary>Gets the open publisher dependency map, retaining unrecognized keys and values.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Dependencies { get; init; } = new Dictionary<string, JsonNode?>();
    /// <summary>Gets the last release comparison, if an update was applied.</summary>
    public PackConsistency? Consistency { get; init; }
    /// <summary>Gets the continuation route when declarations remain missing.</summary>
    public PackRecovery? Recovery { get; init; }
    /// <summary>Gets whether runtime preparation was performed by this result.</summary>
    public bool RuntimePrepared { get; set; }
    /// <summary>Gets the content-only outcome explanation.</summary>
    public string Message { get; set; } = "Pack content imported; install the declared Minecraft and loader dependencies before launching.";
    /// <summary>Gets or sets the runtime-domain preparation result.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimePreparationResult? Runtime { get; set; }
    /// <summary>Gets or sets the actual Java selection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JavaSelectionReport? Java { get; set; }
    /// <summary>Gets or sets a recoverable Java acquisition failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaWarning { get; set; }
    /// <summary>Gets or sets ordered workflow progress and completion evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackPreparation? Preparation { get; set; }
    /// <summary>Gets or sets the exact selected online artifact provenance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OnlinePackSelection? OnlineSource { get; set; }
}

/// <summary>The supported continuation route for a retained incomplete pack.</summary>
/// <param name="Command">The canonical completion command for this instance.</param>
/// <param name="ManualDirectoryOption">The option for explicit local files.</param>
/// <param name="PreservesOverrides">Whether completion retains already imported overrides.</param>
public sealed record PackRecovery(string Command, string ManualDirectoryOption = "--manual-directory", bool PreservesOverrides = true);

/// <summary>Reports a pack export's actual reference and bundled-file counts.</summary>
/// <param name="Path">The published archive path.</param>
/// <param name="Format">The selected archive format.</param>
/// <param name="ReferencedFiles">Publisher references written.</param>
/// <param name="OverrideFiles">Local override files bundled.</param>
/// <param name="Excluded">Files requiring separate redistribution authorization.</param>
/// <param name="Complete">Whether no eligible file was excluded.</param>
public sealed record PackExportResult(string Path, string Format, int ReferencedFiles, int OverrideFiles, IReadOnlyList<PackExcludedFile> Excluded, bool Complete);

/// <summary>A local export candidate omitted without explicit redistribution permission.</summary>
/// <param name="Path">The omitted relative file path.</param>
/// <param name="Reason">The reason for omission.</param>
public sealed record PackExcludedFile(string Path, string Reason);

/// <summary>Lists persisted transactions together with independently unreadable records.</summary>
/// <param name="Operations">Readable transactions owned by the selected instance.</param>
/// <param name="Warnings">Unreadable transaction records.</param>
public sealed record PackOperationList(IReadOnlyList<PackOperationSummary> Operations, IReadOnlyList<PackOperationWarning> Warnings);

/// <summary>Describes one persisted pack transaction without exposing its mutable journal.</summary>
/// <param name="OperationId">The transaction identifier.</param>
/// <param name="Phase">The persisted transaction state.</param>
/// <param name="CreatedAt">The persisted creation time.</param>
/// <param name="VersionId">The publisher's original version value.</param>
/// <param name="Current">Whether this transaction owns the installed baseline.</param>
/// <param name="ReportPath">The original durable report path.</param>
public sealed record PackOperationSummary(string? OperationId, string? Phase, string? CreatedAt, JsonNode? VersionId, bool Current, string ReportPath);

/// <summary>Reports one unreadable transaction without suppressing readable history.</summary>
/// <param name="OperationId">The affected transaction directory.</param>
/// <param name="Error">The original read failure.</param>
public sealed record PackOperationWarning(string OperationId, string Error);