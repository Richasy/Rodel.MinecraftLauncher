// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Relates a game's stored resource-pack selection to the real installed inventory.</summary>
/// <param name="Entry">Original selected pack name.</param>
/// <param name="Present">Whether a matching installed entry exists.</param>
/// <param name="Enabled">Whether that installed entry is enabled, or null when absent.</param>
/// <param name="State">Observed selection state without treating built-in entries as errors.</param>
public sealed record ResourcePackSelectionState(string Entry, bool Present, bool? Enabled, string State);

/// <summary>Reads resource-pack inventory, selection and deferred game-settings evidence without changing them.</summary>
/// <param name="Installed">Actual recognized installed entries.</param>
/// <param name="Selected">Original game selection values, including unfamiliar entries.</param>
/// <param name="SelectionStates">Relationship of each selected name to installed content.</param>
/// <param name="Pending">Open deferred settings record.</param>
/// <param name="PendingWarning">Deferred settings read warning.</param>
/// <param name="Warnings">Heterogeneous original game-settings warnings.</param>
/// <param name="Fingerprint">Settings concurrency fingerprint.</param>
public sealed record ResourcePackSelectionSummary(IReadOnlyList<ContentEntry> Installed, JsonArray Selected, IReadOnlyList<ResourcePackSelectionState> SelectionStates,
    JsonObject? Pending, string? PendingWarning, JsonArray? Warnings, string Fingerprint);