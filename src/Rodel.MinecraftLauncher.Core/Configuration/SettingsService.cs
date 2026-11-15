// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Configuration;

public sealed class SettingsService(LauncherPaths paths)
{
    /// <summary>Merges default, global, and instance settings while retaining unknown values and recoverable resolution warnings.</summary>
    public EffectiveSettings GetEffective(InstanceRecord? instance = null)
    {
        var values = new JsonObject
        {
            ["minMemoryMb"] = 512,
            ["maxMemoryMb"] = 2048,
            ["memoryMode"] = "auto",
            ["gameBridgeEnabled"] = true
        };
        var sources = new JsonObject
        {
            ["minMemoryMb"] = "default",
            ["maxMemoryMb"] = "default",
            ["memoryMode"] = "default",
            ["gameBridgeEnabled"] = "default"
        };
        var warnings = new JsonArray();
        Merge(values, sources, ReadGlobal(), "global");
        if (instance is not null)
        {
            var overrides = JsonFiles.ReadObject(instance.MetadataPath)["settings"] as JsonObject ?? [];
            if (overrides["javaRuntimeId"] is not null && overrides["javaPath"] is null)
            {
                values.Remove("javaPath");
                values.Remove("javaRuntimeExecutable");
                sources.Remove("javaPath");
                sources.Remove("javaRuntimeExecutable");
            }

            Merge(values, sources, overrides, "instance");
        }

        if (sources["memoryMode"]?.ToString() == "default" && sources["maxMemoryMb"]?.ToString() is "global" or "instance")
        {
            values["memoryMode"] = "manual";
            sources["memoryMode"] = sources["maxMemoryMb"]!.DeepClone();
        }

        if (values["javaPath"] is null && values["javaRuntimeId"] is { } reference)
        {
            try
            {
                values["javaPath"] = new Rodel.MinecraftLauncher.Core.Java.JavaRuntimeService(paths).ResolveJavaPath(reference.GetValue<string>(), values["javaRuntimeExecutable"]?.GetValue<string>());
                sources["javaPath"] = "managed-runtime";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
            {
                var runtimeId = reference is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
                warnings.Add((JsonNode)new JsonObject
                {
                    ["code"] = "JAVA_RUNTIME_UNAVAILABLE",
                    ["runtimeId"] = runtimeId,
                    ["source"] = sources["javaRuntimeId"]?.DeepClone(),
                    ["message"] = $"Selected managed Java runtime '{runtimeId ?? "invalid reference"}' could not be resolved: {exception.Message}",
                    ["recovery"] = "Select a usable Java path, repair the runtime registration, or explicitly remove this Java choice to enable automatic discovery."
                });
            }
        }

        return new(values, sources, warnings);
    }

    /// <summary>Reads one effective value and its origin without dropping configuration warnings.</summary>
    public SettingValue Get(string key, InstanceRecord? instance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var effective = GetEffective(instance);
        return new(key, effective.Values[key]?.DeepClone(), effective.Sources[key]?.GetValue<string>() ?? "unset",
            (JsonArray)effective.Warnings.DeepClone());
    }

    public Task SetAsync(string key, JsonNode? value, InstanceRecord? instance = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return UpdateAsync(new JsonObject { [key] = value?.DeepClone() }, instance: instance, cancellationToken: cancellationToken);
    }

    /// <summary>Removes an override so that the global or default value is inherited again.</summary>
    public Task RemoveAsync(string key, InstanceRecord? instance = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return UpdateAsync([], [key], instance, cancellationToken);
    }

    /// <summary>Reads stored overrides without resolving external Java registrations or merging defaults.</summary>
    public JsonObject GetOverrides(InstanceRecord? instance = null)
    {
        if (instance is null) return ReadGlobal();
        var value = JsonFiles.ReadObject(instance.MetadataPath)["settings"];
        return value is null ? [] : value.DeepClone() as JsonObject ?? throw new InvalidDataException("Instance settings must be a JSON object.");
    }

    /// <summary>Atomically edits only the supplied fields, preserving unrelated and unknown settings.</summary>
    public Task UpdateAsync(JsonObject changes, IReadOnlyCollection<string>? removeKeys = null,
        InstanceRecord? instance = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var key in changes.Select(pair => pair.Key).Concat(removeKeys ?? [])) ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (changes.ContainsKey("javaPath") && changes.ContainsKey("javaRuntimeId"))
            throw new ArgumentException("Choose either a Java path or a managed runtime reference.", nameof(changes));
        var edits = (JsonObject)changes.DeepClone();
        var removed = removeKeys?.ToArray() ?? [];
        return EditAsync(instance, settings =>
        {
            foreach (var key in removed) settings.Remove(key);
            if (edits.ContainsKey("javaPath"))
            {
                settings.Remove("javaRuntimeId");
                settings.Remove("javaRuntimeExecutable");
            }

            if (edits.ContainsKey("javaRuntimeId"))
            {
                settings.Remove("javaPath");
                settings.Remove("javaRuntimeExecutable");
            }

            foreach (var pair in edits) settings[pair.Key] = pair.Value?.DeepClone();
        }, cancellationToken);
    }

