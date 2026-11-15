// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;
namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Provides NativeAOT metadata for Core-owned search, bookmark and export records.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SeedSearchState))]
[JsonSerializable(typeof(SeedSearchState[]))]
[JsonSerializable(typeof(SeedBookmark))]
[JsonSerializable(typeof(SeedBookmark[]))]
[JsonSerializable(typeof(SeedMapExport))]
public sealed partial class SeedStoreJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SeedWorkerRequest))]
[JsonSerializable(typeof(SeedWorkerEnvelope))]
[JsonSerializable(typeof(SeedWorkerReady))]
[JsonSerializable(typeof(SeedValue))]
[JsonSerializable(typeof(SeedGeneratedWorld))]
[JsonSerializable(typeof(SeedRegionTints))]
[JsonSerializable(typeof(SeedSurfaceMap))]
internal sealed partial class SeedWorkerJsonContext : JsonSerializerContext;