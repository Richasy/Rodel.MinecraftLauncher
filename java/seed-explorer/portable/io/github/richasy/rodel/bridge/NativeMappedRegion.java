package io.github.richasy.rodel.bridge;

import java.io.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Serializes generated native block states and properties without a numeric block-ID table. */
final class NativeMappedRegion {
    @SuppressWarnings("unchecked")
    static Map<String, Object> read(
            NativeMappedContext world, Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        Map<String, Object> bounds = (Map<String, Object>) query.get("bounds");
        int minX = number(bounds, "minX"),
                minY = number(bounds, "minY"),
                minZ = number(bounds, "minZ"),
                maxX = number(bounds, "maxX"),
                maxY = number(bounds, "maxY"),
                maxZ = number(bounds, "maxZ");
        boolean include = Boolean.TRUE.equals(query.get("includeBlocks"));
        long volume =
                ((long) maxX - minX + 1) * ((long) maxY - minY + 1) * ((long) maxZ - minZ + 1);
        if (minY < world.minimumY
                || maxY >= world.heightLimit
                || maxX < minX
                || maxY < minY
                || maxZ < minZ
                || volume > (include ? 4194304 : 16777216))
            throw new IllegalArgumentException("SEED_VOLUME_BUDGET");
        ByteArrayOutputStream blocks = new ByteArrayOutputStream(include ? (int) volume * 2 : 0);
        Map<Object, Integer> indices = new IdentityHashMap<>();
        List<Object> palette = new ArrayList<>();
        Map<String, Long> materials = new TreeMap<>();
        long started = System.nanoTime();
        NativeMappedSymbols s = world.s;
        Object registry = world.blockRegistry();
        world.report("scan", 0, maxY - minY + 1);
        for (int y = minY; y <= maxY; y++) {
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++) {
                    NativeMappedWorld.check(cancelled);
                    Object state = world.block(x, y, z);
                    Integer index = indices.get(state);
                    if (index == null) {
                        index = palette.size();
                        if (index >= 65536) throw new IOException("SEED_PALETTE_BUDGET");
                        Map<String, String> properties = new TreeMap<>();
                        Object values = s.call(state, "getValues");
                        if (values instanceof Map<?, ?>) {
                            for (Map.Entry<?, ?> property : ((Map<?, ?>) values).entrySet())
                                properties.put(
                                        (String) s.call(property.getKey(), "getName"),
                                        (String)
                                                s.call(
                                                        property.getKey(),
                                                        "getName",
                                                        property.getValue()));
                        } else {
                            try (java.util.stream.Stream<?> stream =
                                    (java.util.stream.Stream<?>) values) {
                                for (Object value :
                                        (Iterable<?>)
                                                stream.collect(
                                                        java.util.stream.Collectors.toList()))
                                    properties.put(
                                            (String) s.call(s.call(value, "property"), "getName"),
                                            (String) s.call(value, "valueName"));
                            }
                        }
                        palette.add(
                                Json.object(
                                        "id",
                                        s.call(registry, "getKey", s.call(state, "getBlock"))
                                                .toString(),
                                        "properties",
                                        properties));
                        indices.put(state, index);
                    }
                    String id = (String) ((Map<String, Object>) palette.get(index)).get("id");
                    materials.put(id, materials.getOrDefault(id, 0L) + 1);
                    if (include) {
                        blocks.write(index & 255);
                        blocks.write((index >>> 8) & 255);
                    }
                }
            world.report("scan", y - minY + 1, maxY - minY + 1);
        }
        List<Object> counts = new ArrayList<>();
        for (Map.Entry<String, Long> count : materials.entrySet())
            counts.add(Json.object("id", count.getKey(), "count", count.getValue()));
        return Json.object(
                "seed",
                query.get("seed"),
                "world",
                query.get("world"),
                "bounds",
                bounds,
                "generationPolicy",
                "fixed_region_v1",
                "palette",
                include ? palette : Collections.emptyList(),
                "blocks",
                Base64.getEncoder().encodeToString(blocks.toByteArray()),
                "materials",
                counts,
                "containers",
                Collections.emptyList(),
                "elapsedMilliseconds",
                (System.nanoTime() - started) / 1000000.0);
    }

    private static int number(Map<String, Object> value, String key) {
        return ((Number) value.get(key)).intValue();
    }
}
