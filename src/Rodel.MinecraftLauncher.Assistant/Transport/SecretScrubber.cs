// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Assistant;

// Adapted from Rodel.Agent.Core Providers.Shared/SecretScrubber and ResponseBodyExcerpt.
internal static partial class SecretScrubber
{
    /// <summary>Checks external JSON string values for an explicitly supplied credential.</summary>
    public static bool ContainsSecret(JsonNode? node, string secret) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text.Contains(secret, StringComparison.Ordinal),
        JsonObject item => item.Any(pair => ContainsSecret(pair.Value, secret)),
        JsonArray array => array.Any(item => ContainsSecret(item, secret)),
        _ => false
    };

    /// <summary>Redacts the supplied credential and recognizable credential headers, keys and query values.</summary>
    public static string Scrub(string text, string? secret = null)
    {
        var marker = !string.IsNullOrEmpty(secret) && "[redacted]".Contains(secret, StringComparison.Ordinal) ? "***" : "[redacted]";
        if (!string.IsNullOrEmpty(secret))
        {
            text = text.Replace(secret, marker, StringComparison.Ordinal);
            text = text.Replace(Uri.EscapeDataString(secret), marker, StringComparison.Ordinal);
        }

        text = HeaderRegex().Replace(text, "$1" + marker);
        text = KeyRegex().Replace(text, marker);
        text = QueryRegex().Replace(text, "$1" + marker);
        return text;
    }

    /// <summary>Returns a bounded, scrubbed diagnostic excerpt without unsafe control characters.</summary>
    public static string Excerpt(string text, string? secret = null, int maximum = 512)
    {
        var clean = Scrub(text, secret);
        clean = new string([.. clean.Where(character => !char.IsControl(character) || character is '\n' or '\t')]);
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    /// <summary>Scrubs JSON string values in place while preserving property names and non-string data.</summary>
    public static void Scrub(JsonNode node, string? secret)
    {
        if (node is JsonObject item)
        {
            foreach (var name in item.Select(pair => pair.Key).ToArray())
            {
                if (item[name] is JsonValue value && value.TryGetValue<string>(out var text))
                    item[name] = Scrub(text, secret);
                else if (item[name] is { } child)
                    Scrub(child, secret);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                    array[index] = Scrub(text, secret);
                else if (array[index] is { } child)
                    Scrub(child, secret);
            }
        }
    }

    [GeneratedRegex("""((?<![\p{L}\p{N}_-])(?:authorization|x-api-key|api-key|x-goog-api-key)["']?\s*:\s*["']?(?:Bearer\s+)?|(?<![\p{L}\p{N}_-])Bearer\s+)[A-Za-z0-9\-._~+/=]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();
    [GeneratedRegex(@"(?:sk-(?:ant-|proj-|or-)?[A-Za-z0-9_-]{16,}|AIza[A-Za-z0-9_-]{20,})", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
    [GeneratedRegex("""([?&](?:key|api[_-]?key|token|secret|password)=)[^&\s"'<>]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryRegex();
}