// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>A typed Core read result, converted to the model's tool protocol only at the runner boundary.</summary>
/// <param name="Data">The concrete Core read report, never a JSON replacement for a fixed result model.</param>
internal sealed record AiSuccessfulToolResult<T>(T Data)
{
    /// <summary>Gets successful completion of this bounded read.</summary>
    public string Status { get; } = "succeeded";
    /// <summary>Gets original inline image blocks when reading archived media.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Assistant.AiMcpContent[]? ImageContent { get; init; }
}

/// <summary>A tool receipt whose returned data was withheld after permission changed.</summary>
/// <param name="Error">The original permission failure.</param>
/// <param name="Data">The explicit remaining-effect uncertainty.</param>
internal sealed record AiWithheldToolResult(LauncherOperationError Error, AiWithheldToolData Data)
{
    /// <summary>Gets failure to return data, not proof that an already dispatched effect did not occur.</summary>
    public string Status { get; } = "failed";
}

/// <summary>Explains withholding without granting permission to replay a dispatched operation.</summary>
/// <param name="EffectsMayHaveOccurred">Whether a durable mutation intent exists.</param>
/// <param name="InvocationId">The exact invocation to inspect.</param>
internal sealed record AiWithheldToolData(bool EffectsMayHaveOccurred, string InvocationId)
{
    /// <summary>Gets whether returned source data was withheld.</summary>
    public bool ReturnedDataWithheld { get; } = true;
}

/// <summary>Actual source-file and guarded-commit observations attached to an open MCP result.</summary>
internal sealed record AiFileObservation
{
    /// <summary>Gets the original selected root identifier value, or explicit null for one-call path access.</summary>
    public JsonNode? RootId { get; init; }
    /// <summary>Gets the original scoped path value presented for this tool call.</summary>
    public JsonNode? Path { get; init; }
    /// <summary>Gets or sets the actually observed source-byte hash.</summary>
    public string? Sha256 { get; set; }
    /// <summary>Gets the complete original source-byte length.</summary>
    public int? OriginalBytes { get; init; }
    /// <summary>Gets the original byte-order-mark length.</summary>
    public int? ByteOrderMarkBytes { get; init; }
    /// <summary>Gets the actual strict source encoding.</summary>
    public string? Encoding { get; init; }
    /// <summary>Gets whether the snapshot was redacted and therefore cannot reconstruct private source bytes.</summary>
    public bool Redacted { get; init; }
    /// <summary>Gets whether the external program received a snapshot rather than the player file.</summary>
    public bool Snapshot { get; init; } = true;
    /// <summary>Gets the tool's uncoerced requested offset.</summary>
    public JsonNode? Offset { get; init; }
    /// <summary>Gets the actual bounded native read limit.</summary>
    public JsonNode? Limit { get; init; }
    /// <summary>Gets source-specific snapshot warnings.</summary>
    public JsonArray Warnings { get; init; } = [];
    /// <summary>Gets or sets whether Core actually committed source bytes.</summary>
    public bool PlayerFileModified { get; set; }
    /// <summary>Gets or sets the source-byte length read back after commit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentBytes { get; set; }
    /// <summary>Gets or sets the actual post-commit byte-order-mark length.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentByteOrderMarkBytes { get; set; }
    /// <summary>Gets or sets the exact observed desired-state comparison after commit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DesiredStateObserved { get; set; }
    /// <summary>Gets or sets the source backup reference; explicit null is retained for newly created files.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? BackupPath { get; set; }
}

/// <summary>Fixed provenance and native byte-truncation semantics of one actual public search response.</summary>
/// <param name="Query">The validated decoded query.</param>
/// <param name="ObservedUtc">The retrieval time, not source publication time.</param>
/// <param name="Truncated">Whether the native response included its truncation marker.</param>
/// <param name="RequestedMaximumBytes">The actual native UTF-8 byte budget.</param>
/// <param name="TransportFailures">The proxy's actual failures.</param>
internal sealed record AiSearchObservation(string Query, string ObservedUtc, bool Truncated, int RequestedMaximumBytes, string[] TransportFailures)
{
    /// <summary>Gets the actual public search provider.</summary>
    public string Provider { get; } = "Mwmbl";
    /// <summary>Gets the public API source kind.</summary>
    public string Source { get; } = "public_api_v2";
    /// <summary>Gets whether a player-owned search key was required.</summary>
    public bool UserSearchKeyRequired { get; }
    /// <summary>Gets the native truncation unit, independent of character counts.</summary>
    public string TruncationUnit { get; } = "native_utf8_bytes";
    /// <summary>Gets whether the response is a server-side page; always false.</summary>
    public bool ServerPagination { get; }
}

