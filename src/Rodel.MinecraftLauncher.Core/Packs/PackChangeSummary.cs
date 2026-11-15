// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Packs;

internal static class PackChangeSummaryBuilder
{
    private sealed record ModFile(string Key, string Id, string Name, string? Version, PackFileChange Action);

    internal static async Task<PackChangeSummary> BuildAsync(string game, string incoming, IReadOnlyList<PackFileChange> actions, CancellationToken ct)
    {
        var changed = actions.Where(action => action.Action != "unchanged").ToArray();
        var before = new ModFile?[changed.Length];
        var after = new ModFile?[changed.Length];
        var warnings = new ConcurrentQueue<PackMetadataWarning>();
        await Parallel.ForEachAsync(Enumerable.Range(0, changed.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (index, token) =>
        {
            var action = changed[index];
            var path = action.Path;
            if (!path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) || !ContentService.IsModFileName(path)) return;
            if (action.Action is "delete" or "replace")
                before[index] = await ReadAsync(game, action, warnings, token).ConfigureAwait(false);
            if (action.Action is "add" or "replace")
                after[index] = await ReadAsync(incoming, action, warnings, token).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var oldGroups = before.OfType<ModFile>().GroupBy(file => file.Key).ToDictionary(group => group.Key, group => group.ToArray());
        var newGroups = after.OfType<ModFile>().GroupBy(file => file.Key).ToDictionary(group => group.Key, group => group.ToArray());
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mods = new List<PackModChange>();
        foreach (var (key, oldFiles) in oldGroups)
        {
            if (oldFiles.Length != 1 || !newGroups.TryGetValue(key, out var newFiles) || newFiles.Length != 1) continue;
            var previous = oldFiles[0];
            var next = newFiles[0];
            if (!ReferenceEquals(previous.Action, next.Action)
                && (previous.Action.Action != "delete" || next.Action.Action != "add"))
            {
                continue;
            }

            mods.Add(Change("update", previous, next));
            consumed.Add(previous.Action.Path);
            consumed.Add(next.Action.Path);
        }

        foreach (var file in after.OfType<ModFile>().Where(file => file.Action.Action == "add"))
        {
            if (!consumed.Contains(file.Action.Path) && newGroups[file.Key].Length == 1 && !oldGroups.ContainsKey(file.Key))
            {
                mods.Add(Change("add", null, file));
                consumed.Add(file.Action.Path);
            }
        }

        foreach (var file in before.OfType<ModFile>().Where(file => file.Action.Action == "delete"))
        {
            if (!consumed.Contains(file.Action.Path) && oldGroups[file.Key].Length == 1 && !newGroups.ContainsKey(file.Key))
            {
                mods.Add(Change("delete", file, null));
                consumed.Add(file.Action.Path);
            }
        }

        var files = changed.Where(action => !consumed.Contains(action.Path)).ToArray();
        return new(mods, files, [.. warnings], mods.Count(mod => mod.Action == "update"),
            mods.Count(mod => mod.Action == "add"), mods.Count(mod => mod.Action == "delete"), files.Length);
    }

    private static PackModChange Change(string action, ModFile? before, ModFile? after)
    {
        var mod = after ?? before!;
        var files = new List<PackFileChange>();
        if (before is not null) files.Add(before.Action);
        if (after is not null && !ReferenceEquals(before?.Action, after.Action)) files.Add(after.Action);
        return new(action, mod.Id, mod.Name, before?.Version, after?.Version, before?.Action.Path, after?.Action.Path, files);
    }

    private static async Task<ModFile?> ReadAsync(string root, PackFileChange action, ConcurrentQueue<PackMetadataWarning> warnings, CancellationToken ct)
    {
        var relative = action.Path;
        try
        {
            var description = await ContentService.InspectSourceAsync("mods", PackFiles.Safe(root, relative), ct).ConfigureAwait(false);
            if (description.MetadataWarning is { } warning)
            {
                warnings.Enqueue(new(relative, warning));
                return null;
            }

            var format = description.MetadataFormat;
            var metadata = description.Metadata;
            var loader = string.Empty;
            JsonObject? mod = null;
            if (format == "fabric.mod.json") { loader = "fabric"; mod = metadata as JsonObject; }
            else if (format == "quilt.mod.json") { loader = "quilt"; mod = (metadata as JsonObject)?["quilt_loader"] as JsonObject; }
            else if (format is "META-INF/mods.toml" or "META-INF/neoforge.mods.toml")
            {
                loader = format.Contains("neoforge", StringComparison.Ordinal) ? "neoforge" : "forge";
                if (metadata?["mods"] is JsonArray { Count: 1 } declared) mod = declared[0] as JsonObject;
            }
            else if (format == "mcmod.info")
            {
                loader = "forge";
                var declared = metadata as JsonArray ?? (metadata as JsonObject)?["modList"] as JsonArray;
                if (declared is { Count: 1 }) mod = declared[0] as JsonObject;
            }

            static string? Text(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
            var id = Text(mod?["id"]) ?? Text(mod?["modId"]) ?? Text(mod?["modid"]);
            if (string.IsNullOrWhiteSpace(id)) return null;
            var name = Text(mod?["name"]) ?? Text(mod?["displayName"]) ?? Text((mod?["metadata"] as JsonObject)?["name"]) ?? id;
            var version = mod?["version"]?.ToString();
            if (version?.Contains("${", StringComparison.Ordinal) == true) version = null;
            return new(loader + ":" + id, id, name, version, action);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or LauncherOperationException)
        {
            warnings.Enqueue(new(relative, exception.Message));
            return null;
        }
    }
}