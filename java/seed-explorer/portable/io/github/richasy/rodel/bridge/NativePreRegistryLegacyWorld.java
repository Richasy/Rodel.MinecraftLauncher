package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Original interface-based chunk generation, discovered without a release-to-symbol table. */
final class NativePreRegistryLegacyWorld extends NativeMappedContext {
    final NativePreRegistrySymbols symbols;
    final Object world, server, source;
    final Class<?> generatorType, chunkType, protoType, chunkAccess;
    final Constructor<?> protoConstructor, chunkPosition;
    final Object upgrade, surface;
    final Method create, height, rawBlock, fullBlock, fullQuery;
    private final Object data, store;
    private final Method biomeSourceQuery;
    private final Object defaultBiome;
    private final String dimension;
    private final Method stop;
    private final Map<Long, Object> fullChunks = new HashMap<>();
    private final Map<Long, Object> rawChunks =
            new LinkedHashMap<Long, Object>(128, .75f, true) {
                @Override
                protected boolean removeEldestEntry(Map.Entry<Long, Object> entry) {
                    return size() > 128;
                }
            };
    private boolean closed;

    NativePreRegistryLegacyWorld(
            NativePreRegistrySymbols symbols, File directory, long seed, Map<String, Object> config)
            throws Exception {
        super(symbols.mapped, directory, seed);
        this.symbols = symbols;
        dimension = Json.string(config, "dimension");
        if (!directory.mkdirs() && !directory.isDirectory())
            throw new IOException("SEED_WORKSPACE_UNAVAILABLE");
        Object fixer = symbols.fixerFactory.invoke(null);
        Class<?>[] parameters = symbols.serverConstructor.getParameterTypes();
        Object commands = parameters[3].getConstructor(boolean.class).newInstance(true);
        Class<?> authenticationType = parameters[4];
        Object authentication =
                authenticationType
                        .getConstructor(java.net.Proxy.class, String.class)
                        .newInstance(java.net.Proxy.NO_PROXY, "seed-explorer");
        Object
                session =
                        authenticationType
                                .getMethod("createMinecraftSessionService")
                                .invoke(authentication),
                repository =
                        authenticationType
                                .getMethod("createProfileRepository")
                                .invoke(authentication);
        Object profile =
                NativePreRegistrySymbols.only(
                                Arrays.asList(parameters[7].getConstructors()),
                                "NATIVE_PROFILE_CACHE")
                        .newInstance(repository, new File(directory, "usercache.json"));
        Set<Method> enabled = new HashSet<>();
        for (LegacyClassFile type : symbols.classes.values())
            if (type.parent != null && type.parent.equals("net/minecraft/server/MinecraftServer"))
                for (LegacyClassFile.Member member : type.methods)
                    if (member.descriptor.equals("()Z")
                            && symbols.literal(type, member, "generate-structures")) {
                        Method method = symbols.serverType.getMethod(member.name);
                        if (Modifier.isAbstract(method.getModifiers())) enabled.add(method);
                    }
        server =
                NativeSeedHostClass.create(symbols.serverConstructor, enabled)
                        .getConstructors()[0]
                        .newInstance(
                                directory,
                                java.net.Proxy.NO_PROXY,
                                fixer,
                                commands,
                                authentication,
                                session,
                                repository,
                                profile);
        stop = symbols.diagnosticMethod(symbols.serverType, "Stopping server");
        try {
            List<Class<?>> playerTypes = new ArrayList<>();
            for (Field field : NativePreRegistrySymbols.fields(symbols.serverType)) {
                LegacyClassFile type =
                        symbols.classes.get(field.getType().getName().replace('.', '/'));
                if (type != null
                        && Arrays.asList(type.text)
                                .containsAll(
                                        Arrays.asList("banned-players.json", "banned-ips.json")))
                    playerTypes.add(field.getType());
            }
            Class<?> playerType = NativePreRegistrySymbols.only(playerTypes, "NATIVE_PLAYER_LIST");
            Constructor<?> playerConstructor =
                    NativePreRegistrySymbols.only(
                            Arrays.asList(playerType.getConstructors()), "NATIVE_PLAYER_LIST_CTOR");
            Object players =
                    NativeSeedHostClass.create(playerConstructor)
                            .getConstructors()[0]
                            .newInstance(
                                    playerConstructor.getParameterTypes().length == 1
                                            ? new Object[] {server}
                                            : new Object[] {server, 0});
            NativePreRegistrySymbols.method(symbols.serverType, void.class, playerType)
                    .invoke(server, players);
            String preset = Json.string(config, "preset"),
                    nativePreset =
                            preset.equals("normal")
                                    ? "default"
                                    : preset.equals("large_biomes")
                                            ? "largeBiomes"
                                            : preset.equals("single_biome") ? "buffet" : preset;
            Object type =
                    symbols.named(symbols.settingsConstructor.getParameterTypes()[4], nativePreset);
            if (type == null)
                throw new IllegalArgumentException("Unregistered original world type.");
            Object settings =
                    symbols.settingsConstructor.newInstance(
                            seed,
                            symbols.named(
                                    symbols.settingsConstructor.getParameterTypes()[1], "creative"),
                            true,
                            false,
                            type);
            if (preset.equals("single_biome")) {
                String id = Json.string(config, "biome");
                Object identifier =
                        s.make(
                                "net.minecraft.resources.ResourceLocation",
                                id.contains(":") ? id : "minecraft:" + id);
                if (symbols.registryGet.invoke(symbols.biomeRegistry, identifier) == null)
                    throw new IllegalArgumentException("Unregistered original biome.");
                String json =
                        Json.encode(
                                Json.object(
                                        "chunk_generator",
                                        Json.object("type", "minecraft:surface"),
                                        "biome_source",
                                        Json.object(
                                                "type",
                                                "minecraft:fixed",
                                                "options",
                                                Json.object(
                                                        "biomes",
                                                        Arrays.asList(
                                                                id.contains(":")
                                                                        ? id
                                                                        : "minecraft:" + id)))));
                Object options = s.call(s.make("com.google.gson.JsonParser"), "parse", json);
                NativePreRegistrySymbols.method(
                                symbols.settingsType,
                                symbols.settingsType,
                                Class.forName("com.google.gson.JsonElement"))
                        .invoke(settings, options);
            }
            data =
                    symbols.dataType
                            .getConstructor(symbols.settingsType, String.class)
                            .newInstance(settings, "Seed exploration");
            List<Object> stores = new ArrayList<>();
            for (Method getter : symbols.serverType.getMethods())
                if (getter.getParameterTypes().length == 0)
                    for (Method factory : getter.getReturnType().getMethods())
                        if (factory.getReturnType() == symbols.saveType
                                && Arrays.equals(
                                        factory.getParameterTypes(),
                                        new Class<?>[] {String.class, symbols.serverType}))
                            stores.add(factory.invoke(getter.invoke(server), "preview", server));
            store = NativePreRegistrySymbols.only(stores, "NATIVE_LEVEL_STORAGE");
            Object dimensions =
                    symbols.dimensionDataType.getConstructor(symbols.saveType).newInstance(store);
            NativePreRegistrySymbols.method(
                            symbols.serverType, void.class, File.class, symbols.dataType)
                    .invoke(server, new File(directory, "preview"), data);
            NativePreRegistrySymbols.method(
                            symbols.serverType,
                            void.class,
                            symbols.saveType,
                            symbols.dimensionDataType,
                            symbols.dataType,
                            symbols.settingsType)
                    .invoke(server, store, dimensions, data, settings);
            int dimensionId =
                    dimension.equals("overworld")
                            ? 0
                            : dimension.equals("the_nether")
                                    ? -1
                                    : dimension.equals("the_end") ? 1 : Integer.MIN_VALUE;
            if (dimensionId == Integer.MIN_VALUE)
                throw new IllegalArgumentException("Unknown original dimension.");
            Object selectedDimension =
                    NativePreRegistrySymbols.method(
                                    symbols.dimensionType, symbols.dimensionType, int.class)
                            .invoke(null, dimensionId);
            world =
                    NativePreRegistrySymbols.method(
                                    symbols.serverType, symbols.worldType, symbols.dimensionType)
                            .invoke(server, selectedDimension);
            List<Object> generators = new ArrayList<>();
            List<Class<?>> generatorTypes = new ArrayList<>();
            Set<Object> providers =
                    Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
            for (Field field : NativePreRegistrySymbols.fields(symbols.worldType)) {
                Object provider = field.get(world);
                if (provider == null
                        || provider.getClass().getClassLoader() == null
                        || !providers.add(provider)) continue;
                for (Field member : NativePreRegistrySymbols.fields(provider.getClass())) {
                    Class<?> candidate = member.getType();
                    if (!candidate.isInterface()) continue;
                    boolean seedApi = false, chunkApi = false;
                    for (Method method : candidate.getMethods()) {
                        if (method.getReturnType() == long.class
                                && method.getParameterTypes().length == 0) seedApi = true;
                        if (method.getReturnType() == void.class
                                && method.getParameterTypes().length == 1
                                && method.getParameterTypes()[0].isInterface()
                                && method.getParameterTypes()[0].getClassLoader() != null)
                            chunkApi = true;
                    }
                    if (!seedApi || !chunkApi) continue;
                    Object value = member.get(provider);
                    boolean duplicate = false;
                    for (Object previous : generators) if (previous == value) duplicate = true;
                    if (value != null && !duplicate) {
                        generators.add(value);
                        generatorTypes.add(candidate);
                    }
                }
            }
            generator = NativePreRegistrySymbols.only(generators, "NATIVE_INTERFACE_GENERATOR");
            generatorType =
                    NativePreRegistrySymbols.only(
                            generatorTypes, "NATIVE_INTERFACE_GENERATOR_TYPE");
            List<Method> creates = new ArrayList<>();
            for (Method method : generatorType.getMethods())
                if (method.getReturnType() == void.class
                        && method.getParameterTypes().length == 1
                        && method.getParameterTypes()[0].isInterface()
                        && method.getParameterTypes()[0].getClassLoader() != null)
                    creates.add(method);
            create = NativePreRegistrySymbols.only(creates, "NATIVE_CREATE_BASE");
            chunkAccess = create.getParameterTypes()[0];
            List<Constructor<?>> protoConstructors = new ArrayList<>();
            for (LegacyClassFile shape : symbols.classes.values()) {
                int count = LegacyClassFile.u2(shape.bytes, shape.poolEnd + 6);
                boolean direct = false;
                for (int i = 0; i < count; i++) {
                    int ref = LegacyClassFile.u2(shape.bytes, shape.poolEnd + 8 + i * 2);
                    if (shape.text[shape.first[ref]].equals(
                            chunkAccess.getName().replace('.', '/'))) direct = true;
                }
                if (!direct) continue;
                for (Constructor<?> constructor : symbols.load(shape.name).getConstructors())
                    if (constructor.getParameterTypes().length == 2)
                        try {
                            constructor.getParameterTypes()[0].getConstructor(int.class, int.class);
                            protoConstructors.add(constructor);
                        } catch (NoSuchMethodException absent) {
                        }
            }
            protoConstructor =
                    NativePreRegistrySymbols.only(protoConstructors, "NATIVE_PROTO_CONSTRUCTOR");
            protoType = protoConstructor.getDeclaringClass();
            chunkPosition =
                    protoConstructor.getParameterTypes()[0].getConstructor(int.class, int.class);
            List<Object> upgrades = new ArrayList<>();
            Class<?> upgradeType = protoConstructor.getParameterTypes()[1];
            for (Field field : upgradeType.getFields())
                if (Modifier.isStatic(field.getModifiers()) && field.getType() == upgradeType)
                    upgrades.add(field.get(null));
            upgrade = NativePreRegistrySymbols.only(upgrades, "NATIVE_EMPTY_UPGRADE");
            Object origin = raw(0, 0);
            List<Method> heights = new ArrayList<>();
            List<Object> surfaces = new ArrayList<>();
            for (Method method : protoType.getMethods()) {
                Class<?>[] p = method.getParameterTypes();
                if (method.getReturnType() == int.class
                        && p.length == 3
                        && p[0].isEnum()
                        && p[1] == int.class
                        && p[2] == int.class)
                    for (Object value : p[0].getEnumConstants())
                        if (((Enum<?>) value).name().equals("WORLD_SURFACE_WG")) {
                            heights.add(method);
                            surfaces.add(value);
                        }
            }
            height = NativePreRegistrySymbols.only(heights, "NATIVE_RAW_HEIGHT");
            surface = NativePreRegistrySymbols.only(surfaces, "NATIVE_SURFACE_HEIGHTMAP");
            List<Method> blocks = new ArrayList<>();
            for (Method method : protoType.getMethods())
                if (Arrays.equals(method.getParameterTypes(), new Class<?>[] {symbols.positionType})
                        && method.getReturnType().getClassLoader() != null)
                    for (Method state : method.getReturnType().getMethods())
                        if (state.getReturnType() == symbols.blockType
                                && state.getParameterTypes().length == 0) blocks.add(method);
            rawBlock = NativePreRegistrySymbols.only(blocks, "NATIVE_RAW_BLOCK");
            List<Method> chunks = new ArrayList<>();
            for (Method method : symbols.worldType.getMethods())
                if (!method.isBridge()
                        && Arrays.equals(
                                method.getParameterTypes(), new Class<?>[] {int.class, int.class})
                        && !method.getReturnType().isInterface()
                        && chunkAccess.isAssignableFrom(method.getReturnType())
                        && method.getReturnType() != protoType) chunks.add(method);
            fullQuery = NativePreRegistrySymbols.only(chunks, "NATIVE_FULL_CHUNK");
            chunkType = fullQuery.getReturnType();
            fullBlock =
                    NativePreRegistrySymbols.method(
                            chunkType, rawBlock.getReturnType(), symbols.positionType);
            List<Object> sources = new ArrayList<>();
            List<Method> sourceQueries = new ArrayList<>();
            for (Method method : generatorType.getMethods())
                if (method.getParameterTypes().length == 0
                        && method.getReturnType().getClassLoader() != null)
                    for (Method query : method.getReturnType().getMethods())
                        if (query.getReturnType() == symbols.biomeType
                                && Arrays.equals(
                                        query.getParameterTypes(),
                                        new Class<?>[] {symbols.positionType, symbols.biomeType})) {
                            sources.add(method.invoke(generator));
                            sourceQueries.add(query);
                        }
            source = NativePreRegistrySymbols.only(sources, "NATIVE_BIOME_SOURCE");
            biomes = source;
            biomeSourceQuery =
                    NativePreRegistrySymbols.only(sourceQueries, "NATIVE_BIOME_SOURCE_QUERY");
            biomeRegistry = symbols.biomeRegistry;
            biomeIds = new IdentityHashMap<>();
            for (Object biome : (Iterable<?>) biomeRegistry)
                biomeIds.put(biome, symbols.key(biomeRegistry, biome));
            defaultBiome =
                    symbols.registryGet.invoke(
                            biomeRegistry,
                            s.make("net.minecraft.resources.ResourceLocation", "minecraft:plains"));
            minimumY = 0;
            int sections = -1;
            for (Method method : protoType.getMethods())
                if (method.getParameterTypes().length == 0
                        && method.getReturnType().isArray()
                        && !method.getReturnType().getComponentType().isPrimitive()) {
                    Class<?> section = method.getReturnType().getComponentType();
                    for (Method block : section.getMethods())
                        if (block.getReturnType() == rawBlock.getReturnType()
                                && Arrays.equals(
                                        block.getParameterTypes(),
                                        new Class<?>[] {int.class, int.class, int.class}))
                            sections = Array.getLength(method.invoke(origin));
                }
            if (sections < 1 || sections > 4096)
                throw new IOException("SEED_VERTICAL_STORAGE_UNAVAILABLE");
            heightLimit = sections * 16;
            structures = new NativePreRegistryLegacyStructures(this);
        } catch (Exception error) {
            try {
                stop.invoke(server);
            } catch (Exception cleanup) {
                error.addSuppressed(cleanup);
            }
            throw error;
        }
    }

