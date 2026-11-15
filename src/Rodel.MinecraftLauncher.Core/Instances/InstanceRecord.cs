// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

public sealed class InstanceRecord
{
    private readonly JsonObject _metadata;
    internal InstanceRecord(JsonObject metadata, string metadataPath)
    {
        _metadata = metadata;
        MetadataPath = metadataPath;
    }

    public string Id => Required("id");
    public string Name => Required("name");
    public string GameDirectory => Resolve(Required("gameDirectory"));
    public string MinecraftDirectory => _metadata["minecraftDirectory"]?.GetValue<string>() is { } path ? Resolve(path) : GameDirectory;
    public string? VersionId => _metadata["versionId"]?.GetValue<string>();
    public string? JavaPath => _metadata["javaPath"]?.GetValue<string>();
    public bool Managed => _metadata["managed"]?.GetValue<bool>() ?? false;
    /// <summary>The owning server favorite for a dedicated client, or null for an ordinary local instance.</summary>
    public string? ServerId => _metadata["serverId"]?.GetValue<string>();
    public string MetadataPath { get; }

    internal JsonObject ToMetadata() => (JsonObject)_metadata.DeepClone();
    /// <summary>Reads resolved registration fields while retaining unknown metadata and explicit nulls.</summary>
    public InstanceSummary Describe()
    {
        var additional = ToMetadata();
        var versionId = StoredReportFields.Text(additional, "versionId");
        var javaPath = StoredReportFields.Text(additional, "javaPath");
        var serverId = StoredReportFields.Text(additional, "serverId");
        var managed = StoredReportFields.Value<bool>(additional, "managed");
        foreach (var key in new[] { "id", "name", "gameDirectory", "minecraftDirectory", "icon" })
        {
            if (additional[key] is not null) additional.Remove(key);
        }

        additional.Remove("gameDirectory");
        additional.Remove("minecraftDirectory");
        return new()
        {
            Id = Id,
            Name = Name,
            GameDirectory = GameDirectory,
            MinecraftDirectory = MinecraftDirectory,
            VersionId = versionId,
            JavaPath = javaPath,
            ServerId = serverId,
            Managed = managed,
            Icon = _metadata["icon"]?.DeepClone(),
            AdditionalData = StoredReportFields.Remaining(additional)
        };
    }
    public JsonObject ToJson()
    {
        var result = ToMetadata();
        result["gameDirectory"] = GameDirectory;
        result["minecraftDirectory"] = MinecraftDirectory;
        return result;
    }
    private string Resolve(string path) => Path.GetFullPath(path, Path.GetDirectoryName(MetadataPath)!);
    private string Required(string key) => _metadata[key]?.GetValue<string>() ?? throw new InvalidDataException($"Instance metadata is missing {key}: {MetadataPath}");
}