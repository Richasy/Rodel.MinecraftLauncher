// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Controls.Versions;

/// <summary>Represents one version or a nonselectable group boundary from the shared Core catalog.</summary>
/// <param name="Value">The exact version ID, or the stable group key for a heading.</param>
/// <param name="Group">The group derived from the manifest's declared version type.</param>
/// <param name="Header">Whether this row labels a group rather than offering a version.</param>
internal sealed record GameVersionRow(string Value, string Group, bool Header);

/// <summary>Groups the existing catalog without fetching a second list or inventing version-type rules from names.</summary>
internal sealed class GameVersionChoices
{
    internal GameVersionChoices(VersionCatalog? catalog)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var versions = (catalog?.Versions ?? []).OfType<JsonObject>()
            .Where(item => item["id"]?.ToString() is { Length: > 0 } id && seen.Add(id))
            .Select(item => new GameVersionRow(item["id"]!.ToString(), GroupOf(item["type"]?.ToString()), false))
            .ToArray();
        var rows = new List<GameVersionRow>();
        foreach (var group in new[] { "release", "snapshot", "historical", "other" })
        {
            var entries = versions.Where(item => item.Group == group).ToArray();
            if (entries.Length == 0)
                continue;
            rows.Add(new(group, group, true));
            rows.AddRange(entries);
        }

        Rows = rows;
    }

    internal IReadOnlyList<GameVersionRow> Rows { get; }

    internal string? VersionAt(int index) => index >= 0 && index < Rows.Count && !Rows[index].Header ? Rows[index].Value : null;

    internal bool GroupMatches(string group, string query) => Rows.Any(row => !row.Header && row.Group == group && row.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string GroupOf(string? type) => type switch
    {
        "release" => "release",
        "snapshot" => "snapshot",
        "old_alpha" or "old_beta" => "historical",
        _ => "other",
    };
}