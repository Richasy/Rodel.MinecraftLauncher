package io.github.richasy.rodel.seed;

import static io.github.richasy.rodel.seed.Protocol.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.level.material.Fluids;

import java.nio.file.Path;
import java.util.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;

/** Samples real decorated top blocks in an isolated bounded world, without player ticking or loot evaluation. */
final class VanillaSurface {
    static SurfaceMap map(Path root, MapQuery query, AtomicBoolean cancelled) throws Exception {
        if (query == null || query.y() != null || !query.terrain() || query.step() < 1 || query.side() < 1
                || (long)query.step() * query.side() > 128)
            throw new IllegalArgumentException("Decorated maps require a surface rectangle at most 128 blocks wide.");
        long lastX = (long)query.minX() + (long)query.step() * (query.side() - 1);
        long lastZ = (long)query.minZ() + (long)query.step() * (query.side() - 1);
        if (query.minX() < -30000000 || query.minZ() < -30000000 || lastX >= 30000000 || lastZ >= 30000000)
            throw new IllegalArgumentException("Surface samples exceed world bounds.");
        long started = System.nanoTime();
        return VanillaRegion.inWorld(root, query.world(), query.seed(), false, cancelled, server -> {
            var level = server.getLevel(ResourceKey.create(Registries.DIMENSION, Identifier.withDefaultNamespace(query.world().dimension())));
            if (level == null) throw new IllegalArgumentException("Unknown original dimension.");
            int west = Math.floorDiv(query.minX(), 16), north = Math.floorDiv(query.minZ(), 16);
            int east = Math.floorDiv((int)lastX, 16), south = Math.floorDiv((int)lastZ, 16);
            // Deterministic terrain and feature staging includes neighboring crowns at the requested edge.
            for (int stage = 0; stage < 2; stage++) {
                int halo = stage == 0 ? 2 : 1;
                for (int z = north - halo; z <= south + halo; z++)
                    for (int x = west - halo; x <= east + halo; x++) {
                        check(cancelled);
                        level.getChunkSource().getChunk(x, z, stage == 0 ? ChunkStatus.TERRAIN : ChunkStatus.FEATURES, true);
                    }
            }
            int count = query.side() * query.side();
            int[] ids = new int[count], heights = new int[count], fluids = new int[count], blocks = new int[count];
            int[] tops = new int[count], depths = new int[count], water = new int[count];
            var biomes = new ArrayList<String>();
            var states = new ArrayList<BlockStateEntry>();
            var stateIds = new IdentityHashMap<BlockState, Integer>();
            var tints = new ArrayList<List<Integer>>();
            final VanillaTint colors;
            try { colors = new VanillaTint(level.registryAccess()); }
            catch (java.io.IOException error) { throw new java.io.UncheckedIOException(error); }
            var pos = new BlockPos.MutableBlockPos();
            for (int z = 0; z < query.side(); z++)
                for (int x = 0; x < query.side(); x++) {
                    check(cancelled);
                    int wx = query.minX() + x * query.step(), wz = query.minZ() + z * query.step();
                    var chunk = level.getChunkSource().getChunk(wx >> 4, wz >> 4, ChunkStatus.FULL, true);
                    int y = chunk.getHeight(Heightmap.Types.WORLD_SURFACE, wx & 15, wz & 15);
                    var state = chunk.getBlockState(pos.set(wx, y, wz));
                    while (y >= level.getMinY() && state.isAir()) state = chunk.getBlockState(pos.set(wx, --y, wz));
                    int i = z * query.side() + x;
                    tops[i] = y;
                    var biome = level.getBiome(pos).value();
                    String biomeId = level.registryAccess().lookupOrThrow(Registries.BIOME).getKey(biome).getPath();
                    if (!biomes.contains(biomeId)) biomes.add(biomeId);
                    ids[i] = biomes.indexOf(biomeId);
                    var fluid = state.getFluidState();
                    fluids[i] = fluid.isEmpty() ? 0 : fluid.getType().isSame(Fluids.WATER) ? 1 : fluid.getType().isSame(Fluids.LAVA) ? 2 : 3;
                    water[i] = biome.getWaterColor() & 0xffffff;
                    tints.add(colors.surface(state, pos, level));
                    blocks[i] = stateIds.computeIfAbsent(state, value -> {
                        var properties = new TreeMap<String, String>();
                        for (var property : value.getProperties()) properties.put(property.getName(), propertyName(value, property));
                        states.add(new BlockStateEntry(BuiltInRegistries.BLOCK.getKey(value.getBlock()).toString(), properties));
                        return states.size() - 1;
                    });
                    while (y >= level.getMinY() && !chunk.getBlockState(pos.set(wx, y, wz)).getFluidState().isEmpty()) y--;
                    heights[i] = y;
                    depths[i] = tops[i] - y;
                }
            var tile = new MapResult(query.seed(), query.world(), query.minX(), query.minZ(), query.step(), query.side(), null,
                    biomes, ids, heights, fluids, List.of(), (System.nanoTime() - started) / 1000000.0);
            int stride = query.side() + 2;
            int[] relief = new int[stride * stride];
            Arrays.fill(relief, Integer.MIN_VALUE);
            for (int z = -1; z <= query.side(); z++)
                for (int x = -1; x <= query.side(); x++) {
                    check(cancelled);
                    int wx = query.minX() + x * query.step(), wz = query.minZ() + z * query.step();
                    if (wx < -30000000 || wz < -30000000 || wx >= 30000000 || wz >= 30000000) continue;
                    var chunk = level.getChunkSource().getChunk(wx >> 4, wz >> 4, ChunkStatus.FULL, true);
                    int y = chunk.getHeight(Heightmap.Types.WORLD_SURFACE, wx & 15, wz & 15);
                    while (y >= level.getMinY() && chunk.getBlockState(pos.set(wx, y, wz)).isAir()) y--;
                    relief[(z + 1) * stride + x + 1] = y;
                }
            return new SurfaceMap(tile, states, blocks, tints, tops, depths, water, relief);
        });
    }

    private static <T extends Comparable<T>> String propertyName(BlockState state, net.minecraft.world.level.block.state.properties.Property<T> property) {
        return property.getName(state.getValue(property));
    }

    private static void check(AtomicBoolean cancelled) {
        if (cancelled.get() || Thread.currentThread().isInterrupted()) throw new CancellationException();
    }
}
