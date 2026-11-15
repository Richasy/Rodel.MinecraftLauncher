// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>Provides source-generated metadata for launcher and game-configuration reports.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(EffectiveSettings))]
[JsonSerializable(typeof(SettingValue))]
[JsonSerializable(typeof(GameSettingsSnapshot))]
[JsonSerializable(typeof(GameSettingsChangeResult))]
[JsonSerializable(typeof(IReadOnlyList<ResourcePackMapping>))]
public sealed partial class ConfigurationJsonContext : JsonSerializerContext;