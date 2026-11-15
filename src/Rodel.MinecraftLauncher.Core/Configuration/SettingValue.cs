// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>Reports one effective setting, its origin, and configuration-resolution warnings.</summary>
/// <param name="Key">The requested configuration key.</param>
/// <param name="Value">The value, or null when the key has no effective value.</param>
/// <param name="Source">The source label, or unset when no value was inherited.</param>
/// <param name="Warnings">Recoverable warnings produced while resolving the configuration.</param>
public sealed record SettingValue(string Key, JsonNode? Value, string Source, JsonArray Warnings);