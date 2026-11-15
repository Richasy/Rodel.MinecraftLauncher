// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Servers;

/// <summary>A stored server favorite retaining unfamiliar fields and explicit nulls.</summary>
public sealed record ServerFavorite
{
    /// <summary>Gets the favorite identifier, when stored as a string.</summary>
    public string? Id { get; init; }
    /// <summary>Gets the display name, when stored as a string.</summary>
    public string? Name { get; init; }
    /// <summary>Gets the server address, when stored as a string.</summary>
    public string? Address { get; init; }
    /// <summary>Gets associated client instance identifiers when the saved array contains only strings.</summary>
    public IReadOnlyList<string>? InstanceIds { get; init; }
    /// <summary>Gets the preferred existing client for this favorite.</summary>
    public string? PreferredInstanceId { get; init; }
    /// <summary>Gets an explicitly saved account preference, if any.</summary>
    public string? AccountId { get; init; }
    /// <summary>Gets original fields not represented losslessly by the typed properties.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];

    internal static ServerFavorite Read(JsonObject source)
    {
        var fields = (JsonObject)source.DeepClone();
        return new()
        {
            Id = StoredReportFields.Text(fields, "id"),
            Name = StoredReportFields.Text(fields, "name"),
            Address = StoredReportFields.Text(fields, "address"),
            InstanceIds = StoredReportFields.TextArray(fields, "instanceIds"),
            PreferredInstanceId = StoredReportFields.Text(fields, "preferredInstanceId"),
            AccountId = StoredReportFields.Text(fields, "accountId"),
            AdditionalData = StoredReportFields.Remaining(fields)
        };
    }
}

/// <summary>A favorite array element, including an unrecognized persisted scalar, array or null.</summary>
[JsonConverter(typeof(ServerFavoriteEntryConverter))]
public sealed record ServerFavoriteEntry
{
    /// <summary>Gets the typed favorite for an object record.</summary>
    public ServerFavorite? Favorite { get; init; }
    /// <summary>Gets an unrecognized non-object record without dropping it from the saved list.</summary>
    public JsonNode? UnrecognizedRecord { get; init; }

    internal static ServerFavoriteEntry Read(JsonNode? value) => value is JsonObject record
        ? new() { Favorite = ServerFavorite.Read(record) }
        : new() { UnrecognizedRecord = value?.DeepClone() };
}

internal sealed class ServerFavoriteEntryConverter : JsonConverter<ServerFavoriteEntry>
{
    /// <inheritdoc/>
    public override bool HandleNull => true;

    /// <inheritdoc/>
    public override ServerFavoriteEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ServerFavoriteEntry.Read(JsonNode.Parse(ref reader));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ServerFavoriteEntry value, JsonSerializerOptions options)
    {
        if (value.Favorite is { } favorite)
            JsonSerializer.Serialize(writer, favorite, ServerJsonContext.Default.ServerFavorite);
        else if (value.UnrecognizedRecord is { } unknown)
            unknown.WriteTo(writer, options);
        else
            writer.WriteNullValue();
    }
}