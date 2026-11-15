// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentVersion(
    string Id,
    string Number,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders)
{
    internal string? PrimarySha1 { get; init; }
    internal string Label => string.Format(Resources.ContentVersionLine, Number, string.Join(", ", GameVersions), string.Join(", ", Loaders));

    internal static ContentVersion Read(JsonObject value)
    {
        var files = (value["files"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var primary = files.FirstOrDefault(file => file["primary"]?.GetValue<bool>() == true) ?? files.FirstOrDefault();
        return new(value["id"]!.ToString(), value["version_number"]?.ToString() ?? value["name"]?.ToString() ?? value["id"]!.ToString(), [.. (value["game_versions"] as JsonArray ?? []).Select(item => item!.ToString())], [.. (value["loaders"] as JsonArray ?? []).Select(item => item!.ToString())])
        {
            PrimarySha1 = (primary?["hashes"] as JsonObject)?["sha1"]?.ToString()
        };
    }
}