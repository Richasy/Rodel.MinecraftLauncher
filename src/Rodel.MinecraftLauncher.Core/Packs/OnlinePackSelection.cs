// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>An exact resolved publisher artifact, independent of downloading or applying it.</summary>
public record OnlinePackSelection : PackResult
{
    /// <summary>Gets the publisher platform.</summary>
    public string Platform { get; init; } = "modrinth";
    /// <summary>Gets the canonical project identifier.</summary>
    public required string ProjectId { get; init; }
    /// <summary>Gets the exact selected release identifier.</summary>
    public required string VersionId { get; init; }
    /// <summary>Gets the publisher's original release display value.</summary>
    public JsonNode? VersionName { get; init; }
    /// <summary>Gets the publisher's original release-channel value.</summary>
    public JsonNode? VersionType { get; init; }
    /// <summary>Gets or sets the requested channel policy.</summary>
    public string? Channel { get; set; }
    /// <summary>Gets the original project title value.</summary>
    public JsonNode? Name { get; init; }
    /// <summary>Gets the selected file reader, retaining the complete provider record.</summary>
    public required OnlinePackFile File { get; init; }
    /// <summary>Gets the provider's original game-version declarations.</summary>
    public JsonNode? GameVersions { get; init; }
    /// <summary>Gets the provider's original loader declarations.</summary>
    public JsonNode? Loaders { get; init; }
    /// <summary>Gets the launcher-owned artifact cache path.</summary>
    public required string ArchivePath { get; init; }
    /// <summary>Gets or sets explicit release selection notices.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PackSelectionWarning>? SelectionWarnings { get; set; }
    /// <summary>Gets or sets unknown fields restored from an existing cached selection.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

/// <summary>Reads a provider artifact record only where acquisition needs a known field.</summary>
[JsonConverter(typeof(OnlinePackFileConverter))]
public sealed class OnlinePackFile
{
    private readonly JsonObject _metadata;

    internal OnlinePackFile(JsonObject metadata) => _metadata = (JsonObject)metadata.DeepClone();
    /// <summary>Gets the provider's artifact URL.</summary>
    public string Url => _metadata["url"]!.GetValue<string>();
    /// <summary>Gets the declared byte size.</summary>
    public long? Size => _metadata["size"]?.GetValue<long>();
    /// <summary>Gets the declared SHA-1, if available.</summary>
    public string? Sha1 => _metadata["hashes"]?["sha1"]?.GetValue<string>();
    /// <summary>Gets the declared SHA-512, if available.</summary>
    public string? Sha512 => _metadata["hashes"]?["sha512"]?.GetValue<string>();
    /// <summary>Gets a copy of the complete unmodified provider file record.</summary>
    public JsonObject RawMetadata => (JsonObject)_metadata.DeepClone();
}

internal sealed class OnlinePackFileConverter : JsonConverter<OnlinePackFile>
{
    /// <inheritdoc/>
    public override OnlinePackFile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException("An online pack file must be an object."));
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, OnlinePackFile value, JsonSerializerOptions options) => value.RawMetadata.WriteTo(writer, options);
}

/// <summary>Explains an explicit release which overrides a requested channel policy.</summary>
/// <param name="Code">The stable selection warning code.</param>
/// <param name="VersionId">The explicitly selected release.</param>
/// <param name="VersionType">Its declared release channel.</param>
/// <param name="Channel">The requested channel policy.</param>
public sealed record PackSelectionWarning(string Code, string VersionId, string VersionType, string? Channel);

/// <summary>Reads one open Modrinth release record without making unknown fields part of the launcher's own contract.</summary>
[JsonConverter(typeof(OnlinePackVersionConverter))]
public sealed class OnlinePackVersion
{
    private readonly JsonObject _metadata;

    internal OnlinePackVersion(JsonObject metadata) => _metadata = (JsonObject)metadata.DeepClone();
    /// <summary>Gets the exact provider release identifier.</summary>
    public string Id => _metadata["id"]!.GetValue<string>();
    /// <summary>Gets the display value used for native release selection.</summary>
    public string DisplayName => _metadata["version_number"]?.ToString() ?? _metadata["name"]?.ToString() ?? Id;
    /// <summary>Gets the provider release channel without inventing an unknown channel policy.</summary>
    public string? VersionType => _metadata["version_type"]?.ToString();
    /// <summary>Gets the original publication timestamp used for ordering.</summary>
    public string? PublishedAt => _metadata["date_published"]?.ToString();
    /// <summary>Gets the original release-name value for selection provenance.</summary>
    public JsonNode? VersionName => (_metadata["version_number"] ?? _metadata["name"])?.DeepClone() ?? JsonValue.Create(Id);
    /// <summary>Gets the original channel value for selection provenance.</summary>
    public JsonNode? VersionTypeMetadata => _metadata["version_type"]?.DeepClone();
    /// <summary>Gets the original game-version declarations.</summary>
    public JsonNode? GameVersions => _metadata["game_versions"]?.DeepClone();
    /// <summary>Gets the original loader declarations.</summary>
    public JsonNode? Loaders => _metadata["loaders"]?.DeepClone();
    /// <summary>Gets a copy of the complete provider response record.</summary>
    public JsonObject RawMetadata => (JsonObject)_metadata.DeepClone();

    internal OnlinePackFile SelectArchive()
    {
        var file = (_metadata["files"] as JsonArray ?? []).OfType<JsonObject>()
            .OrderByDescending(value => value["primary"]?.GetValue<bool>() == true)
            .FirstOrDefault(value => value["filename"]?.GetValue<string>().EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidDataException("Pack version has no mrpack file.");
        return new(file);
    }
}

internal sealed class OnlinePackVersionConverter : JsonConverter<OnlinePackVersion>
{
    /// <inheritdoc/>
    public override OnlinePackVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException("An online pack version must be an object."));
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, OnlinePackVersion value, JsonSerializerOptions options) => value.RawMetadata.WriteTo(writer, options);
}