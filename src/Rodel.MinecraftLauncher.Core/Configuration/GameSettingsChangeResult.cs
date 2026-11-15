// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>A game-settings operation result; only the game's extensible values and retained pending document stay open.</summary>
public record GameSettingsChangeResult
{
    /// <summary>Gets the actual operation state, including planned, conflict, deferred and nothing-pending.</summary>
    public required string State { get; set; }
    /// <summary>Gets the edited options file when a direct change was planned.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    /// <summary>Gets the original options fingerprint.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fingerprint { get; init; }
    /// <summary>Gets the encoding retained for the options file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Encoding { get; init; }
    /// <summary>Gets the requested extensible option values, without narrowing mod-defined keys.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Changes { get; init; }
    /// <summary>Gets why a write was deferred.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; internal set; }
    /// <summary>Gets the fingerprint after successful publication.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewFingerprint { get; internal set; }
    /// <summary>Gets an ancillary cleanup warning after successful publication.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; internal set; }
    /// <summary>Gets the retained file containing discarded pending edits.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecoveryPath { get; init; }
    /// <summary>Gets option names whose current values conflict with the queued baseline.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Conflicts { get; init; }
    /// <summary>Gets the original persisted pending document when a conflict needs inspection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Pending { get; init; }
    /// <summary>Gets conflicts explicitly overwritten by the caller.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ConflictsOverwritten { get; init; }
}

/// <summary>An exact content-owned resource-pack identity change.</summary>
/// <param name="OldName">Previously selected file name.</param>
/// <param name="NewName">Replacement name, or null to remove the selection.</param>
public sealed record ResourcePackMapping(string OldName, string? NewName);