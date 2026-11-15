package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.jar.*;

/** Discovers the unnamed interface-generator API from class-file, NBT and constructor evidence. */
final class NativePreRegistrySymbols {
    final NativeMappedSymbols mapped;
    final Map<String, LegacyClassFile> classes = new LinkedHashMap<>();
    final Class<?> worldType,
            dataType,
            settingsType,
            serverType,
            saveType,
            dimensionDataType,
            dimensionType;
    final Constructor<?> serverConstructor, settingsConstructor;
    final Method bootstrap, fixerFactory;
    final Object biomeRegistry, blockRegistry, fluidRegistry, structureRegistry;
    final Class<?> biomeType, blockType, fluidType, positionType;
    final Method registryKey, registryGet, biomeQuery;
    private final Set<Object> states =
            Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
    private final Set<Class<?>> properties = new HashSet<>();

    static NativePreRegistrySymbols tryDiscover(NativeMappedSymbols candidate, String originalJar)
            throws Exception {
        if (candidate.hasType("net.minecraft.server.level.ServerLevel")) return null;
        NativePreRegistrySymbols result;
        try {
            result = new NativePreRegistrySymbols(candidate, originalJar);
        } catch (UnsupportedShape absent) {
            return null;
        }
        return result;
    }

    private NativePreRegistrySymbols(NativeMappedSymbols mapped, String originalJar)
            throws Exception {
        this.mapped = mapped;
        try (JarFile jar = new JarFile(originalJar)) {
            Enumeration<JarEntry> entries = jar.entries();
            while (entries.hasMoreElements()) {
                JarEntry entry = entries.nextElement();
                if (!entry.getName().endsWith(".class")) continue;
                if (classes.size() >= 50000 || entry.getSize() > 4194304)
                    throw new IOException("SEED_CLASS_BUDGET");
                ByteArrayOutputStream bytes = new ByteArrayOutputStream();
                try (InputStream input = jar.getInputStream(entry)) {
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = input.read(buffer)) >= 0) {
                        if (bytes.size() + count > 4194304)
                            throw new IOException("SEED_CLASS_BUDGET");
                        bytes.write(buffer, 0, count);
                    }
                }
                LegacyClassFile type = new LegacyClassFile(bytes.toByteArray());
                classes.put(type.name, type);
            }
        }
        List<LegacyClassFile> worlds = matching("Unable to find spawn biome"),
                infos = matching("RandomSeed", "SpawnX", "SpawnY", "SpawnZ");
        if (worlds.size() != 1 || infos.size() != 1) throw new UnsupportedShape();
        dataType = load(infos.get(0).name);
        worldType = load(worlds.get(0).name);
        List<Constructor<?>> worldsAbi = new ArrayList<>();
        for (Constructor<?> c : worldType.getConstructors()) {
            Class<?>[] p = c.getParameterTypes();
            if (p.length == 6
                    && p[0].getName().equals("net.minecraft.server.MinecraftServer")
                    && p[3] == dataType
                    && !p[4].isPrimitive()) worldsAbi.add(c);
        }
        if (worldsAbi.size() != 1) throw new UnsupportedShape();
        Class<?>[] wp = worldsAbi.get(0).getParameterTypes();
        serverType = wp[0];
        saveType = wp[1];
        dimensionDataType = wp[2];
        dimensionType = wp[4];
        List<Constructor<?>> settings = new ArrayList<>();
        for (Constructor<?> data : dataType.getConstructors()) {
            Class<?>[] p = data.getParameterTypes();
            if (p.length != 2 || p[1] != String.class) continue;
            for (Constructor<?> c : p[0].getConstructors()) {
                Class<?>[] a = c.getParameterTypes();
                if (a.length == 5
                        && a[0] == long.class
                        && a[1].isEnum()
                        && a[2] == boolean.class
                        && a[3] == boolean.class) settings.add(c);
            }
        }
        settingsConstructor = only(settings, "INTERFACE_WORLD_SETTINGS");
        settingsType = settingsConstructor.getDeclaringClass();
        serverConstructor = only(Arrays.asList(serverType.getConstructors()), "INTERFACE_SERVER");
        if (serverConstructor.getParameterTypes().length != 8
                || !serverConstructor
                        .getParameterTypes()[2]
                        .getName()
                        .equals("com.mojang.datafixers.DataFixer")) throw new UnsupportedShape();
        List<Method> fixers = new ArrayList<>();
        for (LegacyClassFile type : classes.values())
            for (LegacyClassFile.Member member : type.methods)
                if ((member.access & 9) == 9
                        && member.descriptor.equals("()Lcom/mojang/datafixers/DataFixer;"))
                    fixers.add(load(type.name).getMethod(member.name));
        fixerFactory = only(fixers, "INTERFACE_FIXER");
        LegacyClassFile blocks =
                only(matching("Accessed Blocks before Bootstrap!"), "INTERFACE_BLOCKS");
        Class<?> boot = null;
        for (LegacyClassFile.Member member : blocks.methods)
            if (member.name.equals("<clinit>") && member.code != null)
                for (int p = 0; p < member.code.length; p = LegacyClassFile.next(member.code, p))
                    if ((member.code[p] & 255) == 0xb8) {
                        int ref = LegacyClassFile.u2(member.code, p + 1);
                        if (blocks.referenceType(ref).equals("()Z")) {
                            boot = load(blocks.referenceOwner(ref));
                            break;
                        }
                    }
        if (boot == null) throw new IOException("SEED_BOOTSTRAP_UNAVAILABLE");
        List<Method> boots = new ArrayList<>();
        for (Method method : boot.getMethods())
            if (Modifier.isStatic(method.getModifiers())
                    && method.getReturnType() == void.class
                    && method.getParameterTypes().length == 0) boots.add(method);
        bootstrap = only(boots, "INTERFACE_BOOTSTRAP");
        bootstrap.invoke(null);
        List<Class<?>> registryTypes = new ArrayList<>();
        for (LegacyClassFile type : matching("block", "biome", "item")) {
            Class<?> c = load(type.name);
            if (Iterable.class.isAssignableFrom(c)) registryTypes.add(c);
        }
        Class<?> registryType = only(registryTypes, "INTERFACE_REGISTRY");
        List<Method> keys = new ArrayList<>();
        for (Method method : registryType.getMethods())
            if (Arrays.equals(method.getParameterTypes(), new Class<?>[] {Object.class})
                    && !method.getReturnType().isPrimitive()) {
                try {
                    method.getReturnType().getConstructor(String.class, String.class);
                    keys.add(method);
                } catch (NoSuchMethodException absent) {
                }
            }
        registryKey = only(keys, "INTERFACE_REGISTRY_KEY");
        Class<?> identifier = registryKey.getReturnType();
        List<Method> gets = new ArrayList<>();
        for (Method method : registryType.getMethods())
            if (method.getReturnType() == Object.class
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {identifier}))
                gets.add(method);
        List<Object> roots = new ArrayList<>();
        for (Field field : registryType.getFields())
            if (Modifier.isStatic(field.getModifiers())
                    && registryType.isAssignableFrom(field.getType())) {
                Object value = field.get(null);
                if (!(value instanceof Iterable<?>)) continue;
                int size = 0;
                boolean allRegistries = true;
                for (Object entry : (Iterable<?>) value) {
                    size++;
                    if (!registryType.isInstance(entry)) allRegistries = false;
                }
                if (allRegistries && size > 0) roots.add(value);
            }
        Object root = only(roots, "INTERFACE_ROOT_REGISTRY");
        Map<String, Object> registries = new HashMap<>();
        alias("net.minecraft.core.Registry", registryType);
        alias("net.minecraft.resources.ResourceLocation", identifier);
        alias(registryKey, "getKey");
        for (Field field : registryType.getFields())
            if (Modifier.isStatic(field.getModifiers())
                    && registryType.isAssignableFrom(field.getType())) {
                Object value = field.get(null), key = registryKey.invoke(root, value);
                if (key == null) continue;
                String id = id(key);
                registries.put(id, value);
                alias(field, id.toUpperCase(Locale.ROOT));
            }
        biomeRegistry = required(registries, "biome");
        blockRegistry = required(registries, "block");
        fluidRegistry = required(registries, "fluid");
        Object absentKey =
                identifier
                        .getConstructor(String.class)
                        .newInstance("rodel_seed_explorer:absent_symbol_probe");
        List<Method> nullableGets = new ArrayList<>();
        for (Method method : gets)
            try {
                if (method.invoke(biomeRegistry, absentKey) == null) nullableGets.add(method);
            } catch (InvocationTargetException invalid) {
            }
        registryGet = only(nullableGets, "INTERFACE_REGISTRY_GET");
        alias(registryGet, "get");
        for (Object registry : registries.values())
            for (Method method : registry.getClass().getMethods()) {
                if (method.getName().equals(registryKey.getName())
                        && method.getReturnType() == registryKey.getReturnType()
                        && Arrays.equals(
                                method.getParameterTypes(), registryKey.getParameterTypes()))
                    alias(method, "getKey");
                if (method.getName().equals(registryGet.getName())
                        && method.getReturnType() == registryGet.getReturnType()
                        && Arrays.equals(
                                method.getParameterTypes(), registryGet.getParameterTypes()))
                    alias(method, "get");
            }
        biomeType = common(biomeRegistry);
        blockType = common(blockRegistry);
        fluidType = common(fluidRegistry);
        Object structureEntries = registries.get("structure_feature");
        if (structureEntries == null) {
            List<Class<?>> featureTypes = new ArrayList<>();
            for (Method query : biomeType.getMethods())
                if (query.getReturnType() == boolean.class
                        && query.getParameterTypes().length == 1) {
                    Class<?> type = query.getParameterTypes()[0];
                    if (type.getClassLoader() == null) continue;
                    for (Method name : type.getDeclaredMethods())
                        if (Modifier.isAbstract(name.getModifiers())
                                && name.getReturnType() == String.class
                                && name.getParameterTypes().length == 0) featureTypes.add(type);
                }
            Class<?> featureType =
                    only(new LinkedHashSet<>(featureTypes), "INTERFACE_STRUCTURE_TYPE");
            List<Object> maps = new ArrayList<>();
            for (LegacyClassFile type : classes.values()) {
                if (!Arrays.asList(type.text)
                                .containsAll(Arrays.asList("mineshaft", "village", "stronghold"))
                        && !Arrays.asList(type.text)
                                .containsAll(Arrays.asList("Mineshaft", "Village", "Stronghold")))
                    continue;
                for (Field field : load(type.name).getDeclaredFields())
                    if (Modifier.isStatic(field.getModifiers())
                            && Map.class.isAssignableFrom(field.getType())) {
                        field.setAccessible(true);
                        Object raw = field.get(null);
                        if (!(raw instanceof Map<?, ?>) || ((Map<?, ?>) raw).isEmpty()) continue;
                        boolean valid = true;
                        for (Map.Entry<?, ?> entry : ((Map<?, ?>) raw).entrySet())
                            if (!(entry.getKey() instanceof String)
                                    || !featureType.isInstance(entry.getValue())) valid = false;
                        if (valid && !maps.contains(raw)) maps.add(raw);
                    }
            }
            structureEntries = only(maps, "INTERFACE_STRUCTURE_ENTRIES");
        }
        structureRegistry = structureEntries;
        List<Method> biomeQueries = new ArrayList<>();
        for (Method method : worldType.getMethods())
            if (method.getReturnType() == biomeType && method.getParameterTypes().length == 1)
                try {
                    method.getParameterTypes()[0].getConstructor(int.class, int.class, int.class);
                    biomeQueries.add(method);
                } catch (NoSuchMethodException absent) {
                }
        biomeQuery = only(biomeQueries, "INTERFACE_BIOME_QUERY");
        positionType = biomeQuery.getParameterTypes()[0];
        alias("net.minecraft.core.BlockPos", positionType);
        alias("net.minecraft.server.level.ServerLevel", worldType);
        alias("net.minecraft.world.level.LevelSettings", settingsType);
        bindCoordinates(positionType);
    }

    private static Object required(Map<String, Object> values, String key) throws IOException {
        Object value = values.get(key);
        if (value == null) throw new IOException("SEED_REGISTRY_UNAVAILABLE: " + key);
        return value;
    }

    private static Class<?> common(Object registry) throws IOException {
        Class<?> base = null;
        for (Object value : (Iterable<?>) registry) {
            if (base == null) base = value.getClass();
            while (!base.isInstance(value)) base = base.getSuperclass();
        }
        if (base == null || base == Object.class)
            throw new IOException("SEED_REGISTRY_TYPE_UNAVAILABLE");
        return base;
    }

    String id(Object key) {
        String value = key.toString();
        return value.startsWith("minecraft:") ? value.substring(10) : value;
    }

    String key(Object registry, Object value) throws Exception {
        return id(registryKey.invoke(registry, value));
    }

    List<LegacyClassFile> matching(String... strings) {
        List<LegacyClassFile> result = new ArrayList<>();
        for (LegacyClassFile type : classes.values())
            if (Arrays.asList(type.text).containsAll(Arrays.asList(strings))) result.add(type);
        return result;
    }

    Class<?> load(String name) throws ClassNotFoundException {
        return Class.forName(name.replace('/', '.'), false, getClass().getClassLoader());
    }

    static <T> T only(Collection<T> values, String role) throws IOException {
        return NativeSeedSymbols.only(new ArrayList<>(values), role);
    }

    static Method method(Class<?> owner, Class<?> result, Class<?>... args) throws IOException {
        List<Method> methods = new ArrayList<>();
        for (Method method : owner.getMethods())
            if (method.getReturnType() == result && Arrays.equals(method.getParameterTypes(), args))
                methods.add(method);
        return only(
                methods,
                "NATIVE_METHOD_"
                        + owner.getName()
                        + "_"
                        + result.getName()
                        + Arrays.toString(args));
    }

    static List<Field> fields(Class<?> owner) {
        List<Field> result = new ArrayList<>();
        for (Class<?> c = owner; c != null && c.getClassLoader() != null; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers())) {
                    field.setAccessible(true);
                    result.add(field);
                }
        return result;
    }

    Object named(Class<?> owner, String name) throws Exception {
        List<Method> candidates = new ArrayList<>();
        for (Method method : owner.getMethods())
            if (Modifier.isStatic(method.getModifiers())
                    && !method.getName().equals("valueOf")
                    && method.getReturnType() == owner
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {String.class}))
                candidates.add(method);
        return only(candidates, "NATIVE_NAMED_" + owner.getName()).invoke(null, name);
    }

    void alias(String name, Class<?> type) {
        mapped.classes.put(name, type.getName());
    }

    void alias(Method method, String name) {
        mapped.names.put(mapped.key(method), name);
    }

    void alias(Field field, String name) {
        mapped.fields.put(field.getDeclaringClass().getName() + "#" + name, field.getName());
        for (Map.Entry<String, String> owner : mapped.classes.entrySet())
            if (owner.getValue().equals(field.getDeclaringClass().getName()))
                mapped.fields.put(owner.getKey() + "#" + name, field.getName());
    }

    private void bindCoordinates(Class<?> position) throws Exception {
        for (Class<?> c = position;
                c != null && c.getClassLoader() != null;
                c = c.getSuperclass()) {
            LegacyClassFile type = classes.get(c.getName().replace('.', '/'));
            if (type == null) continue;
            Map<Integer, String> written = new HashMap<>();
            for (LegacyClassFile.Member member : type.methods)
                if (member.name.equals("<init>")
                        && member.descriptor.equals("(III)V")
                        && member.code != null) {
                    int last = -1;
                    for (int p = 0;
                            p < member.code.length;
                            p = LegacyClassFile.next(member.code, p)) {
                        int opcode = member.code[p] & 255;
                        if (opcode >= 0x1b && opcode <= 0x1d) last = opcode - 0x1a;
                        if (opcode == 0xb5 && last >= 1 && last <= 3) {
                            int ref = LegacyClassFile.u2(member.code, p + 1);
                            if (type.referenceOwner(ref).equals(type.name)
                                    && type.referenceType(ref).equals("I"))
                                written.put(last, type.referenceName(ref));
                            last = -1;
                        }
                    }
                }
            if (written.size() != 3) continue;
            for (LegacyClassFile.Member member : type.methods)
                if (member.descriptor.equals("()I")
                        && member.code != null
                        && member.code.length == 5
                        && (member.code[1] & 255) == 0xb4) {
                    int ref = LegacyClassFile.u2(member.code, 2);
                    for (int i = 1; i <= 3; i++)
                        if (type.referenceName(ref).equals(written.get(i)))
                            alias(
                                    c.getMethod(member.name),
                                    i == 1 ? "getX" : i == 2 ? "getY" : "getZ");
                }
            return;
        }
        throw new IOException("SEED_POSITION_API_UNAVAILABLE");
    }

    void bindState(Object state) throws Exception {
        if (!states.add(state)) return;
        List<Method> values = new ArrayList<>();
        for (Method method : state.getClass().getMethods()) {
            if (method.getParameterTypes().length != 0
                    || Modifier.isAbstract(method.getModifiers())
                    || method.isBridge()) continue;
            if (method.getReturnType() == blockType) alias(method, "getBlock");
            if (Map.class.isAssignableFrom(method.getReturnType())) values.add(method);
        }
        Method map = only(values, "NATIVE_STATE_PROPERTIES");
        alias(map, "getValues");
        for (Object property : ((Map<?, ?>) map.invoke(state)).keySet()) bindProperty(property);
    }

    private void bindProperty(Object property) {
        if (!properties.add(property.getClass())) return;
        for (Method method : property.getClass().getMethods())
            if (!method.isBridge()
                    && !Modifier.isAbstract(method.getModifiers())
                    && !method.getName().equals("toString")
                    && method.getReturnType() == String.class
                    && method.getParameterTypes().length <= 1) alias(method, "getName");
    }

    boolean literal(LegacyClassFile owner, LegacyClassFile.Member method, String key) {
        if (method.code == null) return false;
        for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
            int op = method.code[p] & 255;
            if (op == 0x12 || op == 0x13) {
                int ref =
                        op == 0x12
                                ? method.code[p + 1] & 255
                                : LegacyClassFile.u2(method.code, p + 1);
                if (owner.tags[ref] == 8 && key.equals(owner.text[owner.first[ref]])) return true;
            }
        }
        return false;
    }

    Method diagnosticMethod(Class<?> owner, String key) throws Exception {
        LegacyClassFile shape = classes.get(owner.getName().replace('.', '/'));
        List<Method> found = new ArrayList<>();
        for (LegacyClassFile.Member method : shape.methods)
            if (method.descriptor.equals("()V") && literal(shape, method, key)) {
                Method nativeMethod = owner.getDeclaredMethod(method.name);
                nativeMethod.setAccessible(true);
                found.add(nativeMethod);
            }
        return only(found, "NATIVE_DIAGNOSTIC_" + key);
    }

    Field serializedField(Class<?> owner, String key, String descriptor) throws Exception {
        LegacyClassFile shape = classes.get(owner.getName().replace('.', '/'));
        Set<String> names = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : shape.methods)
            if (method.code != null) {
                boolean found = false;
                int budget = 0;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int op = method.code[p] & 255;
                    if (op == 0x12 || op == 0x13) {
                        int ref =
                                op == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        found = shape.tags[ref] == 8 && key.equals(shape.text[shape.first[ref]]);
                        budget = 0;
                    }
                    if (found && ++budget > 8) found = false;
                    if (found && op == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (shape.referenceOwner(ref).equals(shape.name)
                                && shape.referenceType(ref).equals(descriptor))
                            names.add(shape.referenceName(ref));
                        found = false;
                    }
                }
            }
        Field field = owner.getDeclaredField(only(names, "NATIVE_NBT_FIELD_" + key));
        field.setAccessible(true);
        return field;
    }

    private static final class UnsupportedShape extends Exception {
        private static final long serialVersionUID = 1L;
    }
}
