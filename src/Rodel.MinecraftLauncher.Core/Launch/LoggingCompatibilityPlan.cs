// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>A known-library compatibility plan; it does not claim to secure arbitrary logging implementations.</summary>
/// <param name="Enabled">Whether compatibility preparation is enabled.</param>
/// <param name="ArchiveCopies">Exact library copies to prepare without modifying shared originals.</param>
public sealed record LoggingCompatibilityPlan(bool Enabled, IReadOnlyList<LoggingArchiveCopy> ArchiveCopies)
{
    /// <summary>Gets the evidence used to select known libraries.</summary>
    public string Source { get; init; } = "known-library-coordinates";
    /// <summary>Gets whether caller-supplied logging configuration remains intact.</summary>
    public bool CustomConfigurationPreserved { get; init; } = true;
    /// <summary>Gets the explicit limits of the compatibility measure.</summary>
    public string Scope { get; init; } = "Known legacy JndiLookup removal and non-overriding defaults; not a general security guarantee.";
    /// <summary>Gets original extension fields when reopening a stored plan.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}

/// <summary>An exact source archive and its independent compatibility copy.</summary>
/// <param name="Source">Original shared archive path.</param>
/// <param name="SourceSha256">Reviewed source digest.</param>
/// <param name="Destination">Private output archive path.</param>
/// <param name="RemoveEntry">Exact entry omitted from the compatibility copy.</param>
/// <param name="Library">Declared library coordinate.</param>
public sealed record LoggingArchiveCopy(string Source, string SourceSha256, string Destination, string RemoveEntry, string Library)
{
    /// <summary>Gets the actual preparation outcome when execution has run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; internal set; }
    /// <summary>Gets the original preparation failure when the source must be retained.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; internal set; }
    /// <summary>Gets original unknown fields when reopening a stored copy plan.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}