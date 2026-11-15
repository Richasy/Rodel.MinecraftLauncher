// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A task's observed lifecycle state, retaining unknown persisted fields and operation-specific data.</summary>
public sealed record JobStatus
{
    /// <summary>Gets the persisted task identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the observed lifecycle status, including interrupted and unreadable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the UTC creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; init; }
    /// <summary>Gets the UTC completion timestamp, if recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishedAt { get; init; }
    /// <summary>Gets the operation request; operation-specific parameters remain open JSON.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JobRequest? Request { get; init; }
    /// <summary>Gets whether the current request can be restarted safely.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanResume { get; init; }
    /// <summary>Gets the previous attempt identifier, if present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumedFrom { get; init; }
    /// <summary>Gets the frozen caller request identity used for idempotent durable admission.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientRequestId { get; init; }
    /// <summary>Gets whether this admission returned an existing matching job without dispatching another attempt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ExistingRequest { get; init; }
    /// <summary>Gets the recovery strategy associated with this request.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecoveryStrategy { get; init; }
    /// <summary>Gets the recorded owner's process identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; init; }
    /// <summary>Gets the owner's UTC process-start identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessStartUtc { get; init; }
    /// <summary>Gets whether activity cannot safely be determined.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ActivityUnknown { get; init; }
    /// <summary>Gets an owner identity or sidecar read warning.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerWarning { get; init; }
    /// <summary>Gets the operation-specific latest progress payload.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LauncherProgress? Progress { get; init; }
    /// <summary>Gets the latest progress-sidecar failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProgressWarning { get; init; }
    /// <summary>Gets an activity-log persistence warning.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivityLogWarning { get; init; }
    /// <summary>Gets the original failure message.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
    /// <summary>Gets the unreadable task record's path.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    /// <summary>Gets the operation-specific completed result.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Result { get; init; }
    /// <summary>Gets structured failure details, including operation-specific evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Failure { get; init; }
    /// <summary>Gets whether cancellation has been requested.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CancelRequested { get; init; }
    /// <summary>Gets unknown fields, malformed known values and explicit nulls from the persisted record.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static JobStatus FromStored(JsonObject record)
    {
        var fields = (JsonObject)record.DeepClone();
        var request = fields["request"] as JsonObject;
        if (request is not null) fields.Remove("request");
        var progress = fields["progress"] as JsonObject;
        if (progress is not null) fields.Remove("progress");
        var result = fields["result"];
        if (result is not null) fields.Remove("result");
        var failure = fields["failure"];
        if (failure is not null) fields.Remove("failure");
        return new()
        {
            Id = StoredReportFields.Text(fields, "id"),
            Status = StoredReportFields.Text(fields, "status"),
            CreatedAt = StoredReportFields.Text(fields, "createdAt"),
            FinishedAt = StoredReportFields.Text(fields, "finishedAt"),
            CanResume = StoredReportFields.Value<bool>(fields, "canResume"),
            ResumedFrom = StoredReportFields.Text(fields, "resumedFrom"),
            ClientRequestId = StoredReportFields.Text(fields, "clientRequestId"),
            ExistingRequest = StoredReportFields.Value<bool>(fields, "existingRequest"),
            RecoveryStrategy = StoredReportFields.Text(fields, "recoveryStrategy"),
            ProcessId = StoredReportFields.Value<int>(fields, "processId"),
            ProcessStartUtc = StoredReportFields.Text(fields, "processStartUtc"),
            ActivityUnknown = StoredReportFields.Value<bool>(fields, "activityUnknown"),
            OwnerWarning = StoredReportFields.Text(fields, "ownerWarning"),
            ProgressWarning = StoredReportFields.Text(fields, "progressWarning"),
            ActivityLogWarning = StoredReportFields.Text(fields, "activityLogWarning"),
            Error = StoredReportFields.Text(fields, "error"),
            Path = StoredReportFields.Text(fields, "path"),
            CancelRequested = StoredReportFields.Value<bool>(fields, "cancelRequested"),
            Request = request is null ? null : JobRequest.Read(request),
            Progress = progress is null ? null : LauncherProgress.Read(progress),
            Result = result,
            Failure = failure,
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}