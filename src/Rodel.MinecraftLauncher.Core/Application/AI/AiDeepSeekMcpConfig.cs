// Copyright (c) Richasy. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Owns one Cordis insert operation in the Harness home patch.</summary>
internal static class AiDeepSeekMcpConfig
{
    private const string RowId = "rodel-minecraft-mcp";
    private const string Package = "@deepseek-ai/dsh-mcp-client";
    private const string ServerName = "rodel-minecraft";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private sealed record Patch(YamlSequenceNode? Sequence, Dictionary<(int Start, bool Sequence), int> Ends)
    {
        internal int End(YamlNode node) => node is YamlScalarNode ? Index(node.End) : Ends[(Index(node.Start), node is YamlSequenceNode)];
    }

    internal static JsonObject Entry(string launcher, JsonArray args) => new()
    {
        ["id"] = RowId,
        ["name"] = Package,
        ["config"] = new JsonObject
        {
            ["serverName"] = ServerName,
            ["transport"] = "stdio",
            ["command"] = launcher,
            ["args"] = args
        }
    };

    internal static JsonObject? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var text = Decode(File.ReadAllBytes(path));
        var patch = Parse(text);
        return Observe(text, patch, FindEntry(patch.Sequence));
    }

    internal static Task EditAsync(string path, JsonObject? previous, JsonObject? candidate, CancellationToken ct)
        => AiMcpConfigFile.EditAsync(path, bytes =>
        {
            var updated = Edit(Decode(bytes), previous, candidate);
            var patch = Parse(updated);
            if (!JsonNode.DeepEquals(candidate, Observe(updated, patch, FindEntry(patch.Sequence))))
                throw new InvalidDataException("The updated Harness patch did not retain the intended MCP entry.");
            var encoded = Utf8.GetBytes(updated);
            return HasBom(bytes) ? [.. Encoding.UTF8.Preamble, .. encoded] : encoded;
        }, ct);

    private static string Decode(byte[]? bytes)
        => Utf8.GetString(bytes.AsSpan(HasBom(bytes) ? Encoding.UTF8.Preamble.Length : 0));

    private static bool HasBom(byte[]? bytes) => bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);

    private static string Edit(string text, JsonObject? previous, JsonObject? candidate)
    {
        var patch = Parse(text);
        var sequence = patch.Sequence;
        var found = FindEntry(sequence);
        if (!JsonNode.DeepEquals(previous, Observe(text, patch, found))) throw AiMcpConfigFile.ConfigChanged();

        if (found is { } entry)
        {
            var start = Index(entry.Row.Start);
            var end = patch.End(entry.Row);
            if (candidate is not null)
                return text[..start] + candidate.ToJsonString() + text[end..];
            return Remove(text, patch, entry.Operation);
        }

        if (candidate is null) throw AiMcpConfigFile.ConfigChanged();
        var operation = new JsonObject { ["insert"] = new JsonArray(candidate.DeepClone()) }.ToJsonString();
        if (sequence?.Style == SequenceStyle.Flow)
        {
            var close = patch.End(sequence) - 1;
            return sequence.Children.LastOrDefault() is { } last
                ? text.Insert(patch.End(last), "," + operation)
                : text.Insert(close, operation);
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var offset = sequence is null ? text.Length : patch.End(sequence);
        var lineBreak = offset == 0 || text[offset - 1] is '\n' or '\r' ? string.Empty : newline;
        var item = sequence is null
            ? "[" + operation + "]"
            : new string(' ', checked((int)sequence.Start.Column - 1)) + "- " + operation;
        return text.Insert(offset, lineBreak + item + newline);
    }

    private static string Remove(string text, Patch patch, YamlMappingNode operation)
    {
        var sequence = patch.Sequence!;
        var start = Index(operation.Start);
        var end = patch.End(operation);
        if (sequence.Style == SequenceStyle.Block)
        {
            // The installed operation is a flow mapping on a single block-sequence item.
            var line = text.LastIndexOf('\n', start - 1) + 1;
            if (!text.AsSpan(line, start - line).Trim().SequenceEqual("-")) throw AiMcpConfigFile.ConfigChanged();
            if (sequence.Children.Count == 1)
                return text[..line] + "[]" + text[end..];
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            return text.Remove(line, end - line);
        }

        var index = sequence.Children.IndexOf(operation);
        if (index > 0)
        {
            var comma = FindComma(text, patch.End(sequence.Children[index - 1]), start);
            if (comma == start) throw AiMcpConfigFile.ConfigChanged();
            return text.Remove(start, end - start).Remove(comma, 1);
        }

        var next = sequence.Children.Count > 1 ? Index(sequence.Children[1].Start) : patch.End(sequence) - 1;
        var trailing = FindComma(text, end, next);
        return (trailing == next ? text : text.Remove(trailing, 1)).Remove(start, end - start);
    }

    private static int FindComma(string text, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (text[index] == ',') return index;
            if (text[index] != '#') continue;
            while (index < end && text[index] is not ('\r' or '\n')) index++;
        }

        return end;
    }

    private static Patch Parse(string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
            if (stream.Documents is [{ RootNode: YamlSequenceNode sequence }]) return new(sequence, CollectionEnds(text));
        }
        catch (YamlException error)
        {
            throw new InvalidDataException("The DeepSeek Harness home patch could not be parsed.", error);
        }

        if (stream.Documents.Count == 0 || stream.Documents is [{ RootNode: YamlScalarNode { Value: null or "" } }])
            return new(null, []);
        throw new InvalidDataException("The DeepSeek Harness home patch must be a YAML sequence.");
    }

    private static Dictionary<(int Start, bool Sequence), int> CollectionEnds(string text)
    {
        // The representation nodes expose the opening event's End. The parser's
        // closing events give the exact source spans without reformatting YAML.
        var parser = new Parser(new StringReader(text));
        var pending = new Stack<((int Start, bool Sequence) Key, bool Flow)>();
        var ends = new Dictionary<(int Start, bool Sequence), int>();
        while (parser.MoveNext())
        {
            var current = parser.Current!;
            if (current is MappingStart mapping)
            {
                pending.Push(((Index(mapping.Start), false), mapping.Style == MappingStyle.Flow));
            }
            else if (current is SequenceStart sequence)
            {
                pending.Push(((Index(sequence.Start), true), sequence.Style == SequenceStyle.Flow));
            }
            else if (current is MappingEnd or SequenceEnd)
            {
                var (key, flow) = pending.Pop();
                var end = Index(current.End);
                if (flow && end < text.Length && text[end] == (current is MappingEnd ? '}' : ']')) end++;
                ends[key] = end;
            }
        }

        return ends;
    }

    private static (YamlMappingNode Operation, YamlMappingNode Row)? FindEntry(YamlSequenceNode? sequence)
    {
        (YamlMappingNode, YamlMappingNode)? found = null;
        if (sequence is null) return null;
        foreach (var operation in sequence.Children.OfType<YamlMappingNode>())
        {
            if (Get(operation, "id") is YamlScalarNode { Value: RowId }) throw Conflict();
            if (Get(operation, "insert") is not YamlSequenceNode rows) continue;
            foreach (var row in rows.Children.OfType<YamlMappingNode>())
            {
                if (Get(row, "id") is YamlScalarNode { Value: RowId })
                {
                    if (found is not null || rows.Children.Count != 1 || operation.Children.Count != 1
                        || operation.Style != MappingStyle.Flow || row.Style != MappingStyle.Flow)
                    {
                        throw Conflict();
                    }

                    found = (operation, row);
                }
                else if (Get(row, "config") is YamlMappingNode config && Get(config, "serverName") is YamlScalarNode { Value: ServerName })
                {
                    throw Conflict();
                }
            }
        }

        return found;
    }

    private static int Index(Mark mark) => checked((int)mark.Index);

    private static YamlNode? Get(YamlMappingNode mapping, string key)
        => mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static JsonObject? Observe(string text, Patch patch, (YamlMappingNode Operation, YamlMappingNode Row)? entry)
    {
        if (entry is not { Row: var row }) return null;
        try
        {
            return JsonNode.Parse(text[Index(row.Start)..patch.End(row)])?.AsObject() ?? throw Conflict();
        }
        catch (JsonException)
        {
            throw Conflict();
        }
    }

    private static LauncherOperationException Conflict()
        => new("AI_INTEGRATION_CONFLICT", "The DeepSeek Harness home patch contains a separately configured Rodel MCP row. It was preserved.");
}