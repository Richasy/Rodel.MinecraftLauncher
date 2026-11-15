// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>A lossless public view of one durable repair incident, not an authorization to replay its trials.</summary>
public sealed record AiRepairReport
{
    /// <summary>Gets the incident identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the originally authorized instance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the original failed run, never a later verification run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginSessionId { get; init; }
    /// <summary>Gets the original preparation failure when no game run existed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginJobId { get; init; }
    /// <summary>Gets the durable incident creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedUtc { get; init; }
    /// <summary>Gets the observed incident lifecycle state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the recorded scoped authorization kind, not a new grant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Authorization { get; init; }
    /// <summary>Gets the bound repair conversation, if created.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationId { get; init; }
    /// <summary>Gets whether entry stopped before submission because no model was configured.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NeedsModel { get; init; }
    /// <summary>Gets the fixed repair scope description captured with the incident.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }
    /// <summary>Gets the immutable original session or preparation diagnosis, retaining its source-specific evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OriginalDiagnosis { get; init; }
    /// <summary>Gets the original content snapshot including open tracking provenance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairContentFile[]? OriginalInventory { get; init; }
    /// <summary>Gets the original lossless configuration snapshot, including unknown keys and explicit nulls.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OriginalSettings { get; init; }
    /// <summary>Gets the durable trials in creation order.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairPlanReport[]? Plans { get; init; }
    /// <summary>Gets bounded tool-result references recorded for this incident.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ResultIds { get; init; }
    /// <summary>Gets the fresh content observation when explicitly inspected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairContentFile[]? CurrentInventory { get; init; }
    /// <summary>Gets the current effective configuration snapshot, preserving open configuration fields.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? EffectiveSettings { get; init; }
    /// <summary>Gets the current content analysis only when explicitly inspected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Content.ContentAnalysis? ModAnalysis { get; init; }
    /// <summary>Gets usable host-discovered Java candidates, never model-supplied executable paths.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairJavaCandidate[]? JavaCandidates { get; init; }
    /// <summary>Gets unknown, null and noncanonical persisted fields unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiRepairReport Read(JsonObject value) => new()
    {
        Id = StoredReportFields.Text(value, "id"),
        InstanceId = StoredReportFields.Text(value, "instanceId"),
        OriginSessionId = StoredReportFields.Text(value, "originSessionId"),
        OriginJobId = StoredReportFields.Text(value, "originJobId"),
        CreatedUtc = StoredReportFields.Text(value, "createdUtc"),
        Status = StoredReportFields.Text(value, "status"),
        Authorization = StoredReportFields.Text(value, "authorization"),
        ConversationId = StoredReportFields.Text(value, "conversationId"),
        NeedsModel = StoredReportFields.Value<bool>(value, "needsModel"),
        Scope = StoredReportFields.Text(value, "scope"),
        OriginalDiagnosis = AiReportFields.Object(value, "originalDiagnosis"),
        OriginalInventory = AiRepairReportFields.Records(value, "originalInventory", AiRepairContentFile.Read),
        OriginalSettings = AiReportFields.Object(value, "originalSettings"),
        Plans = AiRepairReportFields.Records(value, "plans", AiRepairPlanReport.Read),
        ResultIds = StoredReportFields.TextArray(value, "resultIds"),
        AdditionalData = StoredReportFields.Remaining(value)
    };
}

