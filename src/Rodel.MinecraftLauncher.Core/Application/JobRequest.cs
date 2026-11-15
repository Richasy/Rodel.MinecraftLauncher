// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>The persisted task envelope; its parameters belong to the selected typed-task or CLI executor.</summary>
public sealed record JobRequest
{
    /// <summary>Gets the executor operation without interpreting its parameters as CLI tokens.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }
    /// <summary>Gets the executor-owned serialized request, validated by that executor before dispatch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Parameters { get; init; }
    /// <summary>Gets unknown fields and malformed or explicitly null persisted values.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static JobRequest Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        var parameters = fields["parameters"] as JsonObject;
        if (parameters is not null) fields.Remove("parameters");
        return new()
        {
            Operation = StoredReportFields.Text(fields, "operation"),
            Parameters = parameters,
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}