/// <summary>Actual managed web retrieval provenance, not an additional network grant.</summary>
/// <param name="ActualName">The actual native tool invoked.</param>
/// <param name="ObservedUtc">The retrieval timestamp.</param>
/// <param name="TransportFailures">The proxy's actual failures.</param>
internal sealed record AiWebObservation(string ActualName, string ObservedUtc, string[] TransportFailures)
{
    /// <summary>Gets the pinned managed component identity.</summary>
    public string SourceId { get; } = "rodel_web";
    /// <summary>Gets the public DNS source actually used.</summary>
    public string PublicDnsProvider { get; } = "Cloudflare DNS";
}

/// <summary>The bounded observation of an independent job, not a claim that the user goal completed.</summary>
internal sealed record AiJobObservation
{
    /// <summary>Gets the actual independent job identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the observed job lifecycle status.</summary>
    public string? Status { get; init; }
    /// <summary>Gets whether the owner cannot be observed safely; such a job is returned for inspection instead of awaited indefinitely.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ActivityUnknown { get; init; }
    /// <summary>Gets the reason an owner's identity could not be observed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerWarning { get; init; }
    /// <summary>Gets the original request's instance reference without coercing unknown request data.</summary>
    public JsonNode? InstanceId { get; init; }
    /// <summary>Gets whether a durable job result exists.</summary>
    public bool ResultAvailable { get; init; }
    /// <summary>Gets the exact read-only continuation for full job evidence.</summary>
    public required AiJobReadContinuation Read { get; init; }
    /// <summary>Gets bounded typed progress, including future fields retained by the progress model.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(AiObservedProgressConverter))]
    public LauncherProgress? Progress { get; init; }
    /// <summary>Gets bounded original error data without converting noncanonical persisted values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Error { get; init; }
    /// <summary>Gets whether oversized progress is available through the continuation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProgressAvailableThroughJobShow { get; init; }
    /// <summary>Gets whether oversized error evidence is available through the continuation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ErrorAvailableThroughJobShow { get; init; }
    /// <summary>Gets bounded non-object progress retained from malformed or future job records without coercion.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>The exact independent-job observation route.</summary>
/// <param name="Arguments">The bound job identifier.</param>
internal sealed record AiJobReadContinuation(AiJobReadArguments Arguments)
{
    /// <summary>Gets the read-only launcher tool name.</summary>
    public string Tool { get; } = "job_show";
}

/// <summary>A job-read continuation cannot change the job or its ownership.</summary>
/// <param name="JobId">The observed independent job identifier.</param>
internal sealed record AiJobReadArguments(string JobId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AiFileObservation))]
[JsonSerializable(typeof(AiSearchObservation))]
[JsonSerializable(typeof(AiWebObservation))]
[JsonSerializable(typeof(AiJobObservation))]
[JsonSerializable(typeof(AiUserAnswerResult))]
[JsonSerializable(typeof(AiWithheldToolResult))]
[JsonSerializable(typeof(AiSuccessfulToolResult<AiSkillReport>), TypeInfoPropertyName = "SkillResult")]
[JsonSerializable(typeof(AiSuccessfulToolResult<AiSkillReference>), TypeInfoPropertyName = "SkillReferenceResult")]
[JsonSerializable(typeof(AiSuccessfulToolResult<AiContextPage>), TypeInfoPropertyName = "ContextResult")]
[JsonSerializable(typeof(AiSuccessfulToolResult<AiChatRecord>), TypeInfoPropertyName = "EffectsResult")]
internal sealed partial class AiToolObservationJsonContext : JsonSerializerContext;

/// <summary>Preserves the application's phase-specific progress metadata inside an AI observation.</summary>
internal sealed class AiObservedProgressConverter : JsonConverter<LauncherProgress>
{
    /// <inheritdoc />
    public override LauncherProgress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => LauncherProgress.Read(JsonNode.Parse(ref reader) as JsonObject
            ?? throw new JsonException("A launcher progress snapshot must be an object."));

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LauncherProgress value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, ApplicationJsonContext.Progress);
}