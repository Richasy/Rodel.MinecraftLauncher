// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

internal static class HmclPack
{
    internal static IReadOnlyDictionary<string, string> Dependencies(JsonObject manifest)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in manifest["addons"] as JsonArray ?? [])
        {
            var addon = node as JsonObject ?? throw new InvalidDataException("HMCL addon must be an object.");
            var id = addon["id"]?.GetValue<string>() ?? throw new InvalidDataException("HMCL addon ID is missing.");
            var version = addon["version"]?.GetValue<string>() ?? throw new InvalidDataException($"HMCL addon version is missing: {id}");
            var key = id switch
            {
                "game" => "minecraft",
                "fabric" => "fabric-loader",
                "quilt" => "quilt-loader",
                "forge" or "neoforge" => id,
                _ => "hmcl-addon:" + id
            };
            if (dependencies.ContainsKey(key))
                throw new InvalidDataException($"Duplicate HMCL addon: {id}");
            dependencies[key] = version;
        }

        return dependencies;
    }

    internal static IReadOnlyList<PackFileDeclaration> Files(JsonObject manifest)
    {
        var files = manifest["files"] as JsonArray ?? throw new InvalidDataException("HMCL manifest has no files array.");
        var result = new List<PackFileDeclaration>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = DistributionRoot(manifest);
        foreach (var node in files)
        {
            var file = node as JsonObject ?? throw new InvalidDataException("HMCL file declaration must be an object.");
            var path = file["path"]?.GetValue<string>() ?? throw new InvalidDataException("HMCL file path is missing.");
            _ = PackFiles.Safe(Path.GetTempPath(), path);
            if (!seen.Add(path))
                throw new InvalidDataException($"Duplicate declared HMCL file: {path}");
            var hash = file["hash"]?.GetValue<string>();
            if (hash is null || hash.Length != 40 || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException($"HMCL file has no valid SHA1: {path}");
            result.Add(new(file, hash.ToLowerInvariant(), root is null ? null : ChildUri(root, "overrides/" + path).AbsoluteUri));
        }

        return result;
    }

    internal static Uri ManifestUri(JsonObject manifest)
    {
        var root = DistributionRoot(manifest)
            ?? throw new InvalidDataException("This HMCL pack has no HTTP(S) distribution source. Local files and ordinary launch remain available.");
        return ChildUri(root, "server-manifest.json");
    }

    internal static bool HasDisabledVariant(string gameDirectory, string relative)
    {
        if (!relative.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) || relative.AsSpan(5).Contains('/'))
            return false;
        var target = PackFiles.Safe(gameDirectory, relative);
        return File.Exists(target + ".disabled") || File.Exists(target + ".old");
    }

    private static Uri? TryRoot(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    internal static Uri? DistributionRoot(JsonObject manifest) =>
        manifest["fileApi"] is JsonValue value && value.TryGetValue<string>(out var text) ? TryRoot(text) : null;

    private static Uri ChildUri(Uri root, string relative)
    {
        var builder = new UriBuilder(root)
        {
            Path = root.AbsolutePath.TrimEnd('/') + "/" + string.Join('/', relative.Split('/').Select(Uri.EscapeDataString)),
            Fragment = string.Empty
        };
        return builder.Uri;
    }
}