// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;
namespace Rodel.Minecraft.Seeds;

/// <summary>Provides NativeAOT metadata for original generation requests, samples and native registries.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SeedMapView))]
[JsonSerializable(typeof(SeedRuntimeProfile))]
[JsonSerializable(typeof(SeedMapQuery))]
[JsonSerializable(typeof(SeedMapTile))]
[JsonSerializable(typeof(SeedSpawnQuery))]
[JsonSerializable(typeof(SeedSpawnResult))]
[JsonSerializable(typeof(SeedStructureQuery))]
[JsonSerializable(typeof(SeedStructureResult))]
[JsonSerializable(typeof(SeedRegionQuery))]
[JsonSerializable(typeof(SeedLootQuery))]
[JsonSerializable(typeof(SeedContainer))]
[JsonSerializable(typeof(SeedRegionResult))]
[JsonSerializable(typeof(SeedRegionProgress))]
[JsonSerializable(typeof(SeedSearchQuery))]
[JsonSerializable(typeof(SeedSearchResult))]
[JsonSerializable(typeof(SeedCatalog))]
[JsonSerializable(typeof(SeedVersionOption[]))]
public sealed partial class SeedJsonContext : JsonSerializerContext;