    Object position(int x, int y, int z) throws Exception {
        return symbols.positionType
                .getConstructor(int.class, int.class, int.class)
                .newInstance(x, y, z);
    }

    Object raw(int x, int z) throws Exception {
        long key = ((long) x << 32) ^ (z & 0xffffffffL);
        Object chunk = rawChunks.get(key);
        if (chunk == null) {
            chunk = protoConstructor.newInstance(chunkPosition.newInstance(x, z), upgrade);
            create.invoke(generator, chunk);
            rawChunks.put(key, chunk);
        }
        return chunk;
    }

    @Override
    String biome(int x, int y, int z) throws Exception {
        return biomeIds.get(biomeSourceQuery.invoke(source, position(x, y, z), defaultBiome));
    }

    @Override
    List<String> possibleBiomes() throws Exception {
        Set<String> result = new TreeSet<>();
        for (Field field : NativePreRegistrySymbols.fields(source.getClass())) {
            Object value = field.get(source);
            if (field.getType().isArray()
                    && field.getType().getComponentType() == symbols.biomeType)
                for (int i = 0; i < Array.getLength(value); i++)
                    result.add(biomeIds.get(Array.get(value, i)));
            if (field.getType() == symbols.biomeType) result.add(biomeIds.get(value));
        }
        if (result.isEmpty()) throw new IOException("SEED_POSSIBLE_BIOMES_UNAVAILABLE");
        return new ArrayList<>(result);
    }

