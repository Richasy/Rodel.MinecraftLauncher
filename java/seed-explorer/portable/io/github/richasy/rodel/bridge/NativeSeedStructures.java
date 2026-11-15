package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Enumerates original structure starts through each original map generator, without recreating
 * placement algorithms.
 */
final class NativeSeedStructures extends NativeStructureSource {
    private final Map<String, Entry> entries = new LinkedHashMap<>();
    private final Object world;

    NativeSeedStructures(NativeSeedSymbols symbols, NativeSeedWorld original) throws Exception {
        this(symbols.classes, original.world, original.generator);
    }

    NativeSeedStructures(Map<String, LegacyClassFile> classes, Object world, Object baseGenerator)
            throws Exception {
        this.world = world;
        for (Field field : NativeSeedSymbols.fields(baseGenerator.getClass())) {
            Object generator = field.get(baseGenerator);
            if (generator == null || generator.getClass().getClassLoader() == null) continue;
            Class<?> start = null;
            for (Class<?> type = generator.getClass();
                    type != null && type.getClassLoader() != null;
                    type = type.getSuperclass())
                for (Method method : type.getDeclaredMethods())
                    if (Modifier.isAbstract(method.getModifiers())
                            && !method.getReturnType().isPrimitive()
                            && Arrays.equals(
                                    method.getParameterTypes(),
                                    new Class<?>[] {int.class, int.class}))
                        start = method.getReturnType();
            if (start == null) continue;
            List<Method> names = new ArrayList<>(), generate = new ArrayList<>();
            for (Method method : generator.getClass().getMethods()) {
                if (method.getReturnType() == String.class
                        && method.getParameterTypes().length == 0
                        && !method.getDeclaringClass().equals(Object.class)) names.add(method);
                Class<?>[] args = method.getParameterTypes();
                if (!Modifier.isStatic(method.getModifiers())
                        && method.getReturnType() == void.class
                        && (args.length == 4
                                        && args[0].isInstance(world)
                                        && args[1] == int.class
                                        && args[2] == int.class
                                        && !args[3].isPrimitive()
                                || args.length == 5
                                        && args[0].isInstance(baseGenerator)
                                        && args[1].isInstance(world)
                                        && args[2] == int.class
                                        && args[3] == int.class
                                        && !args[4].isPrimitive())) generate.add(method);
            }
            String nativeId =
                    names.isEmpty()
                            ? "legacy:structure_"
                                    + generator.getClass().getSimpleName().toLowerCase(Locale.ROOT)
                            : (String)
                                    NativeSeedSymbols.only(names, "STRUCTURE_NAME")
                                            .invoke(generator);
            Method generateMethod = NativeSeedSymbols.only(generate, "STRUCTURE_GENERATION");
            List<Field> maps = new ArrayList<>(), ranges = new ArrayList<>();
            for (Field member : NativeSeedSymbols.fields(generator.getClass())) {
                if (Map.class.isAssignableFrom(member.getType())
                        && member.getGenericType() instanceof ParameterizedType) {
                    for (Type argument :
                            ((ParameterizedType) member.getGenericType()).getActualTypeArguments())
                        if (argument.equals(start)) maps.add(member);
                }
                if (member.getType() == int.class)
                    for (Method declared : member.getDeclaringClass().getDeclaredMethods())
                        if (declared.getName().equals(generateMethod.getName())
                                && declared.getReturnType() == void.class
                                && Arrays.equals(
                                        declared.getParameterTypes(),
                                        generateMethod.getParameterTypes())) {
                            ranges.add(member);
                            break;
                        }
            }
            if (maps.isEmpty())
                for (Field member : NativeSeedSymbols.fields(generator.getClass()))
                    if (Map.class.isAssignableFrom(member.getType())) maps.add(member);
            Field map = NativeSeedSymbols.only(maps, "STRUCTURE_START_MAP");
            int range = NativeSeedSymbols.only(ranges, "STRUCTURE_RANGE").getInt(generator);
            if (range < 0 || range > 64) throw new IOException("SEED_STRUCTURE_RANGE_INVALID");
            String id = nativeId.replaceAll("([a-z])([A-Z])", "$1_$2").toLowerCase(Locale.ROOT);
            if (entries.put(
                            id,
                            new Entry(
                                    classes,
                                    generator,
                                    baseGenerator,
                                    generateMethod,
                                    map,
                                    start,
                                    range))
                    != null) throw new IOException("SEED_STRUCTURE_ID_AMBIGUOUS");
        }
    }

    Set<String> ids() {
        return entries.keySet();
    }

