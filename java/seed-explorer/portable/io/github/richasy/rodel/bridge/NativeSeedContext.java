package io.github.richasy.rodel.bridge;

import java.io.File;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

/** Actual native world capabilities shared by API families; no generation algorithm lives here. */
abstract class NativeSeedContext implements AutoCloseable {
    Map<Object, String> biomeIds;
    final Map<String, String> biomeNames = new LinkedHashMap<>();
    int minimumY, heightLimit;
    NativeStructureSource structures;
    final List<Map<String, Object>> inventories = new ArrayList<>();
    Consumer<Map<String, Object>> progress = ignored -> {};

    void report(String phase, int completed, int total) {
        progress.accept(Json.object("phase", phase, "completed", completed, "total", total));
    }

    abstract String biome(int x, int y, int z) throws Exception;

    abstract int[] column(int x, int z, Integer slice, boolean terrain) throws Exception;

    abstract Map<String, Object> spawn() throws Exception;

    abstract File save(
            int minX, int minZ, int maxX, int maxZ, boolean loot, AtomicBoolean cancelled)
            throws Exception;

    abstract Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception;

    Map<String, Object> predictLoot(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        throw new UnsupportedOperationException(
                "The original runtime does not expose this recorded-table prediction operation.");
    }

    List<String> possibleBiomes() throws Exception {
        return new ArrayList<>(new TreeSet<>(biomeIds.values()));
    }

    List<String> dimensions() throws Exception {
        return Arrays.asList("overworld", "the_nether", "the_end");
    }

    boolean seedBoundSpawn() {
        return true;
    }

    boolean known(int x, int z) {
        return true;
    }

    int surfaceColor(int x, int y, int z) throws Exception {
        return 0x929b9c;
    }

    /** Releases resources owned by this disposable world, never a player world. */
    @Override
    public void close() throws Exception {}
}

/** Placement, validity and piece assembly remain in each installed native generator. */
abstract class NativeStructureSource {
    abstract Set<String> ids();

    List<String> biomes(String id) throws Exception {
        return Collections.emptyList();
    }

    abstract List<Map<String, Object>> find(
            int minX, int minZ, int maxX, int maxZ, List<String> targets, AtomicBoolean cancelled)
            throws Exception;
}
