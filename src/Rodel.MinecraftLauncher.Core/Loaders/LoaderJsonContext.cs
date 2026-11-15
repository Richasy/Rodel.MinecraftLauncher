// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Loaders;

/// <summary>Provides source-generated serialization metadata for loader catalogs.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(LoaderCatalog))]
[JsonSerializable(typeof(LoaderInstallationResult))]
public sealed partial class LoaderJsonContext : JsonSerializerContext;