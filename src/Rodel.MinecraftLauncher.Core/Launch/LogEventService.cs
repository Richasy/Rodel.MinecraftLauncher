// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Rodel.MinecraftLauncher.Core.Launch;

public sealed class LogEventService(LauncherPaths paths)
{
    /// <summary>Parses bounded source logs with independent stream cursors, resumable fragments and explicit ordering limits.</summary>
    public async Task<LogEventRead> ReadAsync(string session, int budget = 65536, long? stdoutOffset = null, long? stderrOffset = null, JsonObject? parserState = null, string? minimumLevel = null, CancellationToken ct = default)
    {
        var logs = await new LogService(paths).ReadAsync(session, 10000, budget, stdoutOffset, stderrOffset, ct).ConfigureAwait(false);
        var events = new List<LogEvent>();
        var nextState = new Dictionary<string, LogParserContinuation>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var ended = new SessionService(paths).Get(session).Status is "exited" or "failed" or "interrupted";
        foreach (var stream in new[]
        {
            "stdoutLog",
            "stderrLog"
        }

        )
        {
            var info = logs.Streams[stream];
            if (info.Warning is { } warning) warnings.Add(stream + ": " + warning);
            var final = ended && info.HasMore != true;
            var pending = parserState?[stream]?["pending"]?.ToString() ?? string.Empty;
            var textRecord = parserState?[stream]?["textEvent"] as JsonObject;
            if (pending.Length > 131072)
                throw new ArgumentException("Log parser continuation exceeds its context budget.");
            if (textRecord?.ToJsonString().Length > 131072)
                throw new ArgumentException("Text event continuation exceeds its context budget.");
            var textEvent = textRecord is null ? null : JsonSerializer.Deserialize(textRecord, LaunchJsonContext.Default.TextLogEvent);
            var previousNext = parserState?[stream]?["nextOffset"]?.GetValue<long>();
            var currentOffset = info.Offset ?? 0;
            if ((pending.Length > 0 || textEvent is not null) && previousNext != currentOffset)
            {
                pending = string.Empty;
                textEvent = null;
                warnings.Add("Non-contiguous log cursor; previous parser fragment discarded for " + stream);
            }

            var sourceStart = pending.Length > 0 ? parserState?[stream]?["startOffset"]?.GetValue<long>() ?? currentOffset : currentOffset;
            var lines = stream == "stdoutLog" ? logs.StdoutLog : logs.StderrLog;
            var chunk = string.Join('\n', lines);
            if (lines.Count > 0 && info.TrailingPartialLine != true)
                chunk += "\n";
            pending += chunk;
            var consumed = 0;
            void Add(LogEvent entry)
            {
                entry.Stream = stream;
                entry.SourceStartOffset ??= sourceStart;
                entry.SourceEndOffset = info.NextOffset;
                entry.RangeKind = "containing-read-batch";
                if (minimumLevel is null || Rank(entry.Level) == 0 || Rank(entry.Level) >= Rank(minimumLevel))
                    events.Add(entry);
            }

            void FlushText()
            {
                if (textEvent is null)
                    return;
                Add(textEvent);
                textEvent = null;
            }

            void TextLine(string line)
            {
                var parsed = ParseText(line);
                if (parsed.Level != "UNKNOWN")
                {
                    FlushText();
                    parsed.SourceStartOffset = sourceStart;
                    textEvent = parsed;
                    return;
                }

                var continuation = Regex.IsMatch(line, @"^\s*(?:at\s|Caused by:|Suppressed:|\.\.\.\s\d+|[\w.$]+(?:Exception|Error)(?::|\s|$))", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (textEvent is not null && continuation)
                {
                    textEvent.Exception = (textEvent.Exception is { Length: > 0 } stack ? stack + "\n" : string.Empty) + line;
                    textEvent.Raw += "\n" + line;
                    if (JsonSerializer.Serialize(textEvent, LaunchJsonContext.Default.TextLogEvent).Length > 120000)
                    {
                        textEvent.Warning = "Long text event emitted as a bounded fragment.";
                        FlushText();
                    }
                }
                else
                {
                    FlushText();
                    Add(parsed);
                }
            }

            while (consumed < pending.Length)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = pending[consumed..];
                var trimmed = remaining.TrimStart();
                var leading = remaining.Length - trimmed.Length;
                if (trimmed.StartsWith("<log4j:Event", StringComparison.Ordinal) || trimmed.StartsWith("<Event", StringComparison.Ordinal))
                {
                    var closeTag = trimmed.StartsWith("<log4j:") ? "</log4j:Event>" : "</Event>";
                    var close = FindClose(trimmed, closeTag);
                    if (close < 0)
                    {
                        if (!final && remaining.Length <= 131072)
                            break;
                        FlushText();
                        Add(new LogEvent { Message = remaining, Warning = "Incomplete or oversized XML event retained as raw text." });
                        consumed = pending.Length;
                        break;
                    }

                    var xml = trimmed[..(close + closeTag.Length)];
                    FlushText();
                    try
                    {
                        Add(ParseXml(xml));
                    }
                    catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException)
                    {
                        Add(new LogEvent { Message = xml, Warning = ex.Message });
                    }

                    consumed += leading + xml.Length;
                }
                else
                {
                    var newline = remaining.IndexOf('\n');
                    if (newline < 0 && !final && remaining.Length <= 131072)
                        break;
                    var length = newline >= 0 ? newline + 1 : remaining.Length;
                    var line = remaining[..length].TrimEnd('\r', '\n');
                    if (!string.IsNullOrWhiteSpace(line))
                        TextLine(line);
                    consumed += length;
                }
            }

            var next = pending[consumed..];
            if (final)
                FlushText();
            nextState[stream] = new(next, textEvent, next.Length > 0 ? sourceStart : info.NextOffset, info.NextOffset);
        }

