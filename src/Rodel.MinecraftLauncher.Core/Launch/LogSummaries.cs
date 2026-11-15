// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Describes a bounded console window, independently of whether a crash snapshot was saved.</summary>
/// <param name="Complete">Whether the stream ended without a read failure or discarded prefix.</param>
/// <param name="Capturing">Whether capture is still in progress.</param>
/// <param name="CapturedBytes">Total bytes consumed after credential redaction.</param>
/// <param name="DiscardedBytes">Oldest bytes evicted from the memory window.</param>
/// <param name="Warning">An original capture failure, if any.</param>
/// <param name="RetainedBytes">Bytes still available in the window.</param>
public sealed record LogCaptureStatus(bool Complete, bool Capturing, long CapturedBytes, long DiscardedBytes, string? Warning, long RetainedBytes);

/// <summary>A bounded read of one stream; null offset selects its latest bytes.</summary>
/// <param name="StandardError">Selects stderr instead of stdout.</param>
/// <param name="Offset">Absolute post-redaction byte cursor, including any evicted prefix.</param>
/// <param name="ByteBudget">Maximum bytes to return, no greater than the memory window.</param>
internal sealed record LogReadRequest(bool StandardError, long? Offset, int ByteBudget);
/// <summary>An immutable raw window with enough evidence to distinguish missing, evicted and pending output.</summary>
/// <param name="Bytes">Retained bytes copied independently of the producer.</param>
/// <param name="Offset">Absolute position of the returned first byte.</param>
/// <param name="FirstOffset">Oldest available byte cursor.</param>
/// <param name="TotalBytes">Absolute end of the observed stream.</param>
/// <param name="LeadingPartialLine">Whether the first byte may be inside a line.</param>
/// <param name="Capture">Observed stream completion and eviction counts.</param>
/// <param name="Source">Memory, saved, or unavailable storage.</param>
/// <param name="Warning">An access or capture warning, including released memory.</param>
internal sealed record LogReadSnapshot(byte[] Bytes, long Offset, long FirstOffset, long TotalBytes,
    bool LeadingPartialLine, LogCaptureStatus? Capture, string Source, string? Warning = null);
/// <summary>Returns a snapshot or an explicit invalid-read error, never an empty success substitute.</summary>
/// <param name="Snapshot">Available snapshot when the request is valid.</param>
/// <param name="Error">The invalid cursor or budget failure, when no snapshot was produced.</param>
internal sealed record LogReadResponse(LogReadSnapshot? Snapshot, string? Error = null);

/// <summary>A bounded preview of both retained log streams.</summary>
/// <param name="SessionId">Source game session.</param>
/// <param name="ByteBudget">Combined requested byte budget.</param>
/// <param name="Streams">Per-stream cursor and truncation evidence.</param>
/// <param name="StdoutLog">Decoded standard-output lines.</param>
/// <param name="StderrLog">Decoded standard-error lines.</param>
public sealed record LogPreview(string SessionId, int ByteBudget, IReadOnlyDictionary<string, LogStreamSummary> Streams, IReadOnlyList<string> StdoutLog, IReadOnlyList<string> StderrLog)
{
    /// <summary>Gets persistent startup observations even when normal console capture has already been released.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StartupReport? Startup { get; init; }
}

/// <summary>Reports actual decoded log bytes, cursor continuity and retained capture evidence.</summary>
public sealed record LogStreamSummary
{
    /// <summary>Gets memory, saved, or unavailable without implying that an empty preview is a complete log.</summary>
    public string Source { get; init; } = "unavailable";
    /// <summary>Gets a capture, access, cleanup, or released-memory explanation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; init; }
    /// <summary>Gets the oldest retained absolute byte cursor; earlier requested cursors have been evicted.</summary>
    public long FirstOffset { get; init; }
    /// <summary>Gets the decoder's canonical encoding name.</summary>
    public required string Encoding { get; init; }
    /// <summary>Gets a decoder fallback warning, when required.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EncodingWarning { get; init; }
    /// <summary>Gets whether any retained segment was available.</summary>
    public bool Available { get; init; }
    /// <summary>Gets the first decoded byte cursor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Offset { get; init; }
    /// <summary>Gets the continuation byte cursor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NextOffset { get; init; }
    /// <summary>Gets the total retained stream bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RetainedBytes { get; init; }
    /// <summary>Gets the bytes consumed by the decoder.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReturnedBytes { get; init; }
    /// <summary>Gets whether the bounded preview omitted stream data.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }
    /// <summary>Gets whether the first returned line begins inside a source line.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LeadingPartialLine { get; init; }
    /// <summary>Gets whether the last returned line lacks its source newline.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TrailingPartialLine { get; init; }
    /// <summary>Gets whether more retained bytes remain.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasMore { get; init; }
    /// <summary>Gets the number of retained log segments.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SegmentCount { get; init; }
    /// <summary>Gets original persisted capture evidence, including unfamiliar fields.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Capture { get; init; }
}

