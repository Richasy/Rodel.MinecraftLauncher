// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Reports a registered Java resource and its latest probe while retaining unfamiliar registration metadata.</summary>
public sealed record JavaRuntimeSummary
{
    /// <summary>Gets the registration directory's validated identity.</summary>
    public required string Id { get; init; }

    /// <summary>Gets whether the registered executable is currently usable.</summary>
    public bool IsUsable { get; init; }

    /// <summary>Gets whether the launcher owns the runtime bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Managed { get; init; }

    /// <summary>Gets the resolved executable path.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaPath { get; init; }

    /// <summary>Gets the resolved owned runtime directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeDirectory { get; init; }

    /// <summary>Gets the registered import source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>Gets the Mojang component identifier for an installed runtime.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Component { get; init; }

    /// <summary>Gets the provider platform identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Platform { get; init; }

    /// <summary>Gets the major version observed by the current probe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MajorVersion { get; init; }

    /// <summary>Gets the complete observed Java version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FullVersion { get; init; }

    /// <summary>Gets the original process version output for diagnostics.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionOutput { get; init; }

    /// <summary>Gets current ancillary probe warnings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Warnings { get; init; }

    /// <summary>Gets an independently reported registration-read failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; init; }

    /// <summary>Gets unknown fields, noncanonical values and explicit nulls from the original record.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static JavaRuntimeSummary Read(JsonObject record)
    {
        var fields = (JsonObject)record.DeepClone();
        var id = StoredReportFields.Text(fields, "id")!;
        var usable = StoredReportFields.Value<bool>(fields, "isUsable")!.Value;
        string[]? warnings = null;
        if (fields["warnings"] is JsonArray array && array.All(item => item is JsonValue value && value.TryGetValue<string>(out _)))
        {
            warnings = [.. array.Select(item => item!.GetValue<string>())];
            fields.Remove("warnings");
        }

        return new()
        {
            Id = id,
            IsUsable = usable,
            Managed = StoredReportFields.Value<bool>(fields, "managed"),
            JavaPath = StoredReportFields.Text(fields, "javaPath"),
            RuntimeDirectory = StoredReportFields.Text(fields, "runtimeDirectory"),
            Source = StoredReportFields.Text(fields, "source"),
            Component = StoredReportFields.Text(fields, "component"),
            Platform = StoredReportFields.Text(fields, "platform"),
            MajorVersion = StoredReportFields.Value<int>(fields, "majorVersion"),
            FullVersion = StoredReportFields.Text(fields, "fullVersion"),
            VersionOutput = StoredReportFields.Text(fields, "versionOutput"),
            Warnings = warnings,
            Warning = StoredReportFields.Text(fields, "warning"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}