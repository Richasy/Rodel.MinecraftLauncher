// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Reads actual saved states and inventories into the existing frozen rendering/analysis contract.</summary>
internal static class SavedRegionReader
{
    internal static async Task<SeedRegionResult> ReadAsync(WorldMapSnapshot snapshot, string dimension, Dictionary<string, WorldFileStamp> files,
        FiniteWorld? finite, WorldRegionQuery query, IProgress<SeedRegionProgress>? progress, CancellationToken ct)
    {
        var b = query.Bounds ?? throw new ArgumentException("Saved region bounds are required.");
        var width = (long)b.MaxX - b.MinX + 1; var depth = (long)b.MaxZ - b.MinZ + 1; var height = (long)b.MaxY - b.MinY + 1;
        if (width is < 1 or > 256 || depth is < 1 or > 256 || height is < 1 or > 65536 || b.MinY < -32768 || b.MaxY > 32767
            || b.MinX < -30_000_000 || b.MaxX >= 30_000_000 || b.MinZ < -30_000_000 || b.MaxZ >= 30_000_000
            || b.Volume > (query.IncludeBlocks ? 4_194_304 : 16_777_216))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Select a bounded saved block volume.");
        }

        foreach (var file in files.Values) file.EnsureUnchanged();
        var timer = Stopwatch.StartNew();
        var palette = new List<SeedBlockState> { new() { Id = "rodel:unknown" } };
        var identities = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var stateIds = new Dictionary<SeedBlockState, ushort>(ReferenceEqualityComparer.Instance);
        var finiteStates = new Dictionary<string, SeedBlockState>(StringComparer.Ordinal);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var blocks = query.IncludeBlocks ? new byte[checked((int)b.Volume * 2)] : [];
        var biomes = query.IncludeBlocks ? new byte[checked((int)b.Volume * 2)] : [];
        var biomePalette = new List<string> { "rodel:unknown" };
        var biomeIds = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["rodel:unknown"] = 0 };
        var containers = new List<SeedContainer>(); var warnings = new HashSet<string>(StringComparer.Ordinal);
        var unknown = b.Volume;
        var total = ((b.MaxX >> 4) - (b.MinX >> 4) + 1) * ((b.MaxZ >> 4) - (b.MinZ >> 4) + 1); var completed = 0;
        for (var chunkZ = b.MinZ >> 4; chunkZ <= b.MaxZ >> 4; chunkZ++)
        {
            for (var chunkX = b.MinX >> 4; chunkX <= b.MaxX >> 4; chunkX++)
            {
                ct.ThrowIfCancellationRequested();
                SavedChunk? chunk = null;
                if (finite is null)
                {
                    try { chunk = await WorldRegionReader.ReadAsync(dimension, chunkX, chunkZ, files, ct, containers: true).ConfigureAwait(false); }
                    catch (WorldSnapshotChangedException) { throw; }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
                    { if (warnings.Count < 16) warnings.Add("WORLD_CHUNK_READ: " + error.Message); }
                }

                if (finite is not null || chunk is not null)
                {
                    for (var y = b.MinY; y <= b.MaxY; y++)
                    {
                        ct.ThrowIfCancellationRequested();
                        for (var z = Math.Max(b.MinZ, chunkZ * 16); z <= Math.Min(b.MaxZ, (chunkZ * 16) + 15); z++)
                        {
                            for (var x = Math.Max(b.MinX, chunkX * 16); x <= Math.Min(b.MaxX, (chunkX * 16) + 15); x++)
                            {
                                var state = chunk?.State(x & 15, y, z & 15);
                                if (finite is not null && finite.Sample(x, z, y) is { } sample)
                                {
                                    if (!finiteStates.TryGetValue(sample.Block, out state)) finiteStates[sample.Block] = state = new() { Id = sample.Block };
                                }

                                if (state is null) continue;
                                unknown--; counts[state.Id] = counts.GetValueOrDefault(state.Id) + 1;
                                if (!query.IncludeBlocks) continue;
                                if (!stateIds.TryGetValue(state, out var index))
                                {
                                    var key = state.Id + "[" + string.Join(',', state.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)) + "]";
                                    if (!identities.TryGetValue(key, out index))
                                    {
                                        if (palette.Count >= ushort.MaxValue) throw new InvalidDataException("Saved state palette exceeds the preview budget.");
                                        index = (ushort)palette.Count; identities[key] = index; palette.Add(state);
                                    }

                                    stateIds[state] = index;
                                }

                                var position = checked((int)(((((y - b.MinY) * depth) + z - b.MinZ) * width) + x - b.MinX));
                                BinaryPrimitives.WriteUInt16LittleEndian(blocks.AsSpan(position * 2, 2), index);
                                var biome = chunk?.Biome(x & 15, y, z & 15) ?? "unknown";
                                biome = biome == "unknown" ? "rodel:unknown" : biome.Contains(':', StringComparison.Ordinal) ? biome : "minecraft:" + biome;
                                if (!biomeIds.TryGetValue(biome, out var biomeIndex))
                                {
                                    if (biomePalette.Count >= 65536) throw new InvalidDataException("Saved biome palette exceeds the preview budget.");
                                    biomeIndex = (ushort)biomePalette.Count; biomeIds[biome] = biomeIndex; biomePalette.Add(biome);
                                }

                                BinaryPrimitives.WriteUInt16LittleEndian(biomes.AsSpan(position * 2, 2), biomeIndex);
                            }
                        }
                    }

                    if (chunk is not null) ReadContainers(chunk, b, containers);
                }

                progress?.Report(new() { Phase = "scan", Completed = ++completed, Total = total });
            }
        }

        foreach (var file in files.Values) file.EnsureUnchanged();
        return new()
        {
            Seed = snapshot.Seed ?? string.Empty,
            World = snapshot.Settings with { Dimension = query.Dimension },
            Bounds = b with { },
            GenerationPolicy = "saved_snapshot_v1",
            Fingerprint = snapshot.Revision,
            Palette = query.IncludeBlocks ? [.. palette] : [],
            Blocks = blocks,
            BiomePalette = query.IncludeBlocks ? [.. biomePalette] : [],
            Biomes = biomes,
            Materials = [.. counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new SeedBlockCount { Id = pair.Key, Count = pair.Value })],
            Containers = [.. containers],
            UnknownBlocks = unknown,
            Warnings = [.. warnings],
            ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
        };
    }

    internal static void ReadContainers(SavedChunk chunk, SeedBounds bounds, List<SeedContainer> result)
    {
        foreach (var data in chunk.BlockEntities.OfType<Dictionary<string, object>>())
        {
            if (data.Integer("x") is not { } x || data.Integer("y") is not { } y || data.Integer("z") is not { } z
                || x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue
                || !bounds.Contains((int)x, (int)y, (int)z))
            {
                continue;
            }

            var items = data.GetValueOrDefault("Items") as List<object>;
            var loot = data.Text("LootTable") ?? data.Text("loot_table");
            if (items is null && loot is null) continue;
            var stacks = new List<SeedLootItem>();
            foreach (var stack in items?.OfType<Dictionary<string, object>>() ?? [])
            {
                var id = stack.Text("id"); var count = stack.Integer("count") ?? stack.Integer("Count"); var slot = stack.Integer("Slot") ?? stack.Integer("slot");
                if (id is null || count is null or <= 0 or > int.MaxValue || slot is null or < 0 or > 255) continue;
                var components = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (stack.GetValueOrDefault("components") is Dictionary<string, object> saved)
                    foreach (var (key, value) in saved) components[key] = Json(value);
                if (stack.GetValueOrDefault("tag") is { } legacy) components["legacy:tag"] = Json(legacy);
                if (stack.Integer("Damage") is { } damage) components["legacy:damage"] = Json(damage);
                stacks.Add(new() { Id = id, Count = (int)count, Slot = (int)slot, Components = components });
            }

            result.Add(new()
            {
                Id = chunk.State((int)x & 15, (int)y, (int)z & 15).Id,
                X = (int)x,
                Y = (int)y,
                Z = (int)z,
                State = loot is null ? "saved" : "unevaluated",
                LootTable = loot,
                LootSeed = (data.Integer("LootTableSeed") ?? data.Integer("loot_table_seed"))?.ToString(CultureInfo.InvariantCulture),
                Slots = stacks.Count == 0 ? 0 : stacks.Max(stack => stack.Slot) + 1,
                Items = [.. stacks.OrderBy(stack => stack.Slot)],
            });
        }
    }

    private static JsonElement Json(object value)
    {
        using var document = JsonDocument.Parse(Node(value)?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private static JsonNode? Node(object value) => value switch
    {
        Dictionary<string, object> fields => new JsonObject(fields.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Node(pair.Value)))),
        List<object> list => new JsonArray([.. list.Select(Node)]),
        byte[] bytes => new JsonArray([.. bytes.Select(number => (JsonNode?)JsonValue.Create(number))]),
        int[] values => new JsonArray([.. values.Select(number => (JsonNode?)JsonValue.Create(number))]),
        long[] values => new JsonArray([.. values.Select(number => (JsonNode?)JsonValue.Create(number))]),
        string text => JsonValue.Create(text),
        sbyte number => JsonValue.Create(number),
        short number => JsonValue.Create(number),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        float number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        _ => null,
    };
}