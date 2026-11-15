// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

internal static class SeedGameLanguage
{
    internal static Task<IReadOnlyDictionary<string, string>> ReadAsync(SeedRuntimeBinding binding, string language, CancellationToken ct) =>
        ReadAsync(binding.Profile.MinecraftDirectory, binding.ClientJar, binding.Metadata, language, ct);

    internal static async Task<IReadOnlyDictionary<string, string>> ReadAsync(string minecraftDirectory, string clientJar, System.Text.Json.Nodes.JsonObject metadata, string language, CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(clientJar))
        {
            if (archive.GetEntry("assets/minecraft/lang/en_us.json") is { } entry)
            {
                using var stream = entry.Open();
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                Read(json.RootElement, names);
            }
        }

        language = language.Replace('-', '_').ToLowerInvariant();
        var assetId = metadata["assetIndex"]?["id"]?.GetValue<string>() ?? metadata["assets"]?.GetValue<string>();
        if (assetId is null || !language.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')) return names;
        var root = SharedRuntimeLayout.Resolve(minecraftDirectory, "assets");
        var indexPath = Path.Combine(root, "indexes", assetId + ".json");
        if (!File.Exists(indexPath)) return names;
        var hash = JsonFiles.ReadObject(indexPath)["objects"]?[$"minecraft/lang/{language}.json"]?["hash"]?.GetValue<string>();
        if (hash is not { Length: 40 } || !hash.All(Uri.IsHexDigit)) return names;
        var path = Path.Combine(root, "objects", hash[..2], hash);
        if (!File.Exists(path)) return names;
        var localized = File.OpenRead(path);
        await using (localized.ConfigureAwait(false))
        {
            using var json = await JsonDocument.ParseAsync(localized, cancellationToken: ct).ConfigureAwait(false);
            Read(json.RootElement, names);
        }

        return names;
    }

    private static void Read(JsonElement json, Dictionary<string, string> names)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (property.Name.StartsWith("biome.minecraft.", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String)
            {
                names[property.Name[16..]] = property.Value.GetString()!;
            }
            else if (property.Value.ValueKind == JsonValueKind.String && (property.Name.StartsWith("block.minecraft.", StringComparison.Ordinal)
                || property.Name.StartsWith("item.minecraft.", StringComparison.Ordinal) || property.Name.StartsWith("enchantment.minecraft.", StringComparison.Ordinal)))
            {
                names[property.Name] = property.Value.GetString()!;
            }
        }
    }
}