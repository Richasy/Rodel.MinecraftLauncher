// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Globalization;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Checks actual saved evidence before evaluating a table in an unrelated disposable original world.</summary>
internal static class WorldContainerPredictor
{
    internal static async Task<WorldContainerPrediction> ReadAsync(LauncherPaths paths, WorldMapSnapshot snapshot, string dimension, SeedContainer recorded, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(recorded);
        if (!snapshot.Dimensions.Contains(dimension, StringComparer.Ordinal)) throw new ArgumentException("The save has no selected dimension.", nameof(dimension));
        if (snapshot.Seed is null) throw new LauncherOperationException("WORLD_LOOT_SEED_UNAVAILABLE", "The save does not record the world seed needed by the original prediction context.");
        using var saved = await new WorldMapService(paths).OpenAsync(snapshot.Selection, ct).ConfigureAwait(false);
        if (saved.Snapshot.Revision != snapshot.Revision) throw new WorldSnapshotChangedException();
        var region = await saved.RegionAsync(new()
        {
            Selection = snapshot.Selection,
            Dimension = dimension,
            Bounds = new() { MinX = recorded.X, MaxX = recorded.X, MinY = recorded.Y, MaxY = recorded.Y, MinZ = recorded.Z, MaxZ = recorded.Z }
        }, cancellationToken: ct).ConfigureAwait(false);
        var actual = region.Containers.SingleOrDefault(container => container.X == recorded.X && container.Y == recorded.Y && container.Z == recorded.Z);
        if (actual is not { State: "unevaluated", LootTable: not null, LootSeed: not null } || actual.Id != recorded.Id
            || actual.LootTable != recorded.LootTable || actual.LootSeed != recorded.LootSeed)
        {
            throw new LauncherOperationException("WORLD_LOOT_RECORD_CHANGED", "The selected saved container no longer has the same unevaluated loot table and seed. Refresh its saved details.");
        }

        if (!long.TryParse(actual.LootSeed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var lootSeed))
            throw new InvalidDataException("The recorded loot seed is not a signed 64-bit value.");
        var runtime = await new SeedExplorerService(paths).OpenAsync(snapshot.Profile, ct).ConfigureAwait(false);
        await using var disposal = runtime.ConfigureAwait(false);
        var container = await runtime.PredictLootAsync(new()
        {
            Profile = runtime.Profile,
            Seed = snapshot.Seed,
            World = snapshot.Settings with { Dimension = dimension },
            Id = actual.Id,
            X = actual.X,
            Y = actual.Y,
            Z = actual.Z,
            LootTable = actual.LootTable,
            LootSeed = actual.LootSeed
        }, ct).ConfigureAwait(false);
        if (await saved.HasChangedAsync(ct).ConfigureAwait(false)) throw new WorldSnapshotChangedException();
        return new()
        {
            Container = container,
            SourceRevision = snapshot.Revision,
            RuntimeFingerprint = runtime.Fingerprint,
            Deterministic = lootSeed != 0,
            Warnings = lootSeed == 0 ? ["WORLD_LOOT_RANDOM_SEED: The original zero-seed loot path includes randomness; this is not a fixed saved inventory."] : []
        };
    }
}