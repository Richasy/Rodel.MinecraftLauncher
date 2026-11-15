// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

internal static class SeedRegionText
{
    internal static string Policy(SeedExplorerSnapshot state, SeedRegionResult? data) => data?.GenerationPolicy == "saved_snapshot_v1"
        ? string.Format(Resources.SeedSavedCoverageFormat, data.Bounds.Volume - data.UnknownBlocks, data.UnknownBlocks)
        : state.UseSavedMap && !(state.SceneOpen && state.ScenePreview) ? Resources.SeedSavedSourcePolicy : Resources.SeedGenerationPolicy;
    internal static string Name(SeedExplorerSnapshot state, string id, bool item = false)
    {
        var suffix = id.Replace(':', '.');
        var names = state.Catalog?.Names;
        if (names is not null && names.TryGetValue((item ? "item." : "block.") + suffix, out var value)) return value;
        if (names is not null && names.TryGetValue((item ? "block." : "item.") + suffix, out value)) return value;
        return id;
    }
    internal static string Position(SeedContainer container) => SeedText.Coordinates(new() { X = container.X, Y = container.Y, Z = container.Z });
    internal static string Range(SeedBounds bounds) => string.Format(Resources.SeedAreaRangeFormat, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY, bounds.MinZ, bounds.MaxZ);
    internal static string Progress(SeedRegionProgress? progress, bool saved = false)
    {
        var label = progress?.Phase switch
        {
            "terrain" => Resources.SeedStageTerrain,
            "features" => Resources.SeedStageFeatures,
            "full" => Resources.SeedStageFull,
            "scan" => Resources.SeedStageScan,
            "models" => Resources.SeedStageModels,
            _ => saved ? Resources.SeedReadingWorld : Resources.SeedStageLoading,
        };
        return progress is { Total: > 0 } ? string.Format(Resources.SeedStageFormat, label, progress.Completed, progress.Total) : label;
    }
    internal static bool ItemMatches(SeedExplorerSnapshot state, SeedLootItem item, string filter) => filter.Length == 0 || item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Name(state, item.Id, true).Contains(filter, StringComparison.OrdinalIgnoreCase) || Components(state, item).Contains(filter, StringComparison.OrdinalIgnoreCase);
    internal static string Components(SeedExplorerSnapshot state, SeedLootItem item) => string.Join(Environment.NewLine, item.Components.Select(component =>
    {
        if (component.Key is "minecraft:stored_enchantments" or "minecraft:enchantments" && component.Value.ValueKind == JsonValueKind.Object)
        {
            return string.Join(Environment.NewLine, component.Value.EnumerateObject().Select(value =>
                (state.Catalog?.Names.GetValueOrDefault("enchantment." + value.Name.Replace(':', '.')) ?? value.Name) + " " + value.Value.GetRawText()));
        }

        return component.Key + ": " + component.Value.GetRawText();
    }));
}