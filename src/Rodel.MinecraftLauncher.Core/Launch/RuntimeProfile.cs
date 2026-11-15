// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Describes known launch components without requiring a supported installer.</summary>
public static class RuntimeProfile
{
    /// <summary>Identifies known loader coordinates and publisher FML version arguments without restricting custom profiles.</summary>
    public static (string? Kind, string? Version) Loader(JsonObject metadata)
    {
        var game = metadata["arguments"]?["game"] as JsonArray;
        string? Argument(string name)
        {
            for (var index = 0; game is not null && index + 1 < game.Count; index++)
            {
                if (game[index] is JsonValue key && key.TryGetValue<string>(out var text) && text == name
                    && game[index + 1] is JsonValue value && value.TryGetValue<string>(out var argument))
                {
                    return argument;
                }
            }

            return null;
        }

        if (Argument("--fml.neoForgeVersion") is { } neoForgeVersion)
            return ("neoforge", neoForgeVersion);
        var libraries = (metadata["libraries"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        if (Argument("--fml.forgeVersion") is { } legacyNeoForgeVersion
            && libraries.Any(library => library["name"]?.ToString().StartsWith("net.neoforged.fancymodloader:", StringComparison.Ordinal) == true))
        {
            return ("neoforge", legacyNeoForgeVersion);
        }

        foreach (var library in libraries.Reverse())
        {
            var parts = library["name"]?.ToString().Split(':') ?? [];
            if (parts.Length < 3)
                continue;
            var kind = (parts[0], parts[1]) switch
            {
                ("net.fabricmc", "fabric-loader") => "fabric",
                ("org.quiltmc", "quilt-loader") => "quilt",
                ("net.neoforged", "neoforge") => "neoforge",
                ("net.neoforged", "forge") => "neoforge",
                ("net.minecraftforge", "forge") => "forge",
                ("net.minecraftforge", "fmlloader") => "forge",
                _ => null
            };
            if (kind is not null)
                return (kind, parts[2]);
        }

        var main = metadata["mainClass"]?.ToString() ?? string.Empty;
        return (main.Contains("fabricmc", StringComparison.Ordinal) ? "fabric" : main.Contains("quiltmc", StringComparison.Ordinal) ? "quilt" : null, null);
    }

    /// <summary>Resolves a profile and describes recognized components without restricting unknown metadata.</summary>
    public static async Task<RuntimeDescription> DescribeAsync(string root, string version, CancellationToken ct = default)
    {
        var resolved = await new VersionResolver().ResolveAsync(root, version, ct).ConfigureAwait(false);
        var loader = Loader(resolved);
        return new(version, resolved["_minecraftVersion"]?.ToString() ?? version, loader.Kind, loader.Version,
            resolved["mainClass"]?.DeepClone(), resolved["javaVersion"]?.DeepClone(), "version-metadata", resolved);
    }
}