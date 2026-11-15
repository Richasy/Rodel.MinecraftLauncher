// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Storage;

// Persisted summaries remain readable even when a known field has an unexpected JSON kind.
// Only values represented losslessly by the typed property leave the extension-data bag.
internal static class StoredReportFields
{
    internal static string? Text(JsonObject fields, string name)
    {
        if (fields[name] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            fields.Remove(name);
            return text;
        }

        return null;
    }

    internal static T? Value<T>(JsonObject fields, string name) where T : struct
    {
        if (fields[name] is JsonValue value && value.TryGetValue<T>(out var result))
        {
            fields.Remove(name);
            return result;
        }

        return null;
    }

    internal static IReadOnlyList<string>? TextArray(JsonObject fields, string name)
    {
        if (fields[name] is not JsonArray values || values.Any(value => value is not JsonValue item || !item.TryGetValue<string>(out _)))
            return null;
        var result = values.Select(value => value!.GetValue<string>()).ToArray();
        fields.Remove(name);
        return result;
    }

    internal static Dictionary<string, JsonElement> Remaining(JsonObject fields)
    {
        if (fields.Count == 0)
            return new(StringComparer.Ordinal);
        // These are the unprojected fields of a persisted document, not a serialized business report.
        using var document = JsonDocument.Parse(fields.ToJsonString());
        var snapshot = document.RootElement.Clone();
        return snapshot.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
    }
}