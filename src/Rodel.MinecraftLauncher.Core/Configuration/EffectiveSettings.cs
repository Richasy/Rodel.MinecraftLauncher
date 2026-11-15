// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>Reports merged settings and their origins without narrowing extensible configuration keys.</summary>
/// <param name="Values">The effective values, including unknown settings retained from user configuration.</param>
/// <param name="Sources">The source label for each effective setting.</param>
/// <param name="Warnings">Recoverable resolution warnings that do not discard other usable settings.</param>
public sealed record EffectiveSettings(JsonObject Values, JsonObject Sources, JsonArray Warnings);