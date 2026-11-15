// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>Reads a pack publisher's file declaration without discarding unknown fields or changing its environment policy.</summary>
[JsonConverter(typeof(PackFileDeclarationConverter))]
public sealed class PackFileDeclaration
{
    private readonly JsonObject _metadata;
    private readonly string? _hmclSha1;
    private readonly string? _hmclDownload;

    internal PackFileDeclaration(JsonObject metadata, string? hmclSha1 = null, string? hmclDownload = null)
    {
        _metadata = (JsonObject)metadata.DeepClone();
        _hmclSha1 = hmclSha1;
        _hmclDownload = hmclDownload;
    }

    /// <summary>Gets the publisher's relative destination, when this is not a CurseForge project/file reference.</summary>
    public string? Path => _metadata["path"]?.GetValue<string>();
    /// <summary>Gets the declared client requirement without interpreting unknown environment fields.</summary>
    public string? ClientEnvironment => _metadata["env"]?["client"]?.GetValue<string>();
    /// <summary>Gets the declared verification size, including an explicit zero.</summary>
    public long? FileSize => _metadata["fileSize"]?.GetValue<long>();
    /// <summary>Gets the normalized HMCL hash or the publisher's SHA-1 declaration.</summary>
    public string? Sha1 => _hmclSha1 ?? _metadata["hashes"]?["sha1"]?.GetValue<string>();
    /// <summary>Gets the publisher's optional SHA-512 declaration.</summary>
    public string? Sha512 => _hmclSha1 is null ? _metadata["hashes"]?["sha512"]?.GetValue<string>() : null;
    /// <summary>Gets the declared CurseForge project identifier.</summary>
    public long ProjectId => _metadata["projectID"]!.GetValue<long>();
    /// <summary>Gets the declared CurseForge file identifier.</summary>
    public long FileId => _metadata["fileID"]!.GetValue<long>();
    /// <summary>Gets permitted acquisition candidates; an unavailable HMCL file API produces no network candidates.</summary>
    public IEnumerable<string> Downloads => _hmclSha1 is not null
        ? _hmclDownload is null ? [] : [_hmclDownload]
        : (_metadata["downloads"] as JsonArray ?? []).Select(value => value!.GetValue<string>());
    /// <summary>Gets a copy of the original third-party declaration, including unknown fields and explicit nulls.</summary>
    public JsonObject RawMetadata => (JsonObject)_metadata.DeepClone();

    internal JsonObject ToMetadata()
    {
        var metadata = RawMetadata;
        if (_hmclSha1 is not null)
        {
            metadata["hashes"] = new JsonObject { ["sha1"] = _hmclSha1 };
            metadata["downloads"] = _hmclDownload is null ? [] : new JsonArray(JsonValue.Create(_hmclDownload));
        }

        return metadata;
    }
}

internal sealed class PackFileDeclarationConverter : JsonConverter<PackFileDeclaration>
{
    /// <inheritdoc/>
    public override PackFileDeclaration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException("Pack file declaration must be an object."));

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PackFileDeclaration value, JsonSerializerOptions options) =>
        value.ToMetadata().WriteTo(writer, options);
}