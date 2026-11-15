// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Instances;

public static partial class InstanceMigration
{
    private static string SelectedVersionDirectory(string source, string game, string shared, string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version != Path.GetFileName(version) || version is "." or "..")
            return source;
        if (File.Exists(Path.Combine(source, version + ".json")))
            return source;
        var localCandidate = Path.Combine(game, "versions", version);
        if (File.Exists(Path.Combine(localCandidate, version + ".json")))
            return localCandidate;
        var candidate = Path.Combine(shared, "versions", version);
        return File.Exists(Path.Combine(candidate, version + ".json")) ? candidate : source;
    }

    private static void ApplyModernHmcl(string source, ref string game, ref string format, JsonObject settings, JsonObject fields, JsonObject unknown, JsonArray warnings)
    {
        var file = Path.Combine(source, ".hmcl", "config", "instance-game-settings.json");
        if (!File.Exists(file))
            return;
        format = "hmcl";
        var local = JsonFiles.ReadObject(file);
        unknown["hmclInstance"] = local.DeepClone();
        var overrides = (local["overrideProperties"] as JsonArray)?.Select(v => v?.ToString()).ToHashSet() ?? [];
        void Set(string key, JsonNode? value, string origin)
        {
            if (value is null)
                return;
            settings[key] = value.DeepClone();
            fields[key] = new JsonObject { ["value"] = value.DeepClone(), ["source"] = "HMCL instance:" + origin };
        }

        if (overrides.Contains("runningDirectory") && local["runningDirectory"]?.ToString() is { Length: > 0 } directory)
        {
            game = Resolve(directory, source);
            fields["gameDirectoryPolicy"] = JsonValue.Create("HMCL instance:runningDirectory");
        }
        else
        {
            warnings.Add((JsonNode?)JsonValue.Create("HMCL running directory is inherited or unavailable; confirm the game directory explicitly."));
        }

        if (overrides.Contains("javaType") && overrides.Contains("customJavaPath")
            && local["javaType"]?.ToString() == "CUSTOM" && local["customJavaPath"]?.ToString() is { Length: > 0 } java)
        {
            Set("javaPath", JsonValue.Create(Resolve(java, source)), "customJavaPath");
        }

        foreach (var (from, to) in new[] { ("minMemory", "minMemoryMb"), ("maxMemory", "maxMemoryMb"), ("width", "width"), ("height", "height") })
        {
            if (!overrides.Contains(from))
                continue;
            if (from == "maxMemory" && (!overrides.Contains("autoMemory") || local["autoMemory"]?.GetValue<bool>() != false))
                continue;
            if (int.TryParse(local[from]?.ToString(), out var value) && value > 0)
                Set(to, JsonValue.Create(value), from);
        }
    }
}