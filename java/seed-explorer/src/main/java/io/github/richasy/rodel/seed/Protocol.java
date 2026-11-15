package io.github.richasy.rodel.seed;

import java.util.List;

/** Fixed, versioned process protocol. Generation algorithms remain in the original game. */
final class Protocol {
    private Protocol() {}

    record World(String dimension, String preset, String biome) {}

    record SpawnQuery(World world, String seed) {}

    record SpawnResult(String seed, World world, Position position, double elapsedMilliseconds) {}

    record MapQuery(
            World world,
            String seed,
            int minX,
            int minZ,
            int step,
            int side,
            Integer y,
            boolean terrain,
            List<String> structures) {}

    record Condition(
            String id,
            String kind,
            List<String> targets,
            int minimum,
            int radius,
            String reference,
            Integer y) {}

    record SearchQuery(
            World world,
            String startSeed,
            long offset,
            int maxSeeds,
            int maxResults,
            int timeLimitSeconds,
            int biomeStep,
            List<Condition> conditions) {}

    record StructureQuery(
            World world,
            String seed,
            int centerX,
            int centerZ,
            int radius,
            List<String> targets,
            int limit) {}

    record StructureResult(
            String seed,
            World world,
            int centerX,
            int centerZ,
            int radius,
            List<String> targets,
            int found,
            List<Position> positions,
            double elapsedMilliseconds) {}

    record Bounds(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) {
        long volume() {
            return ((long) maxX - minX + 1) * ((long) maxY - minY + 1) * ((long) maxZ - minZ + 1);
        }

        boolean contains(int x, int y, int z) {
            return x >= minX && x <= maxX && y >= minY && y <= maxY && z >= minZ && z <= maxZ;
        }
    }

    record RegionQuery(
            World world, String seed, Bounds bounds, boolean includeBlocks, boolean includeLoot) {}

    record BlockStateEntry(String id, java.util.Map<String, String> properties) {}

    record BlockCount(String id, long count) {}

    record LootItem(
            int slot,
            String id,
            int count,
            java.util.Map<String, com.google.gson.JsonElement> components) {}

    record ContainerEntry(
            String id,
            int x,
            int y,
            int z,
            String state,
            int slots,
            String lootTable,
            String lootSeed,
            List<LootItem> items) {}

    record LootQuery(
            World world,
            String seed,
            String id,
            int x,
            int y,
            int z,
            String lootTable,
            String lootSeed) {}

    record RegionTint(String biomeId, List<Integer> colors) {}

    record RegionTints(List<RegionTint> tintPalette, String tintIndices) {}

    record FrozenTintQuery(
            Bounds bounds,
            List<BlockStateEntry> palette,
            String blocks,
            List<String> biomePalette,
            String biomes) {}

    record RegionProgress(String phase, int completed, int total) {}

    record RegionResult(
            String seed,
            World world,
            Bounds bounds,
            String generationPolicy,
            List<BlockStateEntry> palette,
            String blocks,
            List<BlockCount> materials,
            List<ContainerEntry> containers,
            double elapsedMilliseconds,
            List<RegionTint> tintPalette,
            String tintIndices) {}

    record Request(
            String id,
            String operation,
            MapQuery map,
            SearchQuery search,
            String cancelId,
            String seed,
            StructureQuery structures,
            RegionQuery region,
            SpawnQuery spawn,
            FrozenTintQuery tints,
            LootQuery loot) {}

    record SeedValue(String seed) {}

    record Envelope(String id, String kind, Object data, String error) {}

    record Ready(int protocol, String version) {}

    record BiomeEntry(String id, List<String> dimensions) {}

    record StructureEntry(String id, List<String> dimensions, List<String> biomes) {}

    record DimensionEntry(String id, int minY, int height, List<String> biomes) {}

    record Catalog(
            String version,
            List<DimensionEntry> dimensions,
            List<BiomeEntry> biomes,
            List<StructureEntry> structures,
            java.util.Map<String, java.util.Map<String, String>> blockDefaults, boolean decoratedMaps) {}

    record SurfaceMap(MapResult tile, List<BlockStateEntry> palette, int[] blocks, List<List<Integer>> tints,
            int[] surfaceHeights, int[] waterDepths, int[] waterColors, int[] reliefHeights) {}

    record Position(
            String id, String kind, int x, int y, int z, String biome, int pieces, int[] bounds) {}

    record MapResult(
            String seed,
            World world,
            int minX,
            int minZ,
            int step,
            int side,
            Integer y,
            List<String> palette,
            int[] biomes,
            int[] heights,
            int[] fluids,
            List<Position> structures,
            double elapsedMilliseconds) {}

    record Match(String conditionId, Position anchor, List<Position> positions) {}

    record SearchHit(String seed, List<Match> matches) {}

    record SearchResult(
            String status,
            long checked,
            long nextOffset,
            List<SearchHit> hits,
            double elapsedMilliseconds) {}
}