/// <summary>A parsed event with source-batch byte provenance rather than an invented global ordering.</summary>
[JsonDerivedType(typeof(TextLogEvent))]
[JsonDerivedType(typeof(XmlLogEvent))]
public record LogEvent
{
    /// <summary>Gets the observed source format.</summary>
    public string Format { get; init; } = "raw";
    /// <summary>Gets the parsed severity or UNKNOWN.</summary>
    public string Level { get; init; } = "UNKNOWN";
    /// <summary>Gets the decoded event message.</summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>Gets a bounded parse or fragmentation warning.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; set; }
    /// <summary>Gets the source stream name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stream { get; set; }
    /// <summary>Gets the start of the containing source read batch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceStartOffset { get; set; }
    /// <summary>Gets the end cursor of the containing source read batch.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceEndOffset { get; set; }
    /// <summary>Gets the precision of these source byte ranges.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RangeKind { get; set; }
}

/// <summary>A recognized or unrecognized text line, including accumulated exception continuation.</summary>
public sealed record TextLogEvent : LogEvent
{
    /// <summary>Gets the original textual timestamp, if recognized.</summary>
    public string? RawTimestamp { get; init; }
    /// <summary>Gets the recognized thread name.</summary>
    public string? Thread { get; init; }
    /// <summary>Gets the recognized logger name.</summary>
    public string? Logger { get; init; }
    /// <summary>Gets the retained original text and continuation lines.</summary>
    public string Raw { get; set; } = string.Empty;
    /// <summary>Gets accumulated exception text when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Exception { get; set; }
    /// <summary>Gets unfamiliar caller-supplied continuation fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}

/// <summary>A parsed Log4j XML event retaining both normalized and original timestamps.</summary>
public sealed record XmlLogEvent : LogEvent
{
    /// <summary>Gets the normalized UTC timestamp, if representable.</summary>
    public string? Timestamp { get; init; }
    /// <summary>Gets the original XML timestamp attribute.</summary>
    public string? RawTimestamp { get; init; }
    /// <summary>Gets the XML thread attribute.</summary>
    public string? Thread { get; init; }
    /// <summary>Gets the XML logger attribute.</summary>
    public string? Logger { get; init; }
    /// <summary>Gets XML throwable text, if present.</summary>
    public string? Exception { get; init; }
    /// <summary>Gets the original XML event fragment.</summary>
    public required string Raw { get; init; }
}

/// <summary>Continuation evidence for one independently ordered log stream.</summary>
/// <param name="Pending">Incomplete decoded fragment.</param>
/// <param name="TextEvent">Pending recognized text event.</param>
/// <param name="StartOffset">Containing-batch starting byte cursor.</param>
/// <param name="NextOffset">Expected byte cursor of the next read.</param>
public sealed record LogParserContinuation(string Pending, TextLogEvent? TextEvent, long? StartOffset, long? NextOffset);

/// <summary>A bounded parsed log read and its per-stream continuation state.</summary>
/// <param name="SessionId">Source session.</param>
/// <param name="Events">Events in per-stream source order.</param>
/// <param name="ParserState">Continuations required to resume each stream.</param>
/// <param name="Streams">Byte-cursor and truncation evidence.</param>
/// <param name="Warnings">Parser continuity warnings.</param>
/// <param name="Ordering">Explicit limitation on cross-stream ordering.</param>
public sealed record LogEventRead(string SessionId, IReadOnlyList<LogEvent> Events, IReadOnlyDictionary<string, LogParserContinuation> ParserState, IReadOnlyDictionary<string, LogStreamSummary> Streams, IReadOnlyList<string> Warnings,
    string Ordering = "per-stream source order; stdout and stderr are not a single causal timeline")
{
    /// <summary>Gets startup timing evidence independently of event paging or console retention.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StartupReport? Startup { get; init; }
}