        return new(session, events, nextState, logs.Streams, warnings) { Startup = logs.Startup };
    }

    private static int Rank(string? level) => level?.ToUpperInvariant() switch
    {
        "TRACE" => 1,
        "DEBUG" => 2,
        "INFO" => 3,
        "WARN" or "WARNING" => 4,
        "ERROR" => 5,
        "FATAL" => 6,
        _ => 0
    };
    private static int FindClose(string text, string tag)
    {
        var position = 0;
        while ((position = text.IndexOf('<', position)) >= 0)
        {
            if (text.AsSpan(position).StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                var end = text.IndexOf("]]>", position + 9, StringComparison.Ordinal);
                if (end < 0)
                    return -1;
                position = end + 3;
            }
            else if (text.AsSpan(position).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", position + 4, StringComparison.Ordinal);
                if (end < 0)
                    return -1;
                position = end + 3;
            }
            else if (text.AsSpan(position).StartsWith(tag, StringComparison.Ordinal))
            {
                return position;
            }
            else
            {
                position++;
            }
        }

        return -1;
    }

    private static XmlLogEvent ParseXml(string raw)
    {
        var xml = raw;
        if (raw.StartsWith("<log4j:Event") && !raw[..raw.IndexOf('>')].Contains("xmlns:log4j", StringComparison.Ordinal))
            xml = raw.Insert("<log4j:Event".Length, " xmlns:log4j=\"urn:rodel:log4j\"");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        var element = XElement.Load(reader);
        var timestamp = element.Attribute("timestamp")?.Value;
        string? time = null;
        if (long.TryParse(timestamp, out var millis))
        {
            try
            {
                time = DateTimeOffset.FromUnixTimeMilliseconds(millis).ToString("O");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return new XmlLogEvent
        {
            Format = "xml",
            Level = element.Attribute("level")?.Value ?? "UNKNOWN",
            Timestamp = time,
            RawTimestamp = timestamp,
            Thread = element.Attribute("thread")?.Value,
            Logger = element.Attribute("logger")?.Value,
            Message = element.Elements().FirstOrDefault(x => x.Name.LocalName == "Message")?.Value ?? string.Empty,
            Exception = element.Elements().FirstOrDefault(x => x.Name.LocalName == "Throwable")?.Value,
            Raw = raw
        };
    }

    private static TextLogEvent ParseText(string line)
    {
        var match = Regex.Match(line, @"^\[(?<time>[^\]]+)\]\s*\[(?<thread>[^\]/]+)/(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL)\](?:\s*\[(?<logger>[^\]]+)\])?\s*:?\s*(?<message>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return new TextLogEvent
        {
            Format = "text",
            Level = match.Success ? match.Groups["level"].Value : "UNKNOWN",
            RawTimestamp = match.Success ? match.Groups["time"].Value : null,
            Thread = match.Success ? match.Groups["thread"].Value : null,
            Logger = match.Success ? match.Groups["logger"].Value : null,
            Message = match.Success ? match.Groups["message"].Value : line,
            Raw = line
        };
    }
}