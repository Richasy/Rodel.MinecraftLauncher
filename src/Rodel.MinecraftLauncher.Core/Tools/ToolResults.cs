// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tools;

/// <summary>Bounded tool output and retained-result navigation, independent of MCP transport.</summary>
internal sealed class ToolResults(string dataDirectory)
{
    private const int MaximumInlineCharacters = 24000;
    private string ResultsDirectory => Path.Combine(dataDirectory, "ai", "tool-results");

    /// <summary>Preserve a full tool result for later result_read, including when a model host compacts old history.</summary>
    public async Task<string> SaveResultAsync(ToolResult result, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(ResultsDirectory);
        await File.WriteAllTextAsync(Path.Combine(ResultsDirectory, id + ".json"), JsonSerializer.Serialize(result, ToolJsonContext.Default.ToolResult), ct).ConfigureAwait(false);
        // Expiration is opportunistic cleanup; an unrelated old file must not hide a newly saved result.
        try
        {
            foreach (var file in Directory.EnumerateFiles(ResultsDirectory, "*.json"))
            {
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return id;
    }

    /// <summary>Wraps completed operation data in a success envelope, copying existing payload warnings.</summary>
    /// <param name="operation">The operation that actually completed.</param>
    /// <param name="data">Its result payload, retained without ownership transfer or cloning.</param>
    /// <returns>The success envelope with independent copies of recognized warnings.</returns>
    public static ToolResult Success(string operation, JsonNode? data)
    {
        var warnings = (data as JsonObject)?["warnings"] is JsonArray existing ? (JsonArray)existing.DeepClone() : [];
        if ((data as JsonObject)?["warning"] is { } warning)
            warnings.Add(warning.DeepClone());
        return new(1, operation, LauncherResultStatus.Succeeded, data, warnings, null);
    }

    /// <summary>Compacts recognized search hits while retaining the full upstream result for later reads.</summary>
    /// <param name="original">The provider response; returned unchanged for unrecognized shapes, or with a warning if retention fails.</param>
    /// <param name="ct">Requests cancellation while saving the full search result.</param>
    /// <returns>A summary with a continuation, or the full provider data when safe compaction is unavailable.</returns>
    public async Task<JsonObject> SummarizeSearchAsync(JsonObject original, CancellationToken ct)
    {
        if (original["hits"] is not JsonArray hits)
            return original; // Retain other provider shapes as supplied.
        var summary = (JsonObject)original.DeepClone();
        var items = new JsonArray();
        foreach (var hit in hits)
        {
            if (hit is not JsonObject source)
            {
                items.Add(hit?.DeepClone());
                continue;
            }

            var item = new JsonObject();
            foreach (var key in new[]
            {
                "project_id",
                "slug",
                "title",
                "description",
                "project_type",
                "author",
                "categories",
                "display_categories",
                "latest_version",
                "client_side",
                "server_side",
                "downloads",
                "follows",
                "license",
                "date_created",
                "date_modified"
            }

            )
            {
                if (source[key] is { } field)
                    item[key] = field.DeepClone();
            }

            if (source["versions"] is JsonArray versions)
                item["supportedVersionCount"] = versions.Count;
            items.Add((JsonNode)item);
        }

        summary["hits"] = items;
        summary["summary"] = true;
        summary["selectionNote"] = "Match project identity and description to the user's goal. latest_version alone does not prove compatibility; content_plan resolves the requested Minecraft/loader combination. Full supported versions and other upstream fields remain available in fullResult.";
        try
        {
            var saved = await SaveResultAsync(Success("content_search", original.DeepClone()), ct).ConfigureAwait(false);
            summary["fullResult"] = ProtocolJson.Object(new ToolSavedResult(saved, ProtocolJson.Read(saved, "/data")), ToolJsonContext.Default.ToolSavedResult);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If continuation cannot be persisted, retain all upstream data instead of silently dropping fields.
            original["warning"] = "Could not save the compact search's full result; returning original upstream fields: " + ex.Message;
            return original;
        }

        return summary;
    }

    internal static JsonObject Page(JsonNode? value, JsonObject input, string tool) =>
        ProtocolJson.Object(CreatePage(value, input, tool), ToolJsonContext.Default.ToolPage);

    private static ToolPage CreatePage(JsonNode? value, JsonObject input, string tool)
    {
        if (value is not JsonArray array)
            throw new InvalidDataException("Expected a list result from " + tool);
        var offset = Number(input, "offset", 0, 0, int.MaxValue);
        var limit = Number(input, "limit", 20, 1, 100);
        var returned = array.Skip(offset).Take(limit).Select(n => n?.DeepClone()).ToArray();
        var next = offset + returned.Length;
        var args = (JsonObject)input.DeepClone();
        args["offset"] = next;
        args["limit"] = limit;
        return new([.. returned], offset, limit, array.Count, next < array.Count,
            next < array.Count ? next : null, next < array.Count ? ProtocolJson.Continue(tool, args) : null);
    }

    /// <summary>Bounds oversized output and attempts to retain its full envelope without changing the completed operation status.</summary>
    /// <param name="result">The actual completed outcome, including failures and cancellation.</param>
    /// <param name="input">Normalized list arguments, retained when the output budget reduces a page.</param>
    /// <returns>The original inline result or a summary with a saved-result continuation or explicit retention warning.</returns>
    public async Task<ToolResult> BoundAsync(ToolResult result, JsonObject? input = null)
    {
        if (ToolImages.Contains(result.Data))
        {
            string? saved = null;
            try
            {
                saved = await SaveResultAsync(result, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add((JsonNode?)JsonValue.Create("Images were omitted, but their snapshot could not be saved: " + ex.Message));
            }

            result = result with { Data = ToolImages.Project(result.Data, saved, "/data") };
        }

        if (result.Data is JsonObject list && result.Operation is { } operation)
        {
            await SummarizeListAsync(list, operation, result.Warnings).ConfigureAwait(false);
            if (operation == "context")
            {
                foreach (var (section, tool) in new[] { ("instances", "instance_list"), ("mods", "content_list"), ("jobs", "job_list"), ("sessions", "process_list") })
                {
                    if (list[section] is JsonObject nested)
                        await SummarizeListAsync(nested, tool, result.Warnings).ConfigureAwait(false);
                }
            }
        }

        if (input is not null && result.Operation is "instance_list" or "content_list" or "job_list" or "process_list"
            && result.Data is JsonObject page && page["items"] is JsonArray items && items.ToJsonString().Length > 16000)
        {
            try
            {
                var saved = await SaveResultAsync(result, CancellationToken.None).ConfigureAwait(false);
                var offset = page["offset"]!.GetValue<int>();
                var returned = new JsonArray();
                var characters = 0;
                foreach (var item in items)
                {
                    var clone = item?.DeepClone();
                    var length = clone?.ToJsonString().Length ?? 4;
                    if (length > 12000)
                    {
                        var pointer = "/data/items/" + returned.Count.ToString(CultureInfo.InvariantCulture);
                        clone = ProtocolJson.Object(new ToolItemReference(true, pointer, ProtocolJson.Read(saved, pointer)), ToolJsonContext.Default.ToolItemReference);
                        length = clone.ToJsonString().Length;
                    }

                    if (returned.Count > 0 && characters + length > 16000)
                        break;
                    returned.Add(clone);
                    characters += length;
                }

                var next = offset + returned.Count;
                var hasMore = next < page["total"]!.GetValue<int>();
                var arguments = (JsonObject)input.DeepClone();
                arguments["offset"] = next;
                page["items"] = returned;
                page["hasMore"] = hasMore;
                page["nextOffset"] = hasMore ? next : null;
                page["continuation"] = hasMore ? ProtocolJson.Object(ProtocolJson.Continue(result.Operation, arguments), ToolJsonContext.Default.ToolContinuation) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add((JsonNode?)JsonValue.Create("Could not retain an oversized list page; returning bounded navigation instead: " + ex.Message));
            }
        }

        var json = JsonSerializer.Serialize(result, ToolJsonContext.Default.ToolResult);
        if (json.Length <= MaximumInlineCharacters)
            return result;
        var data = ProtocolJson.Object(new ToolTruncatedData(true, null, json.Length, SafeSlice(json, 0, 2000), null), ToolJsonContext.Default.ToolTruncatedData);
        var summary = result with
        {
            Data = data,
            Warnings = new JsonArray("Tool output exceeded the inline budget. Read only fields needed for the current task using result_read JSON Pointers; omitted fields remain in the saved result.")
        };
        foreach (var warning in result.Warnings.OfType<JsonObject>().Where(w => w["code"]?.GetValue<string>() == "IGNORED_ARGUMENTS"))
            summary.Warnings.Add(warning.DeepClone());
        if (result.Data is JsonObject objectData)
        {
            var properties = PropertyNames(objectData, 0, out var nextProperty);
            ProtocolJson.Extend(data, new ToolTruncatedNavigation(properties, objectData.Count, nextProperty,
                "Call result_read with pointer /data or /data/PROPERTY to inspect specific fields."), ToolJsonContext.Default.ToolTruncatedNavigation);
        }

        if (summary.Error is { Message.Length: > 1000 } error)
            summary = summary with { Error = error with { Message = SafeSlice(error.Message, 0, 1000) + "… (full message in saved result)" } };
        try
        {
            // The operation already completed. A late caller cancellation must not erase its outcome.
            var id = await SaveResultAsync(result, CancellationToken.None).ConfigureAwait(false);
            data["resultId"] = id;
            data["continuation"] = ProtocolJson.Object(ProtocolJson.Read(id), ToolJsonContext.Default.ToolContinuation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            summary.Warnings.Add((JsonNode?)JsonValue.Create("The operation outcome above is retained, but its full output could not be saved: " + ex.Message));
        }

        return summary;
    }

    private async Task SummarizeListAsync(JsonObject page, string tool, JsonArray warnings)
    {
        string[] fields = tool switch
        {
            "instance_list" => ["id", "name", "gameDirectory", "minecraftDirectory", "versionId", "managed", "status", "error", "icon"],
            "content_list" => ["fileName", "path", "kind", "enabled", "isContent", "formatRecognized", "directory", "sha1", "size", "archiveReadable", "metadata", "metadataFormat", "activeDirectory", "activeFileName", "platform", "projectId", "versionId", "gameVersion", "loader", "pinned", "channel", "title", "iconUrl", "dependency", "provides", "requestedBy"],
            "job_list" => ["id", "status", "createdAt", "finishedAt", "canResume", "resumedFrom", "activityUnknown", "error", "progress", "request"],
            "process_list" => ["id", "instanceId", "jobId", "status", "readiness", "processId", "createdAt", "exitedAt", "versionId", "gameDirectory", "exitCode", "server", "activityUnknown", "errorCode", "error"],
            _ => []
        };
        if (fields.Length == 0 || page["items"] is not JsonArray items || items.Count == 0)
            return;
        try
        {
            var saved = await SaveResultAsync(Success(tool, page.DeepClone()), CancellationToken.None).ConfigureAwait(false);
            foreach (var item in items.OfType<JsonObject>())
            {
                var metadata = tool == "content_list" ? item["metadata"] as JsonObject : null;
                var request = tool == "job_list" ? item["request"] as JsonObject : null;
                var identity = metadata is null ? [] : ContentIdentity(metadata);
                foreach (var field in item.Select(property => property.Key).ToArray())
                {
                    if (item[field] is null || (!fields.Contains(field, StringComparer.Ordinal)
                        && !field.EndsWith("Warning", StringComparison.OrdinalIgnoreCase)
                        && field is not ("warning" or "warnings")))
                    {
                        item.Remove(field);
                    }
                }

                item.Remove("metadata");
                item.Remove("request");
                if (identity.Count > 0)
                    item["metadata"] = identity;
                if (request is not null)
                {
                    var parameters = new JsonObject();
                    if (request["parameters"] is JsonObject originalParameters)
                    {
                        foreach (var key in new[] { "instanceId", "sessionId", "kind", "projectId" })
                        {
                            if (originalParameters[key] is { } value)
                                parameters[key] = value.DeepClone();
                        }
                    }

                    item["request"] = new JsonObject { ["operation"] = request["operation"]?.DeepClone(), ["parameters"] = parameters };
                }
            }

            page["summary"] = true;
            page["fullResult"] = ProtocolJson.Object(new ToolSavedResult(saved, ProtocolJson.Read(saved, "/data")), ToolJsonContext.Default.ToolSavedResult);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add((JsonNode?)JsonValue.Create("Could not retain list details; original fields remain available inline: " + ex.Message));
        }
    }

    private static JsonObject ContentIdentity(JsonObject metadata)
    {
        var result = new JsonObject();
        foreach (var key in new[] { "id", "name", "version", "modId", "displayName", "environment", "depends", "breaks", "provides", "modLoader", "loaderVersion" })
        {
            if (metadata[key] is { } value)
                result[key] = value.DeepClone();
        }

        if (metadata["mods"] is JsonArray mods)
            result["mods"] = new JsonArray([.. mods.Select(mod => mod is JsonObject obj ? ContentIdentity(obj) : mod?.DeepClone())]);
        if (metadata["quilt_loader"] is JsonObject quilt)
            result["quilt_loader"] = ContentIdentity(quilt);
        if (metadata["metadata"] is JsonObject information && information["name"] is { } name)
            result["metadata"] = new JsonObject { ["name"] = name.DeepClone() };
        return result;
    }

    /// <summary>Reads a retained snapshot with JSON Pointer selection and bounded array, text, or property navigation.</summary>
    /// <param name="input">The saved-result identifier and validated navigation options.</param>
    /// <param name="ct">Requests cancellation while reading the retained file.</param>
    /// <returns>A navigation result that never re-executes the original operation; missing snapshots report a recoverable error.</returns>
    public async Task<JsonObject> ReadResultAsync(JsonObject input, CancellationToken ct)
    {
        var id = Required(input, "resultId");
        var snapshot = await LoadResultAsync(id, ct).ConfigureAwait(false);
        var pointer = Text(input, "pointer") ?? string.Empty;
        var value = Select(ToolImages.Project(snapshot, id, string.Empty), pointer);

        if (value is JsonArray array)
        {
            var page = CreatePage(value, input, "result_read") with { ResultId = id, Pointer = pointer };
            if (JsonSerializer.Serialize(page, ToolJsonContext.Default.ToolPage).Length > MaximumInlineCharacters - 2000)
            {
                var start = Number(input, "offset", 0, 0, int.MaxValue);
                var items = new JsonArray();
                foreach (var item in page.Items)
                {
                    var clone = item?.DeepClone();
                    if ((clone?.ToJsonString().Length ?? 4) > 12000)
                    {
                        var itemPointer = pointer + "/" + (start + items.Count).ToString(CultureInfo.InvariantCulture);
                        clone = ProtocolJson.Object(new ToolItemReference(true, itemPointer, ProtocolJson.Read(id, itemPointer)), ToolJsonContext.Default.ToolItemReference);
                    }

                    if (items.Count > 0 && items.ToJsonString().Length + (clone?.ToJsonString().Length ?? 4) > 16000)
                        break;
                    items.Add(clone);
                }

                var nextItem = start + items.Count;
                var nextArgs = (JsonObject)input.DeepClone();
                nextArgs["offset"] = nextItem;
                page = page with
                {
                    Items = items,
                    HasMore = nextItem < array.Count,
                    NextOffset = nextItem < array.Count ? nextItem : null,
                    Continuation = nextItem < array.Count ? ProtocolJson.Continue("result_read", nextArgs) : null
                };
            }

            return ProtocolJson.Object(page, ToolJsonContext.Default.ToolPage);
        }

        var text = value?.ToJsonString() ?? "null";
        var offset = Number(input, "offset", 0, 0, int.MaxValue);
        if (offset > text.Length)
            throw new ArgumentException("offset is beyond the saved result.");
        var count = Number(input, "characters", 8000, 256, 8000);
        var chunk = SafeSlice(text, offset, count);
        var next = offset + chunk.Length;
        var arguments = (JsonObject)input.DeepClone();
        arguments["offset"] = next;
        var result = ProtocolJson.Object(new ToolTextChunk(id, pointer, chunk, offset, next < text.Length ? next : null,
            next < text.Length, text.Length, next < text.Length ? ProtocolJson.Continue("result_read", arguments) : null), ToolJsonContext.Default.ToolTextChunk);
        if (value is JsonObject obj)
        {
            var propertyOffset = Number(input, "propertyOffset", 0, 0, int.MaxValue);
            var properties = PropertyNames(obj, propertyOffset, out var nextProperty);
            ProtocolJson.Extend(result, new ToolObjectNavigation(properties, obj.Count, propertyOffset, nextProperty), ToolJsonContext.Default.ToolObjectNavigation);
        }

        return result;
    }

    internal async Task<JsonNode?> LoadResultAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid saved result identifier.");
        var path = Path.Combine(ResultsDirectory, id + ".json");
        if (!File.Exists(path))
            throw new LauncherOperationException("RESULT_NOT_FOUND", "Saved result is missing or expired. Re-read current state; do not repeat a mutation solely to recover its output.");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
    }

    internal static JsonNode? Select(JsonNode? value, string pointer)
    {
        if (pointer.Length > 0)
        {
            if (!pointer.StartsWith('/'))
                throw new ArgumentException("pointer must be empty or start with '/'.");
            foreach (var raw in pointer[1..].Split('/'))
            {
                var key = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                value = value switch
                {
                    JsonObject currentObject when currentObject.TryGetPropertyValue(key, out var child) => child,
                    JsonArray currentArray when int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < currentArray.Count => currentArray[index],
                    _ => throw new ArgumentException("JSON pointer does not exist: " + pointer)
                };
            }
        }

        return value;
    }

    private static JsonArray PropertyNames(JsonObject value, int offset, out int? next)
    {
        var names = new JsonArray();
        var characters = 0;
        foreach (var key in value.Select(p => p.Key).Skip(offset).Take(100))
        {
            // Ordinary field names fit comfortably; unusual long keys remain accessible in text chunks.
            var encoded = JsonValue.Create(key)!.ToJsonString();
            if (characters + encoded.Length > 4000)
                break;
            names.Add((JsonNode?)JsonValue.Create(key));
            characters += encoded.Length;
        }

        next = offset + names.Count < value.Count ? offset + Math.Max(1, names.Count) : null;
        return names;
    }

    private static string SafeSlice(string text, int offset, int count)
    {
        var length = Math.Min(count, text.Length - offset);
        if (length > 0 && offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1]))
            length--;
        return text.Substring(offset, length);
    }

    private static string? Text(JsonObject input, string name) => input[name] is null ? null : input[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new ArgumentException(name + " must be a string.");
    private static string Required(JsonObject input, string name) => Text(input, name) ?? throw new ArgumentException("Missing " + name + ".");
    private static int Number(JsonObject input, string name, int fallback, int min, int max)
    {
        if (input[name] is null)
            return fallback;
        if (input[name] is JsonValue value && value.TryGetValue<int>(out var result) && result >= min && result <= max)
            return result;
        throw new ArgumentException($"{name} must be an integer from {min} through {max}.");
    }

}