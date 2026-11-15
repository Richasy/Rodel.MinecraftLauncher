// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>A read-only view of known rule fields without deserializing or rewriting extensible version metadata.</summary>
internal readonly struct MetadataRuleReader
{
    private readonly JsonObject _rule;
    private readonly JsonObject? _operatingSystem;

    private MetadataRuleReader(JsonObject rule)
    {
        _rule = rule;
        _operatingSystem = rule["os"] as JsonObject;
    }

    /// <summary>Distinguishes an absent/non-array rule set from an explicit array, retaining only object rules.</summary>
    internal static IEnumerable<MetadataRuleReader>? ReadAll(JsonNode? rules) =>
        (rules as JsonArray)?.OfType<JsonObject>().Select(static rule => new MetadataRuleReader(rule));

    internal static bool HasArchitectureRestriction(JsonNode? rules) =>
        ReadAll(rules)?.Any(static rule => rule._operatingSystem?["arch"] is not null) == true;

    internal bool HasOperatingSystem() => _operatingSystem is not null;
    internal string? GetOperatingSystemName() => _operatingSystem?["name"]?.GetValue<string>();
    internal string? GetArchitecture() => _operatingSystem?["arch"]?.GetValue<string>();
    internal string? GetOperatingSystemVersionPattern() => _operatingSystem?["version"]?.GetValue<string>();
    internal string? GetAction() => _rule["action"]?.GetValue<string>();

    internal (string? Minimum, string? Maximum) GetOperatingSystemVersionRange()
    {
        var range = _operatingSystem?["versionRange"] as JsonObject;
        return (range?["min"]?.GetValue<string>(), range?["max"]?.GetValue<string>());
    }

    internal IEnumerable<KeyValuePair<string, bool>> GetRequiredFeatures()
    {
        if (_rule["features"] is not JsonObject features) yield break;
        foreach (var (name, value) in features)
            yield return new(name, value?.GetValue<bool>() ?? false);
    }
}