// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Combines independently validated original pixels with a saved revision without promoting predictions into saved observations.</summary>
internal static class WorldMapPreview
{
    internal static async Task<SeedMapTile> ReadAsync(WorldMapSession session, WorldMapQuery query,
        Func<SeedMapQuery, CancellationToken, Task<SeedMapTile>>? preview, CancellationToken ct, bool decoratedMaps = false)
    {
        var saved = await session.MapAsync(query, ct).ConfigureAwait(false);
        if (!saved.PreviewEligible.Any(value => value)) return saved;
        var snapshot = session.Snapshot;
        if (!snapshot.PreviewSupported || snapshot.Seed is null || preview is null)
            return Warn(saved, snapshot.PreviewUnavailableReason ?? "The selected original runtime or saved seed is unavailable.");
        var request = new SeedMapQuery
        {
            Profile = snapshot.Profile,
            Seed = snapshot.Seed,
            World = snapshot.Settings with { Dimension = query.Dimension },
            MinX = query.MinX,
            MinZ = query.MinZ,
            Step = query.Step,
            Side = query.Side,
            Y = query.Y,
            Terrain = true,
            Decorated = query.Decorated && decoratedMaps && query.Y is null && (long)query.Side * query.Step <= 128,
            Structures = [],
        };
        SeedMapTile? generated = null;
        Exception? failure = null;
        try
        {
            generated = await preview(request, ct).ConfigureAwait(false);
            SeedValidation.Tile(generated, request);
            if (generated.Source != "generated" || string.IsNullOrWhiteSpace(generated.Fingerprint))
                throw new InvalidDataException("The original preview has no independent runtime identity.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or LauncherOperationException or TimeoutException or JsonException)
        { failure = error; }

        ct.ThrowIfCancellationRequested();
        if (await session.HasChangedAsync(ct).ConfigureAwait(false)) throw new WorldSnapshotChangedException();
        return failure is null ? Merge(saved, generated!) : Warn(saved, failure.Message);
    }

    private static SeedMapTile Warn(SeedMapTile saved, string reason) => saved with { Warnings = [.. saved.Warnings, "WORLD_PREVIEW_UNAVAILABLE: " + reason] };

    private static SeedMapTile Merge(SeedMapTile saved, SeedMapTile generated)
    {
        var palette = saved.Palette.ToList();
        var ids = palette.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var biomes = saved.Biomes.ToArray(); var heights = saved.Heights.ToArray(); var fluids = saved.Fluids.ToArray();
        var colors = saved.SurfaceColors.ToArray(); var known = saved.Known.ToArray();
        var surfaceHeights = saved.SurfaceHeights.Length == 0 ? saved.Heights.ToArray() : [.. saved.SurfaceHeights];
        var depths = saved.WaterDepths.Length == 0 ? new int[known.Length] : [.. saved.WaterDepths];
        var relief = saved.ReliefHeights.ToArray();
        var shades = SeedMapImage.Shades(saved);
        var predictedShades = SeedMapImage.Shades(generated);
        var filled = false;
        for (var index = 0; index < known.Length; index++)
        {
            if (known[index] || !saved.PreviewEligible[index] || (generated.Known.Length != 0 && !generated.Known[index])) continue;
            var biome = generated.Palette.Length == 0 ? null : generated.Palette[generated.Biomes[index]];
            var id = -1;
            if (biome is not null && !ids.TryGetValue(biome, out id)) { id = palette.Count; palette.Add(biome); ids.Add(biome, id); }

            biomes[index] = id; heights[index] = generated.Heights[index]; fluids[index] = generated.Fluids[index];
            colors[index] = generated.SurfaceColors.Length == 0 ? SeedMapImage.BiomeColor(biome!) : generated.SurfaceColors[index];
            known[index] = true; filled = true;
            surfaceHeights[index] = generated.SurfaceHeights.Length == 0 ? generated.Heights[index] : generated.SurfaceHeights[index];
            depths[index] = generated.WaterDepths.Length == 0 ? 0 : generated.WaterDepths[index];
            shades[index] = predictedShades[index];
            if (relief.Length != 0) relief[(((index / saved.Side) + 1) * (saved.Side + 2)) + (index % saved.Side) + 1] = surfaceHeights[index];
        }

        return !filled ? saved : saved with
        {
            Source = "mixed",
            Palette = [.. palette],
            Biomes = biomes,
            Heights = heights,
            Fluids = fluids,
            SurfaceColors = colors,
            SurfaceHeights = surfaceHeights,
            WaterDepths = depths,
            ReliefHeights = relief,
            SurfaceShades = shades,
            Known = known,
            DimensionMinY = generated.DimensionMinY,
            PreviewFingerprint = generated.Fingerprint,
            ElapsedMilliseconds = saved.ElapsedMilliseconds + generated.ElapsedMilliseconds,
            Warnings = [.. saved.Warnings, .. generated.Warnings],
        };
    }
}