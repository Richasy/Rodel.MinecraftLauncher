// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Observed game-session state with original launch and unknown persisted evidence retained.</summary>
public sealed record SessionStatus : ILaunchResult
{
    /// <summary>Gets bounded startup timing evidence, retained independently of the released game-console buffer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StartupReport? Startup { get; init; }
    /// <summary>Gets the recorded session identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the associated instance identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the associated launch-task identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JobId { get; init; }
    /// <summary>Gets the observed lifecycle state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets process/window evidence: process_started or window_visible does not establish menu, world or server readiness.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Readiness { get; init; }
    /// <summary>Gets a failure to observe the recorded process tree's window; missing evidence must not imply a visible game.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowObservationWarning { get; init; }
    /// <summary>Gets the selected launch profile identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionId { get; init; }
    /// <summary>Gets the Minecraft language selected for this launch, without treating it as evidence of a loaded language resource.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameLanguage { get; init; }
    /// <summary>Gets an options-read/write failure that prevented language synchronization without preventing game startup.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameLanguageWarning { get; init; }
    /// <summary>Gets the launcher window mode selected for this session, if one was specified.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Fullscreen { get; init; }
    /// <summary>Gets an options-read/write failure that prevented window-mode synchronization without preventing game startup.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowModeWarning { get; init; }
    /// <summary>Gets the frozen authenticated account identifier, when this launch used one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; init; }
    /// <summary>Gets whether the player explicitly chose a protected cached external session for this attempt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseCachedExternalSession { get; init; }
    /// <summary>Gets a structured preparation error code, preserving authentication recovery through the monitor boundary.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }
    /// <summary>Gets sanitized preparation error details without exposing protected credentials.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? ErrorDetails { get; init; }
    /// <summary>Gets the frozen server target without inferring that the player joined it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Server { get; init; }
    /// <summary>Gets the version-specific connection argument mode used by the launch plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServerConnectionMode { get; init; }
    /// <summary>Gets the major version of the Java runtime actually selected for launch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? JavaMajorVersion { get; init; }
    /// <summary>Gets the UTC session creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; init; }
    /// <summary>Gets the UTC game-exit timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExitedAt { get; init; }
    /// <summary>Gets the game's working directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GameDirectory { get; init; }
    /// <summary>Gets the selected Java executable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaPath { get; init; }
    /// <summary>Gets the recorded game process identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; init; }
    /// <summary>Gets the game process's UTC start identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessStartUtc { get; init; }
    /// <summary>Gets the monitor process identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MonitorId { get; init; }
    /// <summary>Gets the monitor's UTC start identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MonitorStartUtc { get; init; }
    /// <summary>Gets the recorded game exit code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExitCode { get; init; }
    /// <summary>Gets whether incomplete process identity prevents safe liveness inference.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ActivityUnknown { get; init; }
    /// <summary>Gets the failure message, if recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
    /// <summary>Gets string warnings when the stored list is representable without losing unfamiliar values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }
    /// <summary>Gets the path of an unreadable session record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    /// <summary>Gets the independently readable activity sidecar of an unreadable session.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Activity { get; init; }
    /// <summary>Gets an activity-sidecar read failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivityWarning { get; init; }
    /// <summary>Gets the current-user memory-console endpoint while its monitor is alive.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogPipe { get; init; }
    /// <summary>Gets the saved standard-output crash snapshot, absent for normal memory-only capture.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StdoutLog { get; init; }
    /// <summary>Gets the saved standard-error crash snapshot, absent for normal memory-only capture.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StderrLog { get; init; }
    /// <summary>Gets the selected standard-output encoding.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StdoutEncoding { get; init; }
    /// <summary>Gets the selected standard-error encoding.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StderrEncoding { get; init; }
    /// <summary>Gets the selected crash-report file encoding.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileEncoding { get; init; }
    /// <summary>Gets the stored output-capture evidence, including unknown future status fields.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? StdoutLogStatus { get; init; }
    /// <summary>Gets the stored error-capture evidence, including unknown future status fields.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? StderrLogStatus { get; init; }
    /// <summary>Gets an output capture or crash-snapshot persistence failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StdoutLogWarning { get; init; }
    /// <summary>Gets an error capture or crash-snapshot persistence failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StderrLogWarning { get; init; }
    /// <summary>Gets unfamiliar fields and original launch evidence, including explicit nulls.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static SessionStatus FromStored(JsonObject record)
    {
        var fields = (JsonObject)record.DeepClone();
        var activity = fields["activity"] as JsonObject;
        if (activity is not null) fields.Remove("activity");
        var stdoutStatus = fields["stdoutLogStatus"];
        if (stdoutStatus is not null) fields.Remove("stdoutLogStatus");
        var stderrStatus = fields["stderrLogStatus"];
        if (stderrStatus is not null) fields.Remove("stderrLogStatus");
        var errorDetails = fields["errorDetails"];
        if (errorDetails is not null) fields.Remove("errorDetails");
        return new()
        {
            UseCachedExternalSession = StoredReportFields.Value<bool>(fields, "useCachedExternalSession"),
            ErrorCode = StoredReportFields.Text(fields, "errorCode"),
            ErrorDetails = errorDetails,
            Id = StoredReportFields.Text(fields, "id"),
            InstanceId = StoredReportFields.Text(fields, "instanceId"),
            JobId = StoredReportFields.Text(fields, "jobId"),
            Status = StoredReportFields.Text(fields, "status"),
            Readiness = StoredReportFields.Text(fields, "readiness"),
            WindowObservationWarning = StoredReportFields.Text(fields, "windowObservationWarning"),
            VersionId = StoredReportFields.Text(fields, "versionId"),
            GameLanguage = StoredReportFields.Text(fields, "gameLanguage"),
            GameLanguageWarning = StoredReportFields.Text(fields, "gameLanguageWarning"),
            Fullscreen = StoredReportFields.Value<bool>(fields, "fullscreen"),
            WindowModeWarning = StoredReportFields.Text(fields, "windowModeWarning"),
            AccountId = StoredReportFields.Text(fields, "accountId"),
            Server = StoredReportFields.Text(fields, "server"),
            ServerConnectionMode = StoredReportFields.Text(fields, "serverConnectionMode"),
            JavaMajorVersion = StoredReportFields.Value<int>(fields, "javaMajorVersion"),
            CreatedAt = StoredReportFields.Text(fields, "createdAt"),
            ExitedAt = StoredReportFields.Text(fields, "exitedAt"),
            GameDirectory = StoredReportFields.Text(fields, "gameDirectory"),
            JavaPath = StoredReportFields.Text(fields, "javaPath"),
            ProcessId = StoredReportFields.Value<int>(fields, "processId"),
            ProcessStartUtc = StoredReportFields.Text(fields, "processStartUtc"),
            MonitorId = StoredReportFields.Value<int>(fields, "monitorId"),
            MonitorStartUtc = StoredReportFields.Text(fields, "monitorStartUtc"),
            ExitCode = StoredReportFields.Value<int>(fields, "exitCode"),
            ActivityUnknown = StoredReportFields.Value<bool>(fields, "activityUnknown"),
            Error = StoredReportFields.Text(fields, "error"),
            Warnings = StoredReportFields.TextArray(fields, "warnings"),
            Path = StoredReportFields.Text(fields, "path"),
            Activity = activity,
            ActivityWarning = StoredReportFields.Text(fields, "activityWarning"),
            LogPipe = StoredReportFields.Text(fields, "logPipe"),
            StdoutLog = StoredReportFields.Text(fields, "stdoutLog"),
            StderrLog = StoredReportFields.Text(fields, "stderrLog"),
            StdoutEncoding = StoredReportFields.Text(fields, "stdoutEncoding"),
            StderrEncoding = StoredReportFields.Text(fields, "stderrEncoding"),
            FileEncoding = StoredReportFields.Text(fields, "fileEncoding"),
            StdoutLogStatus = stdoutStatus,
            StderrLogStatus = stderrStatus,
            StdoutLogWarning = StoredReportFields.Text(fields, "stdoutLogWarning"),
            StderrLogWarning = StoredReportFields.Text(fields, "stderrLogWarning"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}