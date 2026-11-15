package io.github.richasy.rodel.bridge;

import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Enumerates candidate chunks and assembles starts through the installed native placement APIs. */
final class NativeMappedStructures extends NativeStructureSource {
    private final NativeMappedContext world;
    private final NativeMappedSymbols s;
    private final List<Object> sets = new ArrayList<>();
    private final Set<String> ids = new TreeSet<>();
    private final Map<String, Set<String>> eligibility = new HashMap<>();
    private final Map<Long, List<Map<String, Object>>> cache = new LinkedHashMap<>();

    NativeMappedStructures(NativeMappedContext world) throws Exception {
        this.world = world;
        s = world.s;
        Set<String> allowed = new HashSet<>(world.possibleBiomes());
        Object nativeSets = s.call(world.structureState, "possibleStructureSets");
        if (nativeSets instanceof java.util.stream.Stream)
            nativeSets =
                    ((java.util.stream.Stream<?>) nativeSets)
                            .collect(java.util.stream.Collectors.toList());
        for (Object holder : (Iterable<?>) nativeSets) {
            Object set = s.call(holder, "value");
            sets.add(set);
            for (Object entry : (Iterable<?>) s.call(set, "structures")) {
                Object structure = s.call(s.call(entry, "structure"), "value");
                for (Object biome : (Iterable<?>) s.call(structure, "biomes")) {
                    String biomeId = world.biomeIds.get(s.call(biome, "value"));
                    eligibility.computeIfAbsent(id(structure), key -> new TreeSet<>()).add(biomeId);
                    if (allowed.contains(biomeId)) ids.add(id(structure));
                }
            }
        }
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
        return new ArrayList<>(eligibility.getOrDefault(id, Collections.emptySet()));
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
        for (Object set : sets) {
            boolean wanted = targets == null;
            for (Object entry : (Iterable<?>) s.call(set, "structures"))
                if (targets != null
                        && targets.contains(id(s.call(s.call(entry, "structure"), "value"))))
                    wanted = true;
            if (!wanted) continue;
            Object placement = s.call(set, "placement");
            if (s.type(
                            "net.minecraft.world.level.levelgen.structure.placement.RandomSpreadStructurePlacement")
                    .isInstance(placement)) {
                int spacing = ((Number) s.call(placement, "spacing")).intValue();
                if (spacing < 1)
                    throw new IllegalStateException("Native structure spacing is invalid.");
                for (int z = Math.floorDiv(minCz, spacing); z <= Math.floorDiv(maxCz, spacing); z++)
                    for (int x = Math.floorDiv(minCx, spacing);
                            x <= Math.floorDiv(maxCx, spacing);
                            x++) {
                        NativeMappedWorld.check(cancelled);
                        Object pos;
                        try {
                            pos =
                                    s.call(
                                            placement,
                                            "getPotentialStructureChunk",
                                            world.seed,
                                            x * spacing,
                                            z * spacing);
                        } catch (NoSuchMethodException older) {
                            pos =
                                    s.call(
                                            placement,
                                            "getPotentialFeatureChunk",
                                            world.seed,
                                            x * spacing,
                                            z * spacing);
                        }
                        int cx = ((Number) s.field(pos, "x")).intValue(),
                                cz = ((Number) s.field(pos, "z")).intValue();
                        if (cx >= minCx
                                && cx <= maxCx
                                && cz >= minCz
                                && cz <= maxCz
                                && valid(placement, cx, cz)) candidates.put(key(cx, cz), pos);
                    }
            } else if (s.type(
                            "net.minecraft.world.level.levelgen.structure.placement.ConcentricRingsStructurePlacement")
                    .isInstance(placement)) {
                for (Object pos :
                        (Iterable<?>)
                                s.call(world.structureState, "getRingPositionsFor", placement)) {
                    NativeMappedWorld.check(cancelled);
                    int cx = ((Number) s.field(pos, "x")).intValue(),
                            cz = ((Number) s.field(pos, "z")).intValue();
                    if (cx >= minCx && cx <= maxCx && cz >= minCz && cz <= maxCz)
                        candidates.put(key(cx, cz), pos);
                }
            } else {
                if ((long) (maxCx - minCx + 1) * (maxCz - minCz + 1) > 1000000)
                    throw new IllegalArgumentException("SEED_STRUCTURE_BUDGET");
                for (int z = minCz; z <= maxCz; z++)
                    for (int x = minCx; x <= maxCx; x++) {
                        NativeMappedWorld.check(cancelled);
                        if (valid(placement, x, z))
                            candidates.put(
                                    key(x, z), s.make("net.minecraft.world.level.ChunkPos", x, z));
                    }
            }
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

    private static long key(int x, int z) {
        return ((long) x << 32) ^ (z & 0xffffffffL);
    }

    private List<Map<String, Object>> starts(long key, Object position) throws Exception {
        List<Map<String, Object>> result = cache.get(key);
        if (result != null) return result;
        Object container = world.biomeRegistry;
        if (s.hasType("net.minecraft.world.level.chunk.PalettedContainerFactory"))
            container =
                    s.call(
                            s.type("net.minecraft.world.level.chunk.PalettedContainerFactory"),
                            "create",
                            world.registries);
        Object proto =
                s.make(
                        "net.minecraft.world.level.chunk.ProtoChunk",
                        position,
                        s.field("net.minecraft.world.level.chunk.UpgradeData", "EMPTY"),
                        world.height,
                        container,
                        null);
        if (world.structureState == world.generator && world.random != null)
            s.call(
                    world.generator,
                    "createStructures",
                    world.registries,
                    world.random,
                    world.structureManager,
                    proto,
                    world.templates,
                    world.seed);
        else if (world.structureState == world.generator)
            s.call(
                    world.generator,
                    "createStructures",
                    world.registries,
                    world.structureManager,
                    proto,
                    world.templates,
                    world.seed);
        else {
            try {
                s.call(
                        world.generator,
                        "createStructures",
                        world.registries,
                        world.structureState,
                        world.structureManager,
                        proto,
                        world.templates);
            } catch (NoSuchMethodException dimensionApi) {
                s.call(
                        world.generator,
                        "createStructures",
                        world.registries,
                        world.structureState,
                        world.structureManager,
                        proto,
                        world.templates,
                        world.dimensionKey);
            }
        }
        result = new ArrayList<>();
        for (Map.Entry<?, ?> entry : ((Map<?, ?>) s.call(proto, "getAllStarts")).entrySet()) {
            Object start = entry.getValue();
            if (!Boolean.TRUE.equals(s.call(start, "isValid"))) continue;
            Object box = s.call(start, "getBoundingBox"), center = s.call(box, "getCenter");
            List<Object> bounds = new ArrayList<>();
            for (String field : Arrays.asList("minX", "minY", "minZ", "maxX", "maxY", "maxZ"))
                bounds.add(s.call(box, field));
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

    private boolean valid(Object placement, int x, int z) throws Exception {
        return Boolean.TRUE.equals(
                world.structureState == world.generator
                        ? world.random == null
                                ? s.call(
                                        placement,
                                        "isFeatureChunk",
                                        world.generator,
                                        world.seed,
                                        x,
                                        z)
                                : s.call(
                                        placement,
                                        "isStructureChunk",
                                        world.generator,
                                        world.random,
                                        world.seed,
                                        x,
                                        z)
                        : s.call(placement, "isStructureChunk", world.structureState, x, z));
    }
}
