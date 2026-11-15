// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Reads standard version metadata, retaining extension fields and merging inheritance.</summary>
public sealed class VersionResolver
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Retain the public service instance API for callers and future instance configuration.")]
    public async Task<JsonObject> ResolveAsync(string minecraftDirectory, string versionId, CancellationToken ct = default)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task<JsonObject> Read(string id)
        {
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or "..")
                throw new InvalidDataException($"Invalid version ID: {id}");
            if (!active.Add(id))
                throw new InvalidDataException($"Version inheritance cycle at '{id}'.");
            var path = Path.Combine(minecraftDirectory, "versions", id, id + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Version metadata '{id}' is missing.", path);
            var stream = File.OpenRead(path);
            await using var streamDisposal = stream.ConfigureAwait(false);
            var child = (await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false)) as JsonObject ?? throw new InvalidDataException($"Version metadata '{id}' must be an object.");
            JsonObject result;
            var parentId = child["inheritsFrom"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(parentId))
            {
                result = await Read(parentId).ConfigureAwait(false);
                Merge(result, child);
            }
            else
            {
                result = (JsonObject)child.DeepClone();
                result["_minecraftVersion"] = child["id"]?.DeepClone() ?? JsonValue.Create(id);
            }

            // Keep the jar owner separately: a loader's id does not change its inherited client jar.
            if (child["jar"] is JsonValue jar)
                result["_clientJarVersion"] = jar.DeepClone();
            else if (child["downloads"]?["client"] is not null || parentId is null)
                result["_clientJarVersion"] = id;
            active.Remove(id);
            return result;
        }

        return await Read(versionId).ConfigureAwait(false);
    }

    private static void Merge(JsonObject parent, JsonObject child)
    {
        foreach (var (key, value) in child)
        {
            if (key == "libraries" && value is JsonArray libraries)
            {
                var combined = parent[key] as JsonArray ?? [];
                foreach (var library in libraries)
                {
                    // The child's coordinate replaces the same group/artifact/classifier from a parent.
                    var name = library?["name"]?.GetValue<string>();
                    var identity = LibraryIdentity(name);
                    for (var i = combined.Count - 1; i >= 0; i--)
                    {
                        if (identity is not null && LibraryIdentity(combined[i]?["name"]?.GetValue<string>()) == identity)
                            combined.RemoveAt(i);
                    }

                    combined.Add(library?.DeepClone());
                }

                if (parent[key] is null)
                    parent[key] = combined;
            }
            else if (key == "arguments" && value is JsonObject arguments)
            {
                var merged = parent[key] as JsonObject ?? [];
                foreach (var (kind, list) in arguments)
                {
                    if (list is JsonArray additions)
                    {
                        var destination = merged[kind] as JsonArray ?? [];
                        foreach (var item in additions)
                            destination.Add(item?.DeepClone());
                        if (merged[kind] is null)
                            merged[kind] = destination;
                    }
                    else
                    {
                        merged[kind] = list?.DeepClone();
                    }
                }

                if (parent[key] is null)
                    parent[key] = merged;
            }
            else if (value is JsonObject nested && parent[key] is JsonObject existing)
            {
                Merge(existing, nested);
            }
            else
            {
                parent[key] = value?.DeepClone();
            }
        }
    }

    private static string? LibraryIdentity(string? name)
    {
        if (name is null)
            return null;
        var parts = name.Split(':');
        return parts.Length >= 3 ? $"{parts[0]}:{parts[1]}:{(parts.Length > 3 ? parts[3] : string.Empty)}" : name;
    }
}