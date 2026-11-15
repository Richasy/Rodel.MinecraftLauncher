// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>Describes a validated pack archive while retaining its original manifest and file declarations.</summary>
public record PackInspection
{
    /// <summary>Gets the recognized archive format, mrpack, curseforge or hmcl.</summary>
    public required string Format { get; init; }
    /// <summary>Gets the original manifest name value, including null or non-string metadata.</summary>
    public JsonNode? Name { get; init; }
    /// <summary>Gets the original version identifier value.</summary>
    public JsonNode? VersionId { get; init; }
    /// <summary>Gets the required Minecraft version declared by the pack.</summary>
    public required string MinecraftVersion { get; init; }
    /// <summary>Gets the dependency map, including unrecognized dependency keys and values.</summary>
    public required IReadOnlyDictionary<string, JsonNode?> Dependencies { get; init; }
    /// <summary>Gets the original declarations for optional client files.</summary>
    public required IReadOnlyList<PackFileDeclaration> OptionalFiles { get; init; }
    /// <summary>Gets the complete original pack manifest.</summary>
    public required JsonObject Manifest { get; init; }
    /// <summary>Gets the normalized archive path.</summary>
    public required string ArchivePath { get; init; }
}

/// <summary>Provides source-generated serialization metadata for pack inspection results.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PackInspection))]
[JsonSerializable(typeof(PackManualFile))]
[JsonSerializable(typeof(PackContentState))]
[JsonSerializable(typeof(PackState))]
[JsonSerializable(typeof(PackOperationList))]
[JsonSerializable(typeof(PackExportResult))]
[JsonSerializable(typeof(PackOptionalState))]
[JsonSerializable(typeof(OnlinePackSelection))]
[JsonSerializable(typeof(PackReview))]
[JsonSerializable(typeof(PackFileRecord))]
[JsonSerializable(typeof(IReadOnlyList<PackFileRecord>))]
[JsonSerializable(typeof(PackResult))]
[JsonSerializable(typeof(PackPreparation))]
[JsonSerializable(typeof(PackAcquisition))]
[JsonSerializable(typeof(PackJavaPreparation))]
[JsonSerializable(typeof(OnlinePackPlan))]
[JsonSerializable(typeof(IReadOnlyList<OnlinePackVersion>))]
public sealed partial class PackJsonContext : JsonSerializerContext;