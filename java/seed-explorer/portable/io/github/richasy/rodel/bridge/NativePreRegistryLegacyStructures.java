package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Calls the original structure lookup/placement/start routine of the interface-generator API. */
final class NativePreRegistryLegacyStructures extends NativeStructureSource {
    private final NativePreRegistryLegacyWorld world;
    private final Map<String, Entry> entries = new TreeMap<>();

    NativePreRegistryLegacyStructures(NativePreRegistryLegacyWorld world) throws Exception {
        this.world = world;
        if (!(world.symbols.structureRegistry instanceof Map<?, ?>))
            throw new IOException("SEED_NATIVE_STRUCTURE_MAP_UNAVAILABLE");
        for (Map.Entry<?, ?> value : ((Map<?, ?>) world.symbols.structureRegistry).entrySet()) {
            Object feature = value.getValue();
            Class<?> featureType = null;
            for (Class<?> type = feature.getClass();
                    type != null && type.getClassLoader() != null;
                    type = type.getSuperclass())
                for (Method method : type.getDeclaredMethods())
                    if (Modifier.isPrivate(method.getModifiers())
                            && method.getParameterTypes().length == 4
                            && method.getParameterTypes()[3] == long.class
                            && method.getParameterTypes()[0].isInstance(world.world)
                            && method.getParameterTypes()[1].isInstance(world.generator)
                            && java.util.Random.class.isAssignableFrom(
                                    method.getParameterTypes()[2])) featureType = type;
            if (featureType == null) continue;
            Method supported =
                    NativePreRegistrySymbols.method(
                            world.source.getClass(), boolean.class, featureType);
            if (!Boolean.TRUE.equals(supported.invoke(world.source, feature))) continue;
            String id =
                    value.getKey()
                            .toString()
                            .replaceAll("([a-z])([A-Z])", "$1_$2")
                            .toLowerCase(Locale.ROOT);
            if (entries.put(id, new Entry(feature, featureType)) != null)
                throw new IOException("SEED_NATIVE_STRUCTURE_ID_AMBIGUOUS");
        }
    }

    @Override
    Set<String> ids() {
        return Collections.unmodifiableSet(entries.keySet());
    }

    @Override
    List<String> biomes(String id) throws Exception {
        Entry entry = entries.get(id);
        if (entry == null) return Collections.emptyList();
        List<String> result = new ArrayList<>();
        Method allowed =
                NativePreRegistrySymbols.method(
                        world.symbols.biomeType, boolean.class, entry.featureType);
        for (Map.Entry<Object, String> biome : world.biomeIds.entrySet())
            if (Boolean.TRUE.equals(allowed.invoke(biome.getKey(), entry.feature)))
                result.add(biome.getValue());
        Collections.sort(result);
        return result;
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
        List<Map<String, Object>> result = new ArrayList<>();
        Method packed =
                NativePreRegistrySymbols.method(
                        world.chunkPosition.getDeclaringClass(), long.class);
        for (Map.Entry<String, Entry> feature : entries.entrySet()) {
            if (targets != null && !targets.contains(feature.getKey())) continue;
            Entry entry = feature.getValue();
            Object random = entry.random.newInstance();
            for (int z = minCz; z <= maxCz; z++)
                for (int x = minCx; x <= maxCx; x++) {
                    NativePreRegistryLegacyWorld.check(cancelled);
                    long position =
                            ((Number) packed.invoke(world.chunkPosition.newInstance(x, z)))
                                    .longValue();
                    Object start =
                            entry.generate.invoke(
                                    entry.feature, world.world, world.generator, random, position);
                    if (start == entry.invalid) continue;
                    Object box = entry.bounds.invoke(start);
                    int[] bounds = (int[]) entry.array.invoke(entry.boxArray.invoke(box));
                    if (bounds.length != 6)
                        throw new IOException("SEED_NATIVE_STRUCTURE_BOUNDS_INVALID");
                    int wx = (int) Math.floorDiv((long) bounds[0] + bounds[3] + 1, 2),
                            y = (int) Math.floorDiv((long) bounds[1] + bounds[4] + 1, 2),
                            wz = (int) Math.floorDiv((long) bounds[2] + bounds[5] + 1, 2);
                    if (wx < minX || wx > maxX || wz < minZ || wz > maxZ) continue;
                    result.add(
                            Json.object(
                                    "id",
                                    feature.getKey(),
                                    "kind",
                                    "structure",
                                    "x",
                                    wx,
                                    "y",
                                    y,
                                    "z",
                                    wz,
                                    "pieces",
                                    ((List<?>) entry.pieces.invoke(start)).size(),
                                    "bounds",
                                    Arrays.asList(
                                            bounds[0], bounds[1], bounds[2], bounds[3], bounds[4],
                                            bounds[5])));
                }
        }
        return result;
    }

    private static final class Entry {
        final Object feature, invalid;
        final Class<?> featureType;
        final Method generate, bounds, pieces, boxArray, array;
        final Constructor<?> random;

        Entry(Object feature, Class<?> featureType) throws Exception {
            this.feature = feature;
            this.featureType = featureType;
            List<Method> generate = new ArrayList<>();
            for (Method method : featureType.getDeclaredMethods())
                if (Modifier.isPrivate(method.getModifiers())
                        && method.getParameterTypes().length == 4
                        && method.getParameterTypes()[3] == long.class
                        && Random.class.isAssignableFrom(method.getParameterTypes()[2]))
                    generate.add(method);
            this.generate = NativePreRegistrySymbols.only(generate, "NATIVE_STRUCTURE_LOOKUP");
            this.generate.setAccessible(true);
            random = this.generate.getParameterTypes()[2].getConstructor();
            Class<?> startType = this.generate.getReturnType();
            List<Object> invalids = new ArrayList<>();
            for (Field field : featureType.getDeclaredFields())
                if (Modifier.isStatic(field.getModifiers()) && field.getType() == startType) {
                    field.setAccessible(true);
                    invalids.add(field.get(null));
                }
            invalid = NativePreRegistrySymbols.only(invalids, "NATIVE_INVALID_STRUCTURE");
            List<Method> bounds = new ArrayList<>(), pieces = new ArrayList<>();
            for (Method method : startType.getMethods())
                if (method.getParameterTypes().length == 0) {
                    if (List.class.isAssignableFrom(method.getReturnType())) pieces.add(method);
                    try {
                        method.getReturnType()
                                .getConstructor(
                                        int.class, int.class, int.class, int.class, int.class,
                                        int.class);
                        bounds.add(method);
                    } catch (NoSuchMethodException absent) {
                    }
                }
            this.bounds = NativePreRegistrySymbols.only(bounds, "NATIVE_STRUCTURE_BOX");
            this.pieces = NativePreRegistrySymbols.only(pieces, "NATIVE_STRUCTURE_PIECES");
            List<Method> boxes = new ArrayList<>(), arrays = new ArrayList<>();
            for (Method method : this.bounds.getReturnType().getMethods())
                if (method.getParameterTypes().length == 0)
                    for (Method read : method.getReturnType().getMethods())
                        if (read.getParameterTypes().length == 0
                                && read.getReturnType() == int[].class) {
                            boxes.add(method);
                            arrays.add(read);
                        }
            boxArray = NativePreRegistrySymbols.only(boxes, "NATIVE_STRUCTURE_NBT_BOX");
            array = NativePreRegistrySymbols.only(arrays, "NATIVE_STRUCTURE_NBT_ARRAY");
        }
    }
}
