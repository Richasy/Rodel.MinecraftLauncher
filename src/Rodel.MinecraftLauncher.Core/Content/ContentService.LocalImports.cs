// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentService
{
    private async Task<ContentEntry?> CheckModImportAsync(string game, string source, CancellationToken ct)
    {
        var incoming = await InspectPackageAsync(source, ct).ConfigureAwait(false);
        var inventory = await ListAsync(game, "mods", includeHashes: false, ct: ct).ConfigureAwait(false);
        var matches = inventory.Where(item => ModIdentitiesOverlap(incoming, item)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length == 1 && incoming.Sha1 is not null
            && incoming.Sha1 == await Sha1Async(ResolveStoredPath(game, matches[0].Path), ct).ConfigureAwait(false))
        {
            matches[0].Sha1 = incoming.Sha1;
            matches[0].Reused = true;
            return matches[0];
        }

        throw new LauncherOperationException("CONTENT_MOD_ID_CONFLICT",
            "Installed packages declare the same mod ID. Review the local files to confirm a recoverable replacement; no duplicate was imported.",
            new JsonObject
            {
                ["modIds"] = new JsonArray([.. GetModIdentities(incoming.Metadata).Select(id => (JsonNode)JsonValue.Create(id)!)]),
                ["files"] = new JsonArray([.. matches.Select(item => (JsonNode)JsonValue.Create(item.FileName)!)])
            });
    }

    internal Task<ContentInstallReceipt> ApplyLocalModReplacementsAsync(string game, IReadOnlyList<ContentReviewItem> items,
        string expectedLock, CancellationToken ct)
    {
        var nodes = items.Select(item => new ContentPlanNode
        {
            Platform = "local",
            ProjectId = GetModIdentities(item.Inspection!.Metadata).First(),
            VersionId = item.Inspection.Metadata?["version"]?.ToString() ?? string.Empty,
            FileName = item.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? item.FileName[..^9] : item.FileName,
            Kind = "mods",
            Source = item.Source,
            Sha1 = item.Inspection.Sha1,
            Size = item.Inspection.Size,
            DesiredEnabled = !item.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && item.Replacements!.Any(entry => entry.Enabled),
            Upstream = item.Inspection.Metadata!.DeepClone().AsObject(),
        }).ToArray();
        return ApplyGroupAsync(game, nodes, expectedLock, nodes[0].ProjectId, ct, [.. items.SelectMany(item => item.Replacements!)]);
    }

    private async Task CheckModEnableAsync(string game, ContentEntry entry, CancellationToken ct)
    {
        var inventory = await ListAsync(game, "mods", includeHashes: false, ct: ct).ConfigureAwait(false);
        if (inventory.Any(item => item.Enabled && !string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)
            && ModIdentitiesOverlap(entry, item)))
        {
            throw new LauncherOperationException("CONTENT_MOD_ID_CONFLICT", "Another enabled package declares the same mod ID. Disable or replace it before enabling this file.");
        }
    }
}