package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Observes real native inventory storage and invokes only a proven item getter for loot. */
final class NativePreRegistryLegacyInventory {
    static void read(NativePreRegistryLegacyWorld world, boolean loot, AtomicBoolean cancelled)
            throws Exception {
        Set<Object> seen = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        List<Object> entities = new ArrayList<>();
        for (Field list : NativePreRegistrySymbols.fields(world.world.getClass())) {
            if (!List.class.isAssignableFrom(list.getType())) continue;
            Object raw = list.get(world.world);
            if (!(raw instanceof List<?>)) continue;
            entities.addAll((List<?>) raw);
        }
        for (Object chunk : world.generatedChunks()) {
            List<Method> positions = new ArrayList<>(), entitiesAt = new ArrayList<>();
            for (Method method : chunk.getClass().getMethods()) {
                if (method.getParameterTypes().length == 0
                        && Set.class.isAssignableFrom(method.getReturnType())
                        && method.getGenericReturnType() instanceof ParameterizedType)
                    for (Type type :
                            ((ParameterizedType) method.getGenericReturnType())
                                    .getActualTypeArguments())
                        if (type == world.symbols.positionType) positions.add(method);
                if (Arrays.equals(
                        method.getParameterTypes(), new Class<?>[] {world.symbols.positionType})) {
                    LegacyClassFile type =
                            world.symbols.classes.get(
                                    method.getReturnType().getName().replace('.', '/'));
                    if (type != null
                            && Arrays.asList(type.text)
                                    .containsAll(Arrays.asList("id", "x", "y", "z")))
                        entitiesAt.add(method);
                }
            }
            if (!positions.isEmpty() && !entitiesAt.isEmpty()) {
                Method
                        all =
                                NativePreRegistrySymbols.only(
                                        positions, "NATIVE_BLOCK_ENTITY_POSITIONS"),
                        entityAt =
                                NativePreRegistrySymbols.only(
                                        entitiesAt, "NATIVE_BLOCK_ENTITY_READ");
                for (Object position : (Set<?>) all.invoke(chunk)) {
                    NativePreRegistryLegacyWorld.check(cancelled);
                    Object entity = entityAt.invoke(chunk, position);
                    if (entity != null) entities.add(entity);
                }
            }
            for (Field field : NativePreRegistrySymbols.fields(chunk.getClass()))
                if (Map.class.isAssignableFrom(field.getType())) {
                    Object value = field.get(chunk);
                    if (!(value instanceof Map<?, ?>)) continue;
                    for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet())
                        if (world.symbols.positionType.isInstance(entry.getKey()))
                            entities.add(entry.getValue());
                }
        }
        for (Object entity : entities) {
            NativePreRegistryLegacyWorld.check(cancelled);
            if (entity == null || entity.getClass().getClassLoader() == null || !seen.add(entity))
                continue;
            if (seen.size() > 10000) throw new IOException("SEED_INVENTORY_BUDGET");
            List<Integer> capacities = new ArrayList<>();
            for (Field field : NativePreRegistrySymbols.fields(entity.getClass()))
                if (List.class.isAssignableFrom(field.getType())
                        && field.getGenericType() instanceof ParameterizedType)
                    for (Type argument :
                            ((ParameterizedType) field.getGenericType()).getActualTypeArguments())
                        if (argument instanceof Class<?> && stack(world, (Class<?>) argument)) {
                            Object value = field.get(entity);
                            if (value instanceof List<?>) capacities.add(((List<?>) value).size());
                        }
            if (capacities.isEmpty()) continue;
            if (loot) {
                List<Method> getters = new ArrayList<>();
                for (Class<?> type = entity.getClass();
                        type != null && type.getClassLoader() != null;
                        type = type.getSuperclass()) {
                    LegacyClassFile owner =
                            world.symbols.classes.get(type.getName().replace('.', '/'));
                    if (owner == null) continue;
                    for (LegacyClassFile.Member method : owner.methods)
                        if ((method.access & 1) != 0
                                && method.descriptor.startsWith("(I)L")
                                && method.code != null) {
                            Class<?> result =
                                    world.symbols.load(
                                            method.descriptor.substring(
                                                    4, method.descriptor.length() - 1));
                            if (!stack(world, result)) continue;
                            boolean getter = false, mutation = false;
                            for (int p = 0;
                                    p < method.code.length;
                                    p = LegacyClassFile.next(method.code, p)) {
                                int op = method.code[p] & 255;
                                if (op == 0xb5 || op == 0x53) mutation = true;
                                if (op == 0xb6 || op == 0xb9) {
                                    int ref = LegacyClassFile.u2(method.code, p + 1);
                                    if (owner.referenceName(ref).equals("get")
                                            && owner.referenceType(ref)
                                                    .equals("(I)Ljava/lang/Object;")) getter = true;
                                }
                            }
                            if (getter && !mutation)
                                getters.add(type.getMethod(method.name, int.class));
                        }
                    if (!getters.isEmpty()) break;
                }
                if (!getters.isEmpty())
                    NativePreRegistrySymbols.only(getters, "NATIVE_INVENTORY_GETTER")
                            .invoke(entity, 0);
            }
            List<Method> serializers = new ArrayList<>();
            for (Method method : entity.getClass().getMethods())
                if (method.getParameterTypes().length == 1
                        && method.getReturnType() == method.getParameterTypes()[0]
                        && method.getReturnType() != Object.class
                        && !Modifier.isStatic(method.getModifiers())) {
                    LegacyClassFile tag =
                            world.symbols.classes.get(
                                    method.getReturnType().getName().replace('.', '/'));
                    if (tag != null && Arrays.asList(tag.text).contains("java/util/Map"))
                        serializers.add(method);
                }
            if (serializers.isEmpty()) continue;
            Method serializer = NativePreRegistrySymbols.only(serializers, "NATIVE_INVENTORY_SAVE");
            Object tag = serializer.getReturnType().getConstructor().newInstance();
            serializer.invoke(entity, tag);
            List<Method> keys = new ArrayList<>();
            for (Method method : tag.getClass().getMethods())
                if (method.getParameterTypes().length == 0
                        && Set.class.isAssignableFrom(method.getReturnType())) keys.add(method);
            if (!((Set<?>) NativePreRegistrySymbols.only(keys, "NATIVE_NBT_KEYS").invoke(tag))
                    .containsAll(Arrays.asList("x", "y", "z"))) continue;
            Method integer =
                    NativePreRegistrySymbols.method(tag.getClass(), int.class, String.class);
            int x = ((Number) integer.invoke(tag, "x")).intValue(),
                    y = ((Number) integer.invoke(tag, "y")).intValue(),
                    z = ((Number) integer.invoke(tag, "z")).intValue();
            Object block = world.s.call(world.block(x, y, z), "getBlock");
            world.inventories.add(
                    Json.object(
                            "x",
                            x,
                            "y",
                            y,
                            "z",
                            z,
                            "slots",
                            NativePreRegistrySymbols.only(capacities, "NATIVE_INVENTORY_SIZE"),
                            "id",
                            world.symbols
                                    .registryKey
                                    .invoke(world.symbols.blockRegistry, block)
                                    .toString()));
        }
    }

    private static boolean stack(NativePreRegistryLegacyWorld world, Class<?> type) {
        LegacyClassFile value = world.symbols.classes.get(type.getName().replace('.', '/'));
        return value != null
                && Arrays.asList(value.text).containsAll(Arrays.asList("id", "Count", "Damage"));
    }
}
