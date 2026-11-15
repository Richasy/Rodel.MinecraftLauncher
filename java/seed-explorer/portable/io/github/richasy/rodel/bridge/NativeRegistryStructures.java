package io.github.richasy.rodel.bridge;

import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Uses original StructureSettings/StructureFeature placement, then original start assembly. */
final class NativeRegistryStructures extends NativeStructureSource {
    private final NativeRegistryWorld world;
    private final NativeMappedSymbols s;
    private final Map<?, ?> placements;
    private final Set<String> ids = new TreeSet<>();
    private final Map<String, List<String>> eligibility = new HashMap<>();
    private final Map<Long, List<Map<String, Object>>> cache = new LinkedHashMap<>();

    NativeRegistryStructures(NativeRegistryWorld world) throws Exception {
        this.world = world;
        s = world.s;
        Set<String> possible = new HashSet<>(world.possibleBiomes());
        placements = (Map<?, ?>) s.call(s.call(world.generator, "getSettings"), "structureConfig");
        for (Object feature : placements.keySet()) {
            String id = id(feature);
            List<String> allowed = new ArrayList<>();
            for (Map.Entry<Object, String> biome : world.biomeIds.entrySet())
                if (Boolean.TRUE.equals(
                        s.call(
                                s.call(biome.getKey(), "getGenerationSettings"),
                                "isValidStart",
                                feature))) allowed.add(biome.getValue());
            if (!allowed.isEmpty()) {
                if (allowed.stream().anyMatch(possible::contains)) ids.add(id);
                eligibility.put(id, allowed);
            }
        }
        s.call(world.generator, "generateStrongholds");
        if (((List<?>) s.field(world.generator, "strongholdPositions")).isEmpty())
            ids.remove("stronghold");
    }

    private String id(Object structure) throws Exception {
        return NativeMappedWorld.id(s.call(world.structureRegistry, "getKey", structure));
    }

    @Override
    Set<String> ids() {
        return Collections.unmodifiableSet(ids);
    }

    @Override
    List<String> biomes(String id) {
        return eligibility.getOrDefault(id, Collections.emptyList());
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
        Map<Long, Object> candidates = new LinkedHashMap<>();
        for (Map.Entry<?, ?> entry : placements.entrySet()) {
            if (targets != null && !targets.contains(id(entry.getKey()))) continue;
            int spacing = ((Number) s.call(entry.getValue(), "spacing")).intValue();
            if (spacing < 1) throw new IllegalStateException("Invalid original structure spacing.");
            long count =
                    (long) (Math.floorDiv(maxCx, spacing) - Math.floorDiv(minCx, spacing) + 1)
                            * (Math.floorDiv(maxCz, spacing) - Math.floorDiv(minCz, spacing) + 1);
            if (count > 1000000) throw new IllegalArgumentException("SEED_STRUCTURE_BUDGET");
            Object random = s.make("net.minecraft.world.level.levelgen.WorldgenRandom");
            for (int z = Math.floorDiv(minCz, spacing); z <= Math.floorDiv(maxCz, spacing); z++)
                for (int x = Math.floorDiv(minCx, spacing);
                        x <= Math.floorDiv(maxCx, spacing);
                        x++) {
                    NativeMappedWorld.check(cancelled);
                    Object pos =
                            s.call(
                                    entry.getKey(),
                                    "getPotentialFeatureChunk",
                                    entry.getValue(),
                                    world.seed,
                                    random,
                                    x * spacing,
                                    z * spacing);
                    int cx = ((Number) s.field(pos, "x")).intValue(),
                            cz = ((Number) s.field(pos, "z")).intValue();
                    if (cx >= minCx && cx <= maxCx && cz >= minCz && cz <= maxCz)
                        candidates.put(key(cx, cz), pos);
                }
        }
        if (targets == null || targets.contains("stronghold"))
            for (Object pos : (List<?>) s.field(world.generator, "strongholdPositions")) {
                int cx = ((Number) s.field(pos, "x")).intValue(),
                        cz = ((Number) s.field(pos, "z")).intValue();
                if (cx >= minCx && cx <= maxCx && cz >= minCz && cz <= maxCz)
                    candidates.put(key(cx, cz), pos);
            }
        List<Map<String, Object>> result = new ArrayList<>();
        for (Map.Entry<Long, Object> candidate : candidates.entrySet()) {
            NativeMappedWorld.check(cancelled);
            for (Map<String, Object> start : starts(candidate.getKey(), candidate.getValue())) {
                int x = ((Number) start.get("x")).intValue(),
                        z = ((Number) start.get("z")).intValue();
                if ((targets == null || targets.contains(start.get("id")))
                        && x >= minX
                        && x <= maxX
                        && z >= minZ
                        && z <= maxZ) result.add(start);
            }
        }
        return result;
    }

    private List<Map<String, Object>> starts(long key, Object position) throws Exception {
        List<Map<String, Object>> result = cache.get(key);
        if (result != null) return result;
        Object proto;
        try {
            proto =
                    s.make(
                            "net.minecraft.world.level.chunk.ProtoChunk",
                            position,
                            s.field("net.minecraft.world.level.chunk.UpgradeData", "EMPTY"),
                            world.height);
        } catch (NoSuchMethodException oldHeight) {
            proto =
                    s.make(
                            "net.minecraft.world.level.chunk.ProtoChunk",
                            position,
                            s.field("net.minecraft.world.level.chunk.UpgradeData", "EMPTY"));
        }
        s.call(
                world.generator,
                "createStructures",
                world.registries,
                world.structureManager,
                proto,
                world.templates,
                world.seed);
        result = new ArrayList<>();
        for (Map.Entry<?, ?> entry : ((Map<?, ?>) s.call(proto, "getAllStarts")).entrySet()) {
            Object start = entry.getValue();
            if (!Boolean.TRUE.equals(s.call(start, "isValid"))) continue;
            Object box = s.call(start, "getBoundingBox"), center = s.call(box, "getCenter");
            List<Object> bounds = new ArrayList<>();
            for (String field : Arrays.asList("minX", "minY", "minZ", "maxX", "maxY", "maxZ")) {
                try {
                    bounds.add(s.call(box, field));
                } catch (NoSuchMethodException oldBounds) {
                    bounds.add(
                            s.field(
                                    box,
                                    (field.charAt(3) == 'X'
                                                    ? "x"
                                                    : field.charAt(3) == 'Y' ? "y" : "z")
                                            + (field.startsWith("min") ? "0" : "1")));
                }
            }
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
