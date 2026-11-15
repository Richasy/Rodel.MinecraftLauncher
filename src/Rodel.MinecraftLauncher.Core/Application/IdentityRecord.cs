// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>The selected account and stored account inventory, excluding protected credentials.</summary>
/// <param name="AccountId">The optional globally selected account.</param>
/// <param name="Accounts">Stored identities, including unfamiliar records.</param>
public sealed record IdentitySelection(string? AccountId, IReadOnlyList<IdentityEntry> Accounts);

/// <summary>A stored identity, retaining unfamiliar fields and explicit nulls but not protected credentials.</summary>
public sealed record IdentityRecord
{
    internal IdentityRecord WithStatus(string status)
    {
        var additional = new Dictionary<string, JsonElement>(AdditionalData);
        additional.Remove("status");
        return this with { Status = status, AdditionalData = additional };
    }

    internal void PreserveNull(string field) =>
        AdditionalData[field] = JsonSerializer.SerializeToElement((JsonNode?)null, ApplicationJsonContext.Default.JsonNode);

    /// <summary>Gets the identity identifier when represented by a string.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }
    /// <summary>Gets the player name when represented by a string.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
    /// <summary>Gets the stored identity kind.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }
    /// <summary>Gets the stored player UUID.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uuid { get; init; }
    /// <summary>Gets the persisted authentication state without reading protected credentials.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
    /// <summary>Gets the last successful profile verification timestamp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifiedAt { get; init; }
    /// <summary>Gets the active skin URL returned by the Minecraft profile service.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SkinUrl { get; init; }
    /// <summary>Gets the canonical Yggdrasil API root for an external identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthServer { get; init; }
    /// <summary>Gets the authentication service's display name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthServerName { get; init; }
    /// <summary>Gets password or littleskin, identifying the reauthentication flow.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthMethod { get; init; }
    /// <summary>Gets a hash of the service user identifier, separating users without publishing login credentials.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalUser { get; init; }
    /// <summary>Gets unknown fields and values not losslessly represented by typed properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static IdentityRecord Read(JsonObject record)
    {
        var fields = (JsonObject)record.DeepClone();
        return new()
        {
            Id = StoredReportFields.Text(fields, "id"),
            Name = StoredReportFields.Text(fields, "name"),
            Type = StoredReportFields.Text(fields, "type"),
            Uuid = StoredReportFields.Text(fields, "uuid"),
            Status = StoredReportFields.Text(fields, "status"),
            VerifiedAt = StoredReportFields.Text(fields, "verifiedAt"),
            SkinUrl = StoredReportFields.Text(fields, "skinUrl"),
            AuthServer = StoredReportFields.Text(fields, "authServer"),
            AuthServerName = StoredReportFields.Text(fields, "authServerName"),
            AuthMethod = StoredReportFields.Text(fields, "authMethod"),
            ExternalUser = StoredReportFields.Text(fields, "externalUser"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}

/// <summary>A stored identity array element, preserving non-object records rather than silently discarding them.</summary>
[JsonConverter(typeof(IdentityEntryConverter))]
public sealed record IdentityEntry
{
    /// <summary>Gets the typed object identity, if this entry is an object.</summary>
    public IdentityRecord? Identity { get; init; }
    /// <summary>Gets the original non-object entry, including null.</summary>
    public JsonNode? UnrecognizedRecord { get; init; }

    internal static IdentityEntry Read(JsonNode? value) => value is JsonObject record
        ? new() { Identity = IdentityRecord.Read(record) }
        : new() { UnrecognizedRecord = value?.DeepClone() };
}

internal sealed class IdentityEntryConverter : JsonConverter<IdentityEntry>
{
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IdentityEntry value, JsonSerializerOptions options)
    {
        if (value.Identity is { } identity)
            JsonSerializer.Serialize(writer, identity, ApplicationJsonContext.Default.IdentityRecord);
        else if (value.UnrecognizedRecord is { } unknown)
            unknown.WriteTo(writer, options);
        else
            writer.WriteNullValue();
    }

    /// <inheritdoc/>
    public override IdentityEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        IdentityEntry.Read(JsonNode.Parse(ref reader));
}