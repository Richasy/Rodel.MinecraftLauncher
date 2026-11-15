// Copyright (c) Richasy. All rights reserved.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Reads Fabric metadata containing literal string control characters accepted by the loader, without rewriting the archive.</summary>
internal static class FabricMetadataJson
{
    internal static async Task<JsonNode?> ReadAsync(Stream input, CancellationToken ct)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string text;
        try { text = await reader.ReadToEndAsync(ct).ConfigureAwait(false); }
        catch (DecoderFallbackException error) { throw new JsonException("Fabric metadata is not valid UTF-8.", error); }

        var normalized = new StringBuilder(text.Length);
        var inString = false;
        var escaped = false;
        foreach (var character in text)
        {
            if (inString && character < ' ')
                normalized.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else
                normalized.Append(character);
            if (character == '"' && !escaped) inString = !inString;
            escaped = inString && character == '\\' && !escaped;
        }

        return JsonNode.Parse(normalized.ToString());
    }
}