    List<Map<String, Object>> find(
            int minX, int minZ, int maxX, int maxZ, List<String> targets, AtomicBoolean cancelled)
            throws Exception {
        int minCx = Math.floorDiv(minX - 128, 16),
                minCz = Math.floorDiv(minZ - 128, 16),
                maxCx = Math.floorDiv(maxX + 128, 16),
                maxCz = Math.floorDiv(maxZ + 128, 16);
        if ((long) (maxCx - minCx + 1) * (maxCz - minCz + 1) > 1100000)
            throw new IllegalArgumentException(
                    "Zoom in before requesting dense original structure starts.");
        List<Map<String, Object>> result = new ArrayList<>();
        for (Map.Entry<String, Entry> pair : entries.entrySet()) {
            if (targets != null && !targets.contains(pair.getKey())) continue;
            Entry entry = pair.getValue();
            int stride = entry.range * 2 + 1;
            for (int z = minCz + entry.range; z <= maxCz + entry.range; z += stride)
                for (int x = minCx + entry.range; x <= maxCx + entry.range; x += stride) {
                    if (cancelled.get()) throw new java.util.concurrent.CancellationException();
                    if (entry.generate.getParameterTypes().length == 5)
                        entry.generate.invoke(entry.generator, entry.provider, world, x, z, null);
                    else entry.generate.invoke(entry.generator, world, x, z, null);
                }
            for (Object value : ((Map<?, ?>) entry.starts.get(entry.generator)).values()) {
                if (!Boolean.TRUE.equals(entry.valid.invoke(value))) continue;
                Object box = entry.bounds.invoke(value);
                int[] bounds =
                        entry.plainBounds != null
                                ? entry.plainBounds.read(box)
                                : (int[]) entry.array.invoke(entry.boxArray.invoke(box));
                if (bounds.length != 6) throw new IOException("SEED_STRUCTURE_BOUNDS_INVALID");
                int x = (int) Math.floorDiv((long) bounds[0] + bounds[3], 2),
                        y = (int) Math.floorDiv((long) bounds[1] + bounds[4], 2),
                        z = (int) Math.floorDiv((long) bounds[2] + bounds[5], 2);
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;
                int pieces = entry.pieceCount(value);
                result.add(
                        Json.object(
                                "id",
                                pair.getKey(),
                                "kind",
                                "structure",
                                "x",
                                x,
                                "y",
                                y,
                                "z",
                                z,
                                "pieces",
                                pieces,
                                "bounds",
                                Arrays.asList(
                                        bounds[0], bounds[1], bounds[2], bounds[3], bounds[4],
                                        bounds[5])));
            }
        }
        return result;
    }

    private static final class Entry {
        final Object generator, provider;
        final Method generate, valid, bounds, pieces, boxArray, array;
        final Field starts, pieceStorage;
        final NativeAncientBounds plainBounds;
        final int range;

        Entry(
                Map<String, LegacyClassFile> classes,
                Object generator,
                Object provider,
                Method generate,
                Field starts,
                Class<?> start,
                int range)
                throws Exception {
            this.generator = generator;
            this.provider = provider;
            this.generate = generate;
            this.starts = starts;
            this.range = range;
            List<Method> valid = new ArrayList<>(),
                    boxes = new ArrayList<>(),
                    pieces = new ArrayList<>();
            for (Method method : start.getMethods())
                if (method.getParameterTypes().length == 0) {
                    if (method.getReturnType() == boolean.class) valid.add(method);
                    if (List.class.isAssignableFrom(method.getReturnType())) pieces.add(method);
                    try {
                        method.getReturnType()
                                .getConstructor(
                                        int.class, int.class, int.class, int.class, int.class,
                                        int.class);
                        boxes.add(method);
                    } catch (NoSuchMethodException absent) {
                    }
                }
            this.valid = NativeSeedSymbols.only(valid, "STRUCTURE_VALIDITY");
            this.bounds = NativeSeedSymbols.only(boxes, "STRUCTURE_BOUNDS");
            if (pieces.isEmpty()) {
                List<Field> lists = new ArrayList<>();
                for (Field field : NativeSeedSymbols.fields(start))
                    if (!Modifier.isStatic(field.getModifiers())
                            && List.class.isAssignableFrom(field.getType())) lists.add(field);
                pieceStorage = NativeSeedSymbols.only(lists, "STRUCTURE_PIECE_STORAGE");
                this.pieces = null;
            } else {
                pieceStorage = null;
                this.pieces = NativeSeedSymbols.only(pieces, "STRUCTURE_PIECES");
            }
            List<Method> arrays = new ArrayList<>();
            for (Method method : bounds.getReturnType().getMethods())
                if (method.getParameterTypes().length == 0
                        && !method.getReturnType().isPrimitive()) {
                    for (Method accessor : method.getReturnType().getMethods())
                        if (accessor.getParameterTypes().length == 0
                                && accessor.getReturnType() == int[].class) {
                            arrays.add(method);
                            break;
                        }
                }
            if (arrays.isEmpty()) {
                boxArray = null;
                array = null;
                plainBounds = new NativeAncientBounds(classes, bounds.getReturnType());
                return;
            }
            plainBounds = null;
            boxArray = NativeSeedSymbols.only(arrays, "STRUCTURE_BOUNDS_ARRAY");
            arrays.clear();
            for (Method method : boxArray.getReturnType().getMethods())
                if (method.getParameterTypes().length == 0 && method.getReturnType() == int[].class)
                    arrays.add(method);
            array = NativeSeedSymbols.only(arrays, "NBT_INTEGER_ARRAY");
        }

        /** Reads the native piece list through its accessor or the original unexposed storage. */
        int pieceCount(Object start) throws Exception {
            return ((List<?>) (pieces == null ? pieceStorage.get(start) : pieces.invoke(start)))
                    .size();
        }
    }
}
