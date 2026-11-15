// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>Describes an options.txt snapshot and any deferred edits without replacing the game's extensible setting model.</summary>
public sealed record GameSettingsSnapshot
{
    /// <summary>Gets the options.txt path.</summary>
    public required string Path { get; init; }

    /// <summary>Gets whether options.txt exists.</summary>
    public bool Exists { get; init; }

    /// <summary>Gets the encoding used to interpret the file.</summary>
    public required string Encoding { get; init; }

    /// <summary>Gets the content fingerprint used to detect concurrent edits.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Gets the parsed game-setting values, including unrecognized keys.</summary>
    public required JsonObject Values { get; init; }

    /// <summary>Gets deferred edits with their original recovery metadata, or null when none can be read.</summary>
    public JsonObject? Pending { get; init; }

    /// <summary>Gets an explanation when deferred edits could not be read and were retained.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PendingWarning { get; init; }

    /// <summary>Gets deferred-edit warnings when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Warnings { get; init; }
}