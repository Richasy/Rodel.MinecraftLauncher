// Copyright (c) Richasy. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Edits the user-scope MCP value without serializing the surrounding JSON.</summary>
internal static class AiJsonMcpConfig
{
    private const string Servers = "mcpServers";
    private const string Entry = "rodel-minecraft";
    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private sealed record Property(string Name, int Start, int ValueStart, int End);

    internal static Task EditAsync(string path, JsonObject? previous, JsonObject? candidate, CancellationToken ct)
        => AiMcpConfigFile.EditAsync(path, bytes =>
        {
            var updated = Edit(bytes ?? "{}"u8.ToArray(), previous, candidate);
            var offset = updated.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            var observed = JsonNode.Parse(updated.AsSpan(offset), documentOptions: Options)?[Servers]?[Entry];
            if (!JsonNode.DeepEquals(observed, candidate))
                throw new JsonException("The updated client configuration did not retain the intended MCP entry.");
            return updated;
        }, ct);

    private static byte[] Edit(byte[] bytes, JsonObject? previous, JsonObject? candidate)
    {
        var bom = Encoding.UTF8.Preamble;
        var offset = bytes.AsSpan().StartsWith(bom) ? bom.Length : 0;
        var root = ReadObject(bytes, offset);
        var servers = root.Properties.SingleOrDefault(property => property.Name == Servers);
        if (servers is null)
        {
            if (previous is not null || candidate is null) throw AiMcpConfigFile.ConfigChanged();
            return Insert(bytes, root, Servers, new JsonObject { [Entry] = candidate.DeepClone() });
        }

        var table = ReadObject(bytes, servers.ValueStart);
        var target = table.Properties.SingleOrDefault(property => property.Name == Entry);
        if (target is null)
        {
            if (previous is not null || candidate is null) throw AiMcpConfigFile.ConfigChanged();
            return Insert(bytes, table, Entry, candidate);
        }

        var observed = JsonNode.Parse(bytes.AsSpan(target.ValueStart, target.End - target.ValueStart), documentOptions: Options);
        if (previous is null || !JsonNode.DeepEquals(observed, previous)) throw AiMcpConfigFile.ConfigChanged();
        if (candidate is not null)
            return [.. bytes.AsSpan(0, target.ValueStart), .. Encoding.UTF8.GetBytes(candidate.ToJsonString()), .. bytes.AsSpan(target.End)];

        var index = table.Properties.IndexOf(target);
        if (index > 0)
        {
            var comma = FindComma(bytes, table.Properties[index - 1].End, target.Start);
            return [.. bytes.AsSpan(0, comma), .. bytes.AsSpan(comma + 1, target.Start - comma - 1), .. bytes.AsSpan(target.End)];
        }

        var next = table.Properties.Count > 1 ? table.Properties[1].Start : table.End;
        var trailing = FindComma(bytes, target.End, next);
        return trailing == next
            ? [.. bytes.AsSpan(0, target.Start), .. bytes.AsSpan(target.End)]
            : [.. bytes.AsSpan(0, target.Start), .. bytes.AsSpan(target.End, trailing - target.End), .. bytes.AsSpan(trailing + 1)];
    }

    private static byte[] Insert(byte[] bytes, (List<Property> Properties, int End) obj, string key, JsonObject value)
    {
        var last = obj.Properties.LastOrDefault();
        var offset = last?.End ?? obj.End;
        var property = Encoding.UTF8.GetBytes((last is null ? string.Empty : ",") + "\"" + key + "\":" + value.ToJsonString());
        return [.. bytes.AsSpan(0, offset), .. property, .. bytes.AsSpan(offset)];
    }

    private static (List<Property> Properties, int End) ReadObject(byte[] bytes, int offset)
    {
        var reader = new Utf8JsonReader(bytes.AsSpan(offset), new JsonReaderOptions
        {
            AllowTrailingCommas = Options.AllowTrailingCommas,
            CommentHandling = Options.CommentHandling
        });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("The MCP configuration must contain a JSON object.");

        var properties = new List<Property>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString()!;
            var start = checked(offset + (int)reader.TokenStartIndex);
            reader.Read();
            var value = checked(offset + (int)reader.TokenStartIndex);
            reader.Skip();
            properties.Add(new(name, start, value, checked(offset + (int)reader.BytesConsumed)));
        }

        if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("The MCP configuration is incomplete.");
        return (properties, checked(offset + (int)reader.TokenStartIndex));
    }

    private static int FindComma(byte[] bytes, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (bytes[i] == ',') return i;
            if (bytes[i] != '/' || i + 1 >= end) continue;
            if (bytes[i + 1] == '/')
            {
                while (i < end && bytes[i] is not (10 or 13)) i++;
            }
            else if (bytes[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < end && (bytes[i] != '*' || bytes[i + 1] != '/')) i++;
                i++;
            }
        }

        return end;
    }
}