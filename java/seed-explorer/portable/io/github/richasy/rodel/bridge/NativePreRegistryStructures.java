package io.github.richasy.rodel.bridge;

import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Uses original biome-supported structure registries and the original complete start generator. */
final class NativePreRegistryStructures extends NativeStructureSource {
    private final NativePreRegistryWorld world;
    private final NativeMappedSymbols s;
    private final Set<String> ids = new TreeSet<>();
    private final Map<String, Object> features = new HashMap<>();
    private final Map<String, String> nativeNames = new HashMap<>();
    private final Map<Long, List<Map<String, Object>>> cache = new LinkedHashMap<>();

    NativePreRegistryStructures(NativePreRegistryWorld world) throws Exception {
        this.world = world;
        s = world.s;
        for (Object feature : (Iterable<?>) world.structureRegistry) {
            String id = id(feature), nativeName = (String) s.call(feature, "getFeatureName");
            features.put(id, feature);
            nativeNames.put(id, id);
            nativeNames.put("minecraft:" + id, id);
            nativeNames.put(nativeName, id);
            nativeNames.put(nativeName.toLowerCase(Locale.ROOT), id);
            if (Boolean.TRUE.equals(s.call(world.source, "canGenerateStructure", feature)))
                ids.add(id);
        }
    }

    private String id(Object feature) throws Exception {
        if (feature instanceof String) {
            String id = nativeNames.get(feature);
            if (id == null)
                throw new IllegalStateException("Unregistered native structure start: " + feature);
            return id;
        }
        return NativeMappedWorld.id(s.call(world.structureRegistry, "getKey", feature));
    }

    @Override
    Set<String> ids() {
        return Collections.unmodifiableSet(ids);
    }

    @Override
    List<String> biomes(String id) throws Exception {
        Object feature = features.get(id);
        if (feature == null) return Collections.emptyList();
        List<String> result = new ArrayList<>();
        for (Map.Entry<Object, String> biome : world.biomeIds.entrySet())
            if (Boolean.TRUE.equals(s.call(biome.getKey(), "isValidStart", feature)))
                result.add(biome.getValue());
        Collections.sort(result);
        return result;
    }

    private static long key(int x, int z) {
        return ((long) x << 32) ^ (z & 0xffffffffL);
    }

    @Override
    List<Map<String, Object>> find(
            int minX, int minZ, int maxX, int maxZ, List<String> targets, AtomicBoolean cancelled)
            throws Exception {
        int minCx = Math.floorDiv(minX - 128, 16),
                minCz = Math.floorDiv(minZ - 128, 16),
                maxCx = Math.floorDiv(maxX + 128, 16),
                maxCz = Math.floorDiv(maxZ + 128, 16);
        if ((long) (maxCx - minCx + 1) * (maxCz - minCz + 1) > 1100000)
            throw new IllegalArgumentException("SEED_STRUCTURE_BUDGET");
        if (targets != null && Collections.disjoint(ids, targets)) return new ArrayList<>();
        List<Map<String, Object>> result = new ArrayList<>();
        for (int z = minCz; z <= maxCz; z++)
            for (int x = minCx; x <= maxCx; x++) {
                NativePreRegistryWorld.check(cancelled);
                for (Map<String, Object> start : starts(x, z)) {
                    int wx = ((Number) start.get("x")).intValue(),
                            wz = ((Number) start.get("z")).intValue();
                    if ((targets == null || targets.contains(start.get("id")))
                            && wx >= minX
                            && wx <= maxX
                            && wz >= minZ
                            && wz <= maxZ) result.add(start);
                }
            }
        return result;
    }

    private List<Map<String, Object>> starts(int x, int z) throws Exception {
        long key = key(x, z);
        List<Map<String, Object>> result = cache.get(key);
        if (result != null) return result;
        Object proto =
                s.make(
                        "net.minecraft.world.level.chunk.ProtoChunk",
                        s.make("net.minecraft.world.level.ChunkPos", x, z),
                        s.field("net.minecraft.world.level.chunk.UpgradeData", "EMPTY"));
        s.call(world.generator, "createBiomes", proto);
        if (world.biomes == world.source)
            s.call(world.generator, "createStructures", proto, world.generator, world.templates);
        else
            s.call(
                    world.generator,
                    "createStructures",
                    world.biomes,
                    proto,
                    world.generator,
                    world.templates);
        result = new ArrayList<>();
        for (Map.Entry<?, ?> entry : ((Map<?, ?>) s.call(proto, "getAllStarts")).entrySet()) {
            Object start = entry.getValue();
            if (!Boolean.TRUE.equals(s.call(start, "isValid"))) continue;
            Object box = s.call(start, "getBoundingBox"), center = s.call(box, "getCenter");
            List<Object> bounds = new ArrayList<>();
            for (String field : Arrays.asList("x0", "y0", "z0", "x1", "y1", "z1"))
                bounds.add(s.field(box, field));
            result.add(
                    Json.object(
                            "id",
                            id(entry.getKey()),
                            "kind",
                            "structure",
                            "x",
                            s.call(center, "getX"),
                            "y",
                            s.call(center, "getY"),
                            "z",
                            s.call(center, "getZ"),
                            "pieces",
                            ((List<?>) s.call(start, "getPieces")).size(),
                            "bounds",
                            bounds));
        }
        cache.put(key, result);
        if (cache.size() > 4096) cache.remove(cache.keySet().iterator().next());
        return result;
    }
}
