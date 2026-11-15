// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentService
{
    private static IReadOnlyList<ResourcePackMapping> ResourceSelectionMappings(string kind, string source, string? destination) =>
        kind.Equals("resourcepacks", StringComparison.OrdinalIgnoreCase)
            ? [new ResourcePackMapping(Path.GetFileName(source), destination is null ? null : Path.GetFileName(destination))] : [];
    private async Task<GameSettingsChangeResult?> CoordinateResourceSelectionAsync(string game, string journal, JsonObject record, CancellationToken ct,
        IReadOnlyList<ResourcePackMapping>? plannedMappings = null)
    {
        if (record["selectionMappings"] is not JsonArray mappings || mappings.Count == 0 || record["selectionState"]?.ToString() is "applied" or "deferred")
            return null;
        GameSettingsChangeResult result;
        try
        {
            var changes = plannedMappings ?? [.. mappings.OfType<JsonObject>().Select(mapping =>
                new ResourcePackMapping(mapping["oldName"]!.GetValue<string>(), mapping["newName"]?.GetValue<string>()))];
            result = await new GameSettingsService(paths).TransformResourcePacksAsync(game, changes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            result = new GameSettingsChangeResult
            {
                State = "pending-recovery",
                Warning = "Content files committed; selection adjustment retained for content recover: " + ex.Message
            };
        }

        record["selectionState"] = result.State;
        record["selectionResult"] = JsonSerializer.SerializeToNode(result, ContentJsonContext.Default.GameSettingsChangeResult);
        try
        {
            await JsonFiles.WriteAsync(journal, record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Warning = "Content committed; selection journal status could not be saved: " + ex.Message;
        }

        return result;
    }
}