// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tools;

/// <summary>Defers recognized embedded images without treating arbitrary strings or upstream fields as binary.</summary>
internal static class ToolImages
{
    internal static bool Contains(JsonNode? value) => Image(value) is not null || value switch
    {
        JsonObject obj => obj.Any(field => Contains(field.Value)),
        JsonArray array => array.Any(Contains),
        _ => false
    };

    internal static JsonNode? Project(JsonNode? value, string? resultId, string pointer)
    {
        if (Image(value) is { } image)
        {
            var reference = ProtocolJson.Object(new ToolImageReference("image", image.MimeType,
                resultId is null ? null : new ToolContinuation("result_image_read",
                    ProtocolJson.Object(new ResultImageReadInput(resultId, pointer), ToolInputJsonContext.Default.ResultImageReadInput))),
                ToolJsonContext.Default.ToolImageReference);
            if (value is not JsonObject obj)
                return reference;
            return new JsonObject(obj.Select(field => KeyValuePair.Create(field.Key, field.Key == "data"
                ? reference : Project(field.Value, resultId, pointer + "/" + Escape(field.Key)))));
        }

        return value switch
        {
            JsonObject obj => new JsonObject(obj.Select(field => KeyValuePair.Create(field.Key,
                Project(field.Value, resultId, pointer + "/" + Escape(field.Key))))),
            JsonArray array => new JsonArray([.. array.Select((item, index) => Project(item, resultId,
                pointer + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)))]),
            _ => value?.DeepClone()
        };
    }

    internal static async Task<(byte[] Bytes, string MimeType)> ReadAsync(string root, ResultImageReadInput input, CancellationToken ct)
    {
        var snapshot = await new ToolResults(root).LoadResultAsync(input.ResultId, ct).ConfigureAwait(false);
        var selected = ToolResults.Select(snapshot, input.Pointer);
        var image = Image(selected) ?? throw new ArgumentException("The selected value is not an embedded image.");
        try
        {
            return (Convert.FromBase64String(image.Base64), image.MimeType);
        }
        catch (FormatException)
        {
            throw new ArgumentException("The saved image has invalid base64 data.");
        }
    }

    private static (string Base64, string MimeType)? Image(JsonNode? value)
    {
        if (value is JsonObject obj && Text(obj["format"]) is { } format && Text(obj["data"]) is { } data
            && MimeType(format) is { } mime)
        {
            return (data, mime);
        }

        if (Text(value) is { } uri && uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            && uri.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) is > 0 and var separator
            && MimeType(uri[11..separator]) is { } uriMime)
        {
            return (uri[(separator + 8)..], uriMime);
        }

        return null;
    }

    private static string? MimeType(string format) => format.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => null
    };

    private static string? Text(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
    private static string Escape(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}