// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Starts or stops using one installed resource pack, preserving other selections and restoring its file when explicitly selected.</summary>
    public async Task<ResourcePackSelectionResult> SetResourcePackUsageAsync(string instanceId, string fileName, bool use,
        string? expectedFingerprint = null, CancellationToken cancellationToken = default)
    {
        EnsureInstanceIdle(instanceId);
        var current = await ReadResourcePacksAsync(instanceId, null, cancellationToken).ConfigureAwait(false);
        if (expectedFingerprint is not null && current.Fingerprint != expectedFingerprint)
            throw new LauncherOperationException("GAME_SETTINGS_CHANGED", "The resource-pack selection changed; refresh before applying this operation.");
        var entry = current.Installed.FirstOrDefault(item => item.FileName == fileName)
            ?? throw new FileNotFoundException("The selected resource pack is not installed in this instance.", fileName);
        if (use && !entry.Enabled)
        {
            await Content.SetEnabledAsync(Instances.Get(instanceId).GameDirectory, "resourcepacks", entry.FileName, true, ct: cancellationToken).ConfigureAwait(false);
            current = await ReadResourcePacksAsync(instanceId, null, cancellationToken).ConfigureAwait(false);
        }

        var activeName = entry.Enabled ? entry.FileName : entry.ActiveFileName ?? (entry.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? entry.FileName[..^9] : entry.FileName);
        var names = current.Selected.Select(value => value!.ToString()).Where(value => value != activeName && value != "file/" + activeName
            && value != fileName && value != "file/" + fileName).ToList();
        if (use && current.Selected.Any(value => value?.ToString() == activeName || value?.ToString() == "file/" + activeName))
            names = [.. current.Selected.Select(value => value!.ToString())];
        else if (use) names.Add(activeName);
        return await ResourcePacksAsync(instanceId, new() { Names = names, Replace = true, ExpectedFingerprint = current.Fingerprint }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves one selected resource pack by one priority position while preserving built-in and unfamiliar selections.</summary>
    /// <param name="instanceId">The instance whose options are changed.</param>
    /// <param name="fileName">The installed selected file identity.</param>
    /// <param name="higherPriority">Whether to move toward the last-applied, higher-priority end.</param>
    /// <param name="expectedFingerprint">The options snapshot used to display this action.</param>
    /// <param name="cancellationToken">Cancels before publication.</param>
    public async Task<ResourcePackSelectionResult> MoveResourcePackAsync(string instanceId, string fileName, bool higherPriority,
        string? expectedFingerprint = null, CancellationToken cancellationToken = default)
    {
        EnsureInstanceIdle(instanceId);
        var current = await ReadResourcePacksAsync(instanceId, null, cancellationToken).ConfigureAwait(false);
        if (expectedFingerprint is not null && current.Fingerprint != expectedFingerprint)
            throw new LauncherOperationException("GAME_SETTINGS_CHANGED", "The resource-pack selection changed; refresh before changing priority.");
        var names = current.Selected.Select(value => value!.ToString()).ToList();
        var local = current.Installed.Select(item => item.FileName).ToHashSet(StringComparer.Ordinal);
        var positions = Enumerable.Range(0, names.Count).Where(index => local.Contains(names[index].StartsWith("file/", StringComparison.Ordinal) ? names[index][5..] : names[index])).ToArray();
        var actual = names.FindIndex(name => name == fileName || name == "file/" + fileName);
        var index = Array.IndexOf(positions, actual);
        var next = index + (higherPriority ? 1 : -1);
        if (index < 0) throw new ArgumentException("The resource pack is not currently selected.", nameof(fileName));
        if (next >= 0 && next < positions.Length) (names[positions[index]], names[positions[next]]) = (names[positions[next]], names[positions[index]]);
        return await ResourcePacksAsync(instanceId, new() { Names = names, Replace = true, ExpectedFingerprint = current.Fingerprint }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResourcePackSelectionSummary> ReadResourcePacksAsync(string instanceId, string? encoding, CancellationToken ct)
    {
        var target = Instances.Get(instanceId);
        var current = await GameSettings.GetAsync(target.GameDirectory, encoding, ct).ConfigureAwait(false);
        var installed = (await Content.ListAsync(target.GameDirectory, "resourcepacks", ct: ct).ConfigureAwait(false)).Where(entry => entry.IsContent).ToArray();
        var selected = ResourcePackEntries(current.Values, "resourcePacks");
        var states = selected.Select(entry =>
        {
            var name = entry?.ToString() ?? string.Empty;
            var fileName = name.StartsWith("file/", StringComparison.Ordinal) ? name[5..] : name;
            var match = installed.FirstOrDefault(item => item.FileName == fileName);
            return new ResourcePackSelectionState(name, match is not null, match?.Enabled,
                match is null ? "builtin-or-untracked-or-missing" : match.Enabled == false ? "disabled" : "available");
        }).ToArray();
        return new(installed, selected, states, current.Pending, current.PendingWarning, current.Warnings, current.Fingerprint);
    }

    private static JsonArray ResourcePackEntries(JsonObject values, string key)
    {
        try
        {
            return JsonNode.Parse(values[key]?.ToString() ?? "[]") as JsonArray ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ArgumentException("Current " + key + " is malformed; use game-settings set to supply the intended list.");
        }
    }

    private async Task<ResourcePackSelectionResult> ResourcePacksAsync(string instanceId, ResourcePackSelectionOptions options, CancellationToken ct)
    {
        var target = Instances.Get(instanceId);
        var service = new GameSettingsService(paths);
        var current = await service.GetAsync(target.GameDirectory, options.Encoding, ct).ConfigureAwait(false);
        var inventory = await new ContentService(paths).ListAsync(target.GameDirectory, "resourcepacks", ct: ct).ConfigureAwait(false);
        var content = inventory.Where(entry => entry.IsContent).ToArray();
        JsonArray Entries(string key) => ResourcePackEntries(current.Values, key);
        var selected = Entries("resourcePacks");

        var context = await GetContentContextAsync(target.Id, ct: ct).ConfigureAwait(false);
        var modern = System.Version.TryParse(context.MinecraftVersion, out var version) && version >= new System.Version(1, 13);
        var warnings = new List<string>();
        var names = new HashSet<string>(content.Select(x => x.FileName), StringComparer.Ordinal);
        var installedNames = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var item in content)
        {
            if (item.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                names.Add(item.FileName[..^9]);
            foreach (var alias in item.Tracking?["selectionAliases"] as JsonArray ?? [])
                names.Add(alias!.ToString());
        }

        string Format(string name) => modern && !name.StartsWith("file/", StringComparison.Ordinal) && name != "vanilla" && !(selected.Any(x => x?.ToString() == name) && !names.Contains(name)) ? "file/" + name : name;
        var requested = options.Names.Select(Format).Distinct(StringComparer.Ordinal).ToArray();
        var entries = options.Replace ? new List<string>() : [.. selected.Select(x => x!.ToString()).Where(x => !names.Contains(x.StartsWith("file/") ? x[5..] : x))];
        foreach (var name in requested)
        {
            var localName = name.StartsWith("file/", StringComparison.Ordinal) ? name[5..] : name;
            if (content.Any(x => x.FileName == localName && x.Enabled == false))
                warnings.Add("Selected pack is disabled; enable its file before the game can discover it: " + name);
            if (!installedNames.Contains(localName) && (!selected.Any(x => x?.ToString() == name) || names.Contains(localName)))
                warnings.Add("Selected entry is not an installed local pack; retained as explicitly requested: " + name);
            if (!entries.Contains(name, StringComparer.Ordinal))
                entries.Add(name);
        }

        var changes = new JsonObject
        {
            ["resourcePacks"] = new JsonArray([.. entries.Select(x => (JsonNode?)JsonValue.Create(x))]).ToJsonString()
        };
        if (options.AllowIncompatible)
            changes["incompatibleResourcePacks"] = new JsonArray([.. Entries("incompatibleResourcePacks").Select(x => x!.ToString()).Concat(requested).Distinct().Select(x => (JsonNode?)JsonValue.Create(x))]).ToJsonString();
        var result = await service.SetAsync(target.GameDirectory, changes, options.PlanOnly, options.ExpectedFingerprint ?? current.Fingerprint, options.Encoding, ct).ConfigureAwait(false);
        return new ResourcePackSelectionResult(result, entries, warnings);
    }
}