    /// <summary>Atomically edits fields in an object section, retaining other fields from its latest stored version.</summary>
    public Task UpdateSectionAsync(string section, JsonObject changes, IReadOnlyCollection<string>? removeKeys = null,
        InstanceRecord? instance = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentNullException.ThrowIfNull(changes);
        var edits = (JsonObject)changes.DeepClone();
        var removed = removeKeys?.ToArray() ?? [];
        foreach (var key in edits.Select(pair => pair.Key).Concat(removed)) ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return EditAsync(instance, settings =>
        {
            if (settings[section] is not null and not JsonObject)
                throw new InvalidDataException("The settings section must be a JSON object.");
            var values = settings[section] as JsonObject ?? [];
            foreach (var key in removed) values.Remove(key);
            foreach (var pair in edits) values[pair.Key] = pair.Value?.DeepClone();
            if (settings[section] is null) settings[section] = values;
        }, cancellationToken);
    }

    internal Task UpdateCheckedAsync(JsonObject changes, IReadOnlyCollection<string> removeKeys, InstanceRecord instance,
        JsonObject expectedOverrides, CancellationToken ct) => EditAsync(instance, settings =>
        {
            if (!JsonNode.DeepEquals(settings, expectedOverrides))
                throw new Application.LauncherOperationException("REPAIR_PLAN_STALE", "Instance settings changed before the repair write. New values were preserved.");
            foreach (var key in removeKeys) settings.Remove(key);
            if (changes.ContainsKey("javaPath")) { settings.Remove("javaRuntimeId"); settings.Remove("javaRuntimeExecutable"); }

            foreach (var pair in changes) settings[pair.Key] = pair.Value?.DeepClone();
        }, ct);

    private async Task EditAsync(InstanceRecord? instance, Action<JsonObject> edit, CancellationToken cancellationToken)
    {
        var file = instance?.MetadataPath ?? paths.SettingsFile;
        using var lease = await ResourceLease.AcquireAsync(resource: file, cancellationToken: cancellationToken).ConfigureAwait(false);
        var document = File.Exists(file) ? JsonFiles.ReadObject(file) : [];
        JsonObject settings;
        if (instance is null)
        {
            settings = document;
        }
        else
        {
            if (document["settings"] is not null and not JsonObject)
                throw new InvalidDataException("Instance settings must be a JSON object.");
            settings = document["settings"] as JsonObject ?? [];
            if (document["settings"] is null)
                document["settings"] = settings;
        }

        edit(settings);
        await JsonFiles.WriteAsync(file, document, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject ReadGlobal() => File.Exists(paths.SettingsFile) ? JsonFiles.ReadObject(paths.SettingsFile) : [];
    private static void Merge(JsonObject values, JsonObject sources, JsonObject overrides, string source)
    {
        foreach (var entry in overrides)
        {
            values[entry.Key] = entry.Value?.DeepClone();
            sources[entry.Key] = source;
        }
    }
}