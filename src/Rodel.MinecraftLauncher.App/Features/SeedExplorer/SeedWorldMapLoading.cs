// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Publishes saved coverage before waiting for original generation at each progressively finer map level.</summary>
internal static class SeedWorldMapLoading
{
    internal static async Task ReadAsync(SeedMapQuery[] queries,
        Func<SeedMapQuery, CancellationToken, Task<SeedMapTile>> read,
        Func<SeedMapQuery, SeedMapTile, CancellationToken, Task<SeedMapTile>> preview,
        Func<SeedMapQuery, SeedMapTile, bool, Task> publish, CancellationToken ct)
    {
        foreach (var level in queries.GroupBy(query => query.Step))
        {
            var saved = new List<(SeedMapQuery Query, SeedMapTile Tile)>();
            foreach (var query in level)
            {
                ct.ThrowIfCancellationRequested();
                var tile = await read(query, ct);
                ct.ThrowIfCancellationRequested();
                saved.Add((query, tile));
                await publish(query, tile, false);
            }

            foreach (var (query, tile) in saved)
            {
                ct.ThrowIfCancellationRequested();
                var completed = await preview(query, tile, ct);
                ct.ThrowIfCancellationRequested();
                await publish(query, completed, true);
            }
        }
    }
}