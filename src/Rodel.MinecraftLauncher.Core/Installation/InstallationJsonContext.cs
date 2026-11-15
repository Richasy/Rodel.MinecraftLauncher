// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Installation;

/// <summary>Provides source-generated serialization metadata for installation catalog results.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(VersionCatalog))]
[JsonSerializable(typeof(InstallationResult))]
[JsonSerializable(typeof(IInstallationResult))]
public sealed partial class InstallationJsonContext : JsonSerializerContext;