    @Override
    int[] column(int x, int z, Integer slice, boolean terrain) throws Exception {
        if (slice != null) return new int[] {slice, 0, slice};
        Object chunk = raw(x >> 4, z >> 4);
        int top = ((Number) height.invoke(chunk, surface, x & 15, z & 15)).intValue(), ground = top;
        if (!terrain || top < minimumY) return new int[] {top, 0, top};
        int fluid = fluid(rawBlock.invoke(chunk, position(x, top, z)));
        if (fluid != 0)
            while (ground >= minimumY && fluid(rawBlock.invoke(chunk, position(x, ground, z))) != 0)
                ground--;
        return new int[] {ground, fluid, top};
    }

    private int fluid(Object state) throws Exception {
        for (Method getter : state.getClass().getMethods())
            if (getter.getParameterTypes().length == 0
                    && getter.getReturnType().getClassLoader() != null)
                for (Method type : getter.getReturnType().getMethods())
                    if (type.getReturnType() == symbols.fluidType
                            && type.getParameterTypes().length == 0) {
                        String id =
                                symbols.key(
                                        symbols.fluidRegistry, type.invoke(getter.invoke(state)));
                        return id.equals("water") || id.equals("flowing_water")
                                ? 1
                                : id.equals("lava") || id.equals("flowing_lava") ? 2 : 0;
                    }
        throw new IOException("SEED_FLUID_STATE_UNAVAILABLE");
    }

