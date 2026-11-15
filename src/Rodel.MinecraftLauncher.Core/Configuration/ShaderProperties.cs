// Copyright (c) Richasy. All rights reserved.

using System.Globalization;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Configuration;

/// <summary>Edits only shader-owned Java-properties entries while retaining all unrelated bytes and line endings.</summary>
internal sealed class ShaderProperties(byte[] bytes)
{
    private string _text = Encoding.Latin1.GetString(bytes);

    internal string? Get(string key)
        => Entries().LastOrDefault(entry => entry.Key == key)?.Value;

    internal void Set(string key, string value)
    {
        var entry = Entries().LastOrDefault(candidate => candidate.Key == key);
        var newline = _text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replacement = key + "=" + Escape(value);
        if (entry is not null)
            _text = _text[..entry.Start] + replacement + _text[entry.End..];
        else
            _text += (_text.Length > 0 && !_text.EndsWith('\n') && !_text.EndsWith('\r') ? newline : string.Empty) + replacement + newline;
    }

    internal byte[] Bytes() => Encoding.Latin1.GetBytes(_text);

    private sealed record Entry(int Start, int End, string Key, string Value);

    private IEnumerable<Entry> Entries()
    {
        var offset = 0;
        while (offset < _text.Length)
        {
            var start = offset;
            var logical = new StringBuilder();
            int end;
            while (true)
            {
                end = offset;
                while (end < _text.Length && _text[end] is not ('\r' or '\n')) end++;
                var line = _text[offset..end];
                if (offset > start) line = line.TrimStart(' ', '\t', '\f');
                var slashes = 0;
                for (var i = line.Length - 1; i >= 0 && line[i] == '\\'; i--) slashes++;
                var comment = offset == start && line.TrimStart(' ', '\t', '\f') is { Length: > 0 } trimmed && trimmed[0] is '#' or '!';
                var continued = !comment && slashes % 2 != 0;
                logical.Append(continued ? line[..^1] : line);
                offset = end;
                if (offset < _text.Length && _text[offset] == '\r') offset++;
                if (offset < _text.Length && _text[offset] == '\n') offset++;
                if (!continued || offset >= _text.Length) break;
            }

            var text = logical.ToString().TrimStart(' ', '\t', '\f');
            if (text.Length == 0 || text[0] is '#' or '!') continue;
            var separator = 0;
            var escaped = false;
            while (separator < text.Length)
            {
                var character = text[separator];
                if (!escaped && character is '=' or ':' or ' ' or '\t' or '\f') break;
                escaped = !escaped && character == '\\';
                separator++;
            }

            var value = separator;
            while (value < text.Length && text[value] is ' ' or '\t' or '\f') value++;
            if (value < text.Length && text[value] is '=' or ':') value++;
            while (value < text.Length && text[value] is ' ' or '\t' or '\f') value++;
            yield return new(start, end, Decode(text[..separator]), Decode(text[value..]));
        }
    }

    private static string Decode(string value)
    {
        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length) { result.Append(value[i]); continue; }

            var escaped = value[++i];
            if (escaped == 'u')
            {
                if (i + 4 >= value.Length || !ushort.TryParse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    throw new InvalidDataException("The shader properties contain an invalid Unicode escape; repair the original configuration before changing it.");
                result.Append((char)code); i += 4;
            }
            else
            {
                result.Append(escaped switch { 't' => '\t', 'n' => '\n', 'r' => '\r', 'f' => '\f', _ => escaped });
            }
        }

        return result.ToString();
    }

    private static string Escape(string value)
    {
        var output = new StringBuilder();
        foreach (var character in value)
        {
            if (character is '\\' or '=' or ':' or '#' or '!' or ' ') output.Append('\\').Append(character);
            else if (character is < ' ' or > '~') output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else output.Append(character);
        }

        return output.ToString();
    }
}