/// <summary>A durable, exact repair trial and its actual write/verification evidence.</summary>
public sealed record AiRepairPlanReport
{
    /// <summary>Gets the frozen trial identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets disable_mod, java, memory or dependencies.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }
    /// <summary>Gets the evidence-based explanation captured before dispatch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
    /// <summary>Gets the trial lifecycle state, independently of the model's answer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }
    /// <summary>Gets the trial creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedUtc { get; init; }
    /// <summary>Gets the version binding checked before applying the trial.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionId { get; init; }
    /// <summary>Gets the deduplication signature of this exact candidate.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; init; }
    /// <summary>Gets the observed mod archive targeted by a disable trial.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairContentFile? Target { get; init; }
    /// <summary>Gets the frozen content observation before applying the trial.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairContentFile[]? BeforeInventory { get; init; }
    /// <summary>Gets the actual content read back after the trial.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiRepairContentFile[]? AfterInventory { get; init; }
    /// <summary>Gets the lossless override snapshot used by stale-plan and rollback checks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? BeforeSettings { get; init; }
    /// <summary>Gets the effective configuration snapshot checked before dispatch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? BeforeEffectiveSettings { get; init; }
    /// <summary>Gets actual overrides read after the trial, preserving open configuration keys.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? AfterSettings { get; init; }
    /// <summary>Gets the exact settings patch captured by this trial, including explicit nulls.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Changes { get; init; }
    /// <summary>Gets the frozen dependency review from the content subsystem, not a fresh resolution.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? DependencyReview { get; init; }
    /// <summary>Gets the unique tagged verification session when known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerificationSessionId { get; init; }
    /// <summary>Gets the actual diagnostic evidence observed during verification.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Diagnosis { get; init; }
    /// <summary>Gets the observed outcome, not an assumption of full game readiness.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Outcome { get; init; }
    /// <summary>Gets whether the original fault signature was observed again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OriginalFaultObserved { get; init; }
    /// <summary>Gets the verified scope and the explicitly unverified conditions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerificationScope { get; init; }
    /// <summary>Gets heterogeneous operation failure information without inferring rollback safety.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
    /// <summary>Gets launch-dispatch failure evidence without authorizing another launch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? LaunchError { get; init; }
    /// <summary>Gets unknown, null and noncanonical persisted fields unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiRepairPlanReport Read(JsonObject value) => new()
    {
        Id = StoredReportFields.Text(value, "id"),
        Kind = StoredReportFields.Text(value, "kind"),
        Reason = StoredReportFields.Text(value, "reason"),
        State = StoredReportFields.Text(value, "state"),
        CreatedUtc = StoredReportFields.Text(value, "createdUtc"),
        VersionId = StoredReportFields.Text(value, "versionId"),
        Signature = StoredReportFields.Text(value, "signature"),
        Target = AiReportFields.Object(value, "target") is { } target ? AiRepairContentFile.Read(target) : null,
        BeforeInventory = AiRepairReportFields.Records(value, "beforeInventory", AiRepairContentFile.Read),
        AfterInventory = AiRepairReportFields.Records(value, "afterInventory", AiRepairContentFile.Read),
        BeforeSettings = AiReportFields.Object(value, "beforeSettings"),
        BeforeEffectiveSettings = AiReportFields.Object(value, "beforeEffectiveSettings"),
        AfterSettings = AiReportFields.Object(value, "afterSettings"),
        Changes = AiReportFields.Object(value, "changes"),
        DependencyReview = AiReportFields.Object(value, "dependencyReview"),
        VerificationSessionId = StoredReportFields.Text(value, "verificationSessionId"),
        Diagnosis = AiReportFields.Object(value, "diagnosis"),
        Outcome = StoredReportFields.Text(value, "outcome"),
        OriginalFaultObserved = StoredReportFields.Value<bool>(value, "originalFaultObserved"),
        VerificationScope = StoredReportFields.Text(value, "verificationScope"),
        Error = AiReportFields.Node(value, "error"),
        LaunchError = AiReportFields.Node(value, "launchError"),
        AdditionalData = StoredReportFields.Remaining(value)
    };
}

/// <summary>The lossless content fingerprint used by repair stale-plan and rollback checks.</summary>
public sealed record AiRepairContentFile
{
    /// <summary>Gets the exact observed file name, including the disabled suffix.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; init; }
    /// <summary>Gets the observed content hash.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sha1 { get; init; }
    /// <summary>Gets the actual enabled state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; init; }
    /// <summary>Gets whether the entry is a directory rather than a mod archive.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Directory { get; init; }
    /// <summary>Gets source-specific tracking metadata without normalizing unknown values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Tracking { get; init; }
    /// <summary>Gets unknown, null and noncanonical stored fields unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    internal static AiRepairContentFile Read(JsonObject value) => new()
    {
        FileName = StoredReportFields.Text(value, "fileName"),
        Sha1 = StoredReportFields.Text(value, "sha1"),
        Enabled = StoredReportFields.Value<bool>(value, "enabled"),
        Directory = StoredReportFields.Value<bool>(value, "directory"),
        Tracking = AiReportFields.Node(value, "tracking"),
        AdditionalData = StoredReportFields.Remaining(value)
    };
}

/// <summary>A usable runtime actually returned by host Java discovery.</summary>
/// <param name="Path">The discovered executable path.</param>
/// <param name="MajorVersion">The observed Java major version.</param>
public sealed record AiRepairJavaCandidate(string Path, int? MajorVersion);

internal static class AiRepairReportFields
{
    internal static T[]? Records<T>(JsonObject fields, string key, Func<JsonObject, T> read)
    {
        if (fields[key] is not JsonArray array || array.Any(item => item is not JsonObject)) return null;
        fields.Remove(key);
        return [.. array.Cast<JsonObject>().Select(read)];
    }
}