    @Override
    Map<String, Object> spawn() throws Exception {
        if (!dimension.equals("overworld"))
            throw new IllegalArgumentException("Native shared spawn belongs to the Overworld.");
        return Json.object(
                "id",
                "spawn",
                "kind",
                "spawn",
                "x",
                symbols.serializedField(symbols.dataType, "SpawnX", "I").getInt(data),
                "y",
                symbols.serializedField(symbols.dataType, "SpawnY", "I").getInt(data),
                "z",
                symbols.serializedField(symbols.dataType, "SpawnZ", "I").getInt(data),
                "pieces",
                0);
    }

    Object fullChunk(int x, int z) throws Exception {
        long key = ((long) x << 32) ^ (z & 0xffffffffL);
        Object chunk = fullChunks.get(key);
        if (chunk == null) {
            chunk = fullQuery.invoke(world, x, z);
            fullChunks.put(key, chunk);
        }
        return chunk;
    }

    Collection<Object> generatedChunks() {
        return new ArrayList<>(fullChunks.values());
    }

    @Override
    Object block(int x, int y, int z) throws Exception {
        Object state = fullBlock.invoke(fullChunk(x >> 4, z >> 4), position(x, y, z));
        symbols.bindState(state);
        return state;
    }

    @Override
    File save(int minX, int minZ, int maxX, int maxZ, boolean loot, AtomicBoolean cancelled)
            throws Exception {
        if ((long) (maxX >> 4) - (minX >> 4) > 32 || (long) (maxZ >> 4) - (minZ >> 4) > 32)
            throw new IOException("SEED_REGION_BUDGET");
        int total = ((maxX >> 4) - (minX >> 4) + 3) * ((maxZ >> 4) - (minZ >> 4) + 3), done = 0;
        report("full", 0, total);
        for (int z = (minZ >> 4) - 1; z <= (maxZ >> 4) + 1; z++)
            for (int x = (minX >> 4) - 1; x <= (maxX >> 4) + 1; x++) {
                check(cancelled);
                fullChunk(x, z);
                report("full", ++done, total);
            }
        NativePreRegistryLegacyInventory.read(this, loot, cancelled);
        List<Method> saves = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods())
            if (method.getReturnType() == void.class
                    && method.getParameterTypes().length == 2
                    && method.getParameterTypes()[0] == boolean.class
                    && method.getParameterTypes()[1].isInterface()) saves.add(method);
        NativePreRegistrySymbols.only(saves, "NATIVE_WORLD_SAVE").invoke(world, true, null);
        return new File(directory, "preview");
    }

    @Override
    Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        return NativeMappedRegion.read(this, query, cancelled);
    }

    static void check(AtomicBoolean cancelled) {
        if (cancelled.get()) throw new CancellationException();
    }

    /** Saves and closes only this helper's discovered native server. */
    @Override
    public void close() throws Exception {
        if (closed) return;
        closed = true;
        stop.invoke(server);
    }
}
