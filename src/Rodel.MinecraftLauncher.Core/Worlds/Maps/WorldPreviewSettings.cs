// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.IO.Compression;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Recognizes saved vanilla generator settings that the original preview request can express without guessing custom noise or packs.</summary>
internal static class WorldPreviewSettings
{
    internal static (SeedWorldSettings Settings, string? Reason) Read(Dictionary<string, object> data, Dictionary<string, object> generation, string? directory = null)
    {
        var settings = new SeedWorldSettings();
        if (data.GetValueOrDefault("enabled_features") is List<object> features && features.Any(value => value is not "minecraft:vanilla"))
            return (settings, "The saved experimental features cannot be reproduced by a vanilla preview.");
        if (directory is not null && data.Compound("DataPacks").GetValueOrDefault("Enabled") is List<object> enabled)
        {
            foreach (var pack in enabled.OfType<string>().Where(value => value.StartsWith("file/", StringComparison.Ordinal)))
            {
                var name = pack[5..];
                if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name is "." or ".."
                    || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.TrimEnd(' ', '.') != name)
                {
                    return (settings, "The active saved data-pack path cannot be inspected safely.");
                }

                try
                {
                    var path = Path.Combine(directory, "datapacks", name);
                    if (File.Exists(path))
                    {
                        using var archive = ZipFile.OpenRead(path);
                        if (archive.Entries.Any(entry => WorldgenPath(entry.FullName))) return (settings, "An active saved data pack changes world-generation definitions.");
                    }
                    else if (Directory.Exists(path))
                    {
                        if (Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(file => WorldgenPath(Path.GetRelativePath(path, file).Replace('\\', '/'))))
                            return (settings, "An active saved data pack changes world-generation definitions.");
                    }
                    else
                    {
                        return (settings, "An active saved data pack is unavailable for generation inspection.");
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { return (settings, "An active saved data pack could not be inspected: " + error.Message); }
            }
        }

        if (generation.Integer("generate_features") == 0)
            return (settings, "The saved structure-generation setting is not represented by the original preview request.");
        if (generation.Count == 0)
        {
            var name = data.Text("generatorName");
            settings.Preset = name?.ToLowerInvariant() switch { "largebiomes" => "large_biomes", "amplified" => "amplified", _ => "normal" };
            if ((name is not null && !new[] { "default", "normal", "largebiomes", "amplified" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                || (data.GetValueOrDefault("generatorOptions") is { } options && (options is not string text || !string.IsNullOrWhiteSpace(text))))
            {
                return (settings, "The saved custom generator options cannot be reproduced by the vanilla preview presets.");
            }

            return (settings, data.Integer("RandomSeed") is null ? "The save does not record a reproducible original seed." : null);
        }

        var dimensions = generation.Compound("dimensions");
        if (dimensions.Compound("minecraft:overworld").Text("type") != "minecraft:overworld")
            return (settings, "The saved Overworld dimension type is custom or unavailable.");
        var overworld = dimensions.Compound("minecraft:overworld").Compound("generator");
        if (overworld.Text("type") != "minecraft:noise" || overworld.GetValueOrDefault("settings") is not string noise)
            return (settings, "The saved generator does not use a supported original noise-settings preset.");
        settings.Preset = noise switch { "minecraft:overworld" => "normal", "minecraft:large_biomes" => "large_biomes", "minecraft:amplified" => "amplified", _ => string.Empty };
        if (settings.Preset.Length == 0) return (new(), "The saved noise settings are custom or unavailable to the original preview presets.");
        var source = overworld.Compound("biome_source");
        var type = source.Text("type");
        if (type == "minecraft:fixed" && source.Text("biome") is { } biome && biome.StartsWith("minecraft:", StringComparison.Ordinal))
        {
            if (noise != "minecraft:overworld") return (settings, "The fixed-biome world uses nonstandard noise settings.");
            settings.Preset = "single_biome"; settings.Biome = biome[10..];
        }
        else if (type == "minecraft:vanilla_layered")
        {
            if (source.Integer("legacy_biome_init_layer") == 1) return (settings, "The saved legacy biome initialization is not represented by the preview presets.");
            if ((source.Integer("large_biomes") == 1 && noise != "minecraft:overworld")
                || (source.Integer("large_biomes") != 1 && noise == "minecraft:large_biomes"))
            {
                return (settings, "The saved layered biome and noise presets form a custom combination.");
            }

            if (source.Integer("large_biomes") == 1) settings.Preset = "large_biomes";
        }
        else if (type != "minecraft:multi_noise" || source.Text("preset") != "minecraft:overworld")
        {
            return (settings, "The saved biome source is custom or cannot be reproduced from its original preset.");
        }

        if ((source.Integer("seed") is { } biomeSeed && biomeSeed != generation.Integer("seed"))
            || (overworld.Integer("seed") is { } noiseSeed && noiseSeed != generation.Integer("seed")))
        {
            return (settings, "The saved generator uses a seed override outside the world preview request.");
        }

        foreach (var (id, value) in dimensions)
        {
            if (id == "minecraft:overworld") continue;
            if (value is not Dictionary<string, object> dimension) return (settings, "The saved dimension configuration is unavailable.");
            if (dimension.Text("type") != id) return (settings, "A saved dimension uses a nonstandard dimension type.");
            var generator = dimension.Compound("generator");
            var biomes = generator.Compound("biome_source");
            if ((generator.Integer("seed") is { } dimensionSeed && dimensionSeed != generation.Integer("seed"))
                || (biomes.Integer("seed") is { } dimensionBiomeSeed && dimensionBiomeSeed != generation.Integer("seed")))
            {
                return (settings, "A saved dimension uses a seed override outside the world preview request.");
            }

            var standard = id switch
            {
                "minecraft:the_nether" => generator.Text("type") == "minecraft:noise" && generator.Text("settings") == "minecraft:nether"
                    && biomes.Text("type") == "minecraft:multi_noise" && biomes.Text("preset") == "minecraft:nether",
                "minecraft:the_end" => generator.Text("type") == "minecraft:noise" && generator.Text("settings") == "minecraft:end" && biomes.Text("type") == "minecraft:the_end",
                _ => false,
            };
            if (!standard) return (settings, "The save contains a custom dimension generator that the original preview request does not represent.");
        }

        return (settings, null);
    }

    private static bool WorldgenPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 4 && parts[0] == "data" && (parts[2] is "worldgen" or "dimension" or "dimension_type"
            || (parts.Length >= 5 && parts[2] == "tags" && parts[3] == "worldgen"));
    }
}