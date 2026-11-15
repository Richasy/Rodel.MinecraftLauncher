// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Safe launcher workflow evidence with original unfamiliar persisted fields retained.</summary>
public sealed record LauncherActivityEntry
{
    /// <summary>Gets the recorded UTC timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Time { get; init; }
    /// <summary>Gets the safe event identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; init; }
    /// <summary>Gets the event severity classification.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Level { get; init; }
    /// <summary>Gets the associated instance identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the associated task identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JobId { get; init; }
    /// <summary>Gets the safe task-stage identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stage { get; init; }
    /// <summary>Gets the safe diagnostic code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }
    /// <summary>Gets an operation-specific item count.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; init; }
    /// <summary>Gets an operation-specific byte count.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Bytes { get; init; }
    /// <summary>Gets the native error code without an exception message.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NativeError { get; init; }
    /// <summary>Gets an HTTP status without a private source URL.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HttpStatus { get; init; }
    /// <summary>Gets unknown persisted values, malformed known values and explicit null fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static LauncherActivityEntry Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        return new()
        {
            Time = StoredReportFields.Text(fields, "time"),
            Event = StoredReportFields.Text(fields, "event"),
            Level = StoredReportFields.Text(fields, "level"),
            InstanceId = StoredReportFields.Text(fields, "instanceId"),
            JobId = StoredReportFields.Text(fields, "jobId"),
            Stage = StoredReportFields.Text(fields, "stage"),
            Code = StoredReportFields.Text(fields, "code"),
            Count = StoredReportFields.Value<int>(fields, "count"),
            Bytes = StoredReportFields.Value<long>(fields, "bytes"),
            NativeError = StoredReportFields.Value<int>(fields, "nativeError"),
            HttpStatus = StoredReportFields.Value<int>(fields, "httpStatus"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}