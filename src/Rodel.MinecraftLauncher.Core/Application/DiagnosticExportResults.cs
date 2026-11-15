// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Reports a published raw console export and any unavailable source windows.</summary>
/// <param name="Path">Published archive path.</param>
/// <param name="SessionId">Source session.</param>
/// <param name="Bytes">Archive size.</param>
/// <param name="Warnings">Source availability warnings.</param>
public sealed record LogExportResult(string Path, string SessionId, long Bytes, IReadOnlyList<string> Warnings);

/// <summary>Describes a generated diagnostic entry, bounded console window or original report file.</summary>
public sealed record DiagnosticBundleEntry
{
    /// <summary>Gets the archive-relative entry name.</summary>
    public required string Entry { get; init; }
    /// <summary>Gets whether the entry is generated metadata rather than an original file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Generated { get; init; }
    /// <summary>Gets the original file path or console storage source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
    /// <summary>Gets console source availability when the entry represents a window.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Available { get; init; }
    /// <summary>Gets bytes available at snapshot time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Bytes { get; init; }
    /// <summary>Gets the absolute console start cursor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Offset { get; init; }
    /// <summary>Gets the absolute console end at snapshot time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalBytes { get; init; }
    /// <summary>Gets whether a console prefix or suffix is omitted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }
}

/// <summary>A diagnostic bundle preview or publication; warning payloads remain open for independent subsystem failures.</summary>
public sealed record DiagnosticBundleManifest
{
    /// <summary>Gets the source session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the actual planned archive entries.</summary>
    public required IReadOnlyList<DiagnosticBundleEntry> Files { get; init; }
    /// <summary>Gets heterogeneous source/read warnings without losing their original shape.</summary>
    public required JsonArray Warnings { get; init; }
    /// <summary>Gets whether best-effort text redaction was requested.</summary>
    public bool RedactionEnabled { get; init; }
    /// <summary>Gets the redaction scope and limitations.</summary>
    public required string Redaction { get; init; }
    /// <summary>Gets the snapshot creation timestamp.</summary>
    public required string CreatedAt { get; init; }
    /// <summary>Gets the published output path, absent for a preview or for the in-archive manifest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; internal set; }
}