// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Runtime;

/// <summary>The fixed selection descriptor committed by runtime preparation, retaining open version declarations.</summary>
/// <param name="MinecraftVersion">Resolved base version.</param>
/// <param name="Loader">Recognized loader family.</param>
/// <param name="Source">Descriptor source.</param>
/// <param name="VersionId">Selected launch profile.</param>
/// <param name="LoaderVersion">Recognized loader release.</param>
/// <param name="MainClass">Original main-class declaration.</param>
/// <param name="JavaVersion">Original Java requirement declaration.</param>
public sealed record RuntimeTarget(string? MinecraftVersion, string? Loader, string Source, string? VersionId,
    string? LoaderVersion, JsonNode? MainClass, JsonNode? JavaVersion)
{
    internal static RuntimeTarget From(RuntimeDescription value) =>
        new(value.MinecraftVersion, value.Loader, value.Source, value.VersionId, value.LoaderVersion, value.MainClass, value.JavaVersion);
}

internal sealed record RuntimeCommitResult(string OperationId, RuntimeTarget Target, IReadOnlyList<string> Warnings);

/// <summary>A runtime change plan or its committed outcome.</summary>
public sealed record RuntimeChangeResult
{
    /// <summary>Gets the target instance.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets the previously selected launch profile.</summary>
    public required string PreviousVersion { get; init; }
    /// <summary>Gets the requested base game version.</summary>
    public required string MinecraftVersion { get; init; }
    /// <summary>Gets the requested loader, or null for vanilla.</summary>
    public string? Loader { get; init; }
    /// <summary>Gets the requested loader release.</summary>
    public string? LoaderVersion { get; init; }
    /// <summary>Gets the planned or prepared profile identifier.</summary>
    public string? TargetVersion { get; internal set; }
    /// <summary>Gets planned or applied without inferring game readiness.</summary>
    public string State { get; internal set; } = "planned";
    /// <summary>Gets retained compatibility and commit warnings.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
    /// <summary>Gets the committed operation identifier, absent for a plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationId { get; internal set; }
    /// <summary>Gets the actual committed target, absent for a plan.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeTarget? Target { get; internal set; }
}

/// <summary>Reports dependency preparation and its actual committed selection.</summary>
/// <param name="Prepared">Whether the declared preparation completed under its existing compatibility policy.</param>
/// <param name="VersionId">Committed profile identifier.</param>
/// <param name="Warnings">Unrecognized dependencies and commit warnings.</param>
/// <param name="OperationId">Selection operation identifier.</param>
/// <param name="Target">Committed selection descriptor.</param>
public sealed record RuntimePreparationResult(bool Prepared, string VersionId, IReadOnlyList<string> Warnings, string OperationId, RuntimeTarget Target)
{
    /// <summary>Gets the migrated instance when preparation originated from a directory import.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; internal set; }
    /// <summary>Gets whether dependencies came from a pack or migrated source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectionSource { get; internal set; }
}

/// <summary>A returned selection journal; extensible settings and historical target data remain unchanged.</summary>
public sealed record RuntimeRollbackResult
{
    /// <summary>Gets the stored operation identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the recorded owning instance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the original extensible settings snapshot.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? PreviousSettings { get; init; }
    /// <summary>Gets the operation's extensible target settings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? TargetSettings { get; init; }
    /// <summary>Gets the original persisted target descriptor, including unknown historical fields.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Target { get; init; }
    /// <summary>Gets the original selection source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
    /// <summary>Gets the stored completion state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }
    /// <summary>Gets the original creation timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; init; }
    /// <summary>Gets remaining original fields and values not representable by the fixed properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static RuntimeRollbackResult Read(JsonObject source)
    {
        var fields = source.DeepClone().AsObject();
        JsonObject? Object(string key)
        {
            if (fields[key] is not JsonObject value) return null;
            fields.Remove(key);
            return value;
        }

        return new()
        {
            Id = StoredReportFields.Text(fields, "id"),
            InstanceId = StoredReportFields.Text(fields, "instanceId"),
            PreviousSettings = Object("previousSettings"),
            TargetSettings = Object("targetSettings"),
            Target = Object("target"),
            Source = StoredReportFields.Text(fields, "source"),
            State = StoredReportFields.Text(fields, "state"),
            CreatedAt = StoredReportFields.Text(fields, "createdAt"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}