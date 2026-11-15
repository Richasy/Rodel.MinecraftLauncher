package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.net.Proxy;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * One disposable original server world. No listener, player, ticking loop or source save is opened.
 */
final class NativeSeedWorld extends NativeSeedContext {
    final Object world, server, options, store, generator;
    final Object info;
    final Class<?> chunkType;
    final File directory;
    final Method biome, chunk, baseChunk, height, block;
    final Constructor<?> position;
    final Map<String, Object> registries;
    private final NativeSeedSymbols symbols;
    private final int dimension;
    private final NativeAncientBlocks ancientBlocks;
    private final LinkedHashMap<Long, Object> samples =
            new LinkedHashMap<Long, Object>(128, .75f, true) {
                @Override
                protected boolean removeEldestEntry(Map.Entry<Long, Object> entry) {
                    return size() > 128;
                }
            };

    NativeSeedWorld(
            NativeSeedSymbols symbols, File directory, long seed, String preset, int dimension)
            throws Exception {
        this(symbols, directory, seed, preset, dimension, null);
    }

    /** Keeps every requested native preset and fixed biome explicit before creating a world. */
    NativeSeedWorld(
            NativeSeedSymbols symbols,
            File directory,
            long seed,
            String preset,
            int dimension,
            String requestedBiome)
            throws Exception {
        this.symbols = symbols;
        this.dimension = dimension;
        this.directory = directory;
        if (!directory.mkdirs() && !directory.isDirectory())
            throw new IOException("SEED_WORKSPACE_UNAVAILABLE");
        List<Constructor<?>> constructors =
                new ArrayList<>(Arrays.asList(symbols.serverType.getConstructors()));
        int mostParameters = 0;
        for (Constructor<?> candidate : constructors)
            mostParameters = Math.max(mostParameters, candidate.getParameterTypes().length);
        final int complete = mostParameters;
        constructors.removeIf(candidate -> candidate.getParameterTypes().length != complete);
        Constructor<?> constructor = NativeSeedSymbols.only(constructors, "HOST_CONSTRUCTOR");
        Object[] parameters = new Object[constructor.getParameterTypes().length];
        for (int i = 0; i < parameters.length; i++) {
            Class<?> type = constructor.getParameterTypes()[i];
            if (type == File.class) parameters[i] = directory;
            else if (type == Proxy.class) parameters[i] = Proxy.NO_PROXY;
            else {
                LegacyClassFile shape = symbols.classes.get(type.getName().replace('.', '/'));
                boolean authentication = type.getName().startsWith("com.mojang.authlib.");
                if (shape != null)
                    for (String text : shape.text)
                        if (text != null && text.startsWith("com/mojang/authlib/"))
                            authentication = true;
                if (!authentication) parameters[i] = symbols.factory(type);
            }
        }
        server =
                NativeSeedHostClass.create(constructor)
                        .getConstructors()[0]
                        .newInstance(parameters);
        List<Class<?>> lists = new ArrayList<>();
        for (Field field : NativeSeedSymbols.fields(symbols.serverType)) {
            LegacyClassFile type = symbols.classes.get(field.getType().getName().replace('.', '/'));
            if (type != null
                    && (NativeSeedSymbols.contains(type, "banned-players.json")
                            || NativeSeedSymbols.contains(type, "banned-players.txt")))
                lists.add(field.getType());
        }
        Class<?> players = NativeSeedSymbols.only(lists, "PLAYER_LIST");
        Object playerList =
                NativeSeedHostClass.create(players.getConstructor(symbols.serverType))
                        .getConstructors()[0]
                        .newInstance(server);
        NativeAncientView.configure(symbols, playerList);
        List<Method> setters = new ArrayList<>();
        for (Method method : symbols.serverType.getMethods())
            if (method.getReturnType() == void.class
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {players}))
                setters.add(method);
        NativeSeedSymbols.only(setters, "PLAYER_LIST_SETTER").invoke(server, playerList);
        biomeIds = registry(symbols.biomeType);
        boolean single = preset.equals("single_biome");
        if (single && dimension != 0)
            throw NativeAncientPresets.unsupported(
                    "This original customized generator applies fixed biomes only in the"
                            + " overworld.");
        String generatorOptions =
                single ? NativeAncientPresets.singleBiome(symbols, biomeIds, requestedBiome) : null;
        Object type = symbols.named(symbols.presetType, single ? "customized" : preset);
        if (type == null)
            throw NativeAncientPresets.unsupported(
                    "This original world type is unavailable: " + preset);
        options =
                symbols.settingsConstructor.newInstance(
                        seed, symbols.named(symbols.gameType, "creative"), true, false, type);
        if (generatorOptions != null) NativeAncientPresets.apply(options, generatorOptions);
        Object initialInfo =
                symbols.worldInfo
                        .getConstructor(symbols.settings, String.class)
                        .newInstance(options, "Rodel isolated generation");
        Class<?> saveType = symbols.worldConstructor.getParameterTypes()[1];
        List<Object> stores = new ArrayList<>();
        for (Field field : NativeSeedSymbols.fields(symbols.serverType)) {
            Object format = field.get(server);
            if (format == null || format.getClass().getClassLoader() == null) continue;
            for (Method method : format.getClass().getMethods())
                if (method.getReturnType() == saveType
                        && Arrays.equals(
                                method.getParameterTypes(),
                                new Class<?>[] {String.class, boolean.class}))
                    stores.add(method.invoke(format, "preview", false));
        }
        store = NativeSeedSymbols.only(stores, "SAVE_FACTORY");
        Class<?>[] worldArguments = symbols.worldConstructor.getParameterTypes();
        boolean directSettings = worldArguments[2] == String.class;
        Object[] creation = new Object[worldArguments.length];
        creation[0] = server;
        creation[1] = store;
        creation[2] = directSettings ? "Rodel isolated generation" : initialInfo;
        creation[3] = dimension;
        int nativeServices = directSettings ? 5 : 4;
        if (directSettings) creation[4] = options;
        for (int i = nativeServices; i < creation.length; i++) {
            try {
                creation[i] = worldArguments[i].getConstructor().newInstance();
            } catch (NoSuchMethodException service) {
                List<Method> getters = new ArrayList<>();
                for (Method getter : symbols.serverType.getMethods())
                    if (getter.getParameterTypes().length == 0
                            && getter.getReturnType() == worldArguments[i]) getters.add(getter);
                creation[i] =
                        NativeSeedSymbols.only(getters, "WORLD_NATIVE_SERVICE").invoke(server);
            }
        }
        world = symbols.worldConstructor.newInstance(creation);
        if (directSettings) {
            List<Method> getters = new ArrayList<>();
            for (Method method : symbols.worldType.getMethods())
                if (method.getParameterTypes().length == 0
                        && method.getReturnType() == symbols.worldInfo) getters.add(method);
            info = NativeSeedSymbols.only(getters, "WORLD_INFO_QUERY").invoke(world);
        } else info = initialInfo;
        List<Method> initialize = new ArrayList<>();
        for (Method method : symbols.worldType.getDeclaredMethods())
            if (Modifier.isPublic(method.getModifiers())
                    && method.getParameterTypes().length == 0
                    && method.getReturnType() == symbols.worldType.getSuperclass())
                initialize.add(method);
        if (!initialize.isEmpty())
            NativeSeedSymbols.only(initialize, "WORLD_INITIALIZATION").invoke(world);
        for (Field field : NativeSeedSymbols.fields(symbols.serverType))
            if (field.getType().isArray()
                    && field.getType().getComponentType() == symbols.worldType) {
                Object worlds = Array.newInstance(symbols.worldType, 3);
                Array.set(worlds, 0, world);
                field.set(server, worlds);
            }
        List<Method> newWorld = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods())
            if (method.getReturnType() == void.class
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {symbols.settings}))
                newWorld.add(method);
        if (!directSettings || !newWorld.isEmpty())
            NativeSeedSymbols.only(newWorld, "SPAWN_INITIALIZATION").invoke(world, options);
        List<Method> biomes = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods())
            if (method.getReturnType() == symbols.biomeType
                    && (method.getParameterTypes().length == 1
                            || Arrays.equals(
                                    method.getParameterTypes(),
                                    new Class<?>[] {int.class, int.class}))) biomes.add(method);
        biome = NativeSeedSymbols.only(biomes, "BIOME_QUERY");
        position =
                biome.getParameterTypes().length == 1
                        ? biome.getParameterTypes()[0].getConstructor(
                                int.class, int.class, int.class)
                        : null;
        registries = new LinkedHashMap<>();
        List<Method> chunks = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods())
            if (Arrays.equals(method.getParameterTypes(), new Class<?>[] {int.class, int.class})
                    && !method.getReturnType().isPrimitive()
                    && method.getReturnType().getClassLoader() != null) {
                LegacyClassFile candidate =
                        symbols.classes.get(method.getReturnType().getName().replace('.', '/'));
                if (candidate != null)
                    for (LegacyClassFile.Member member : candidate.methods)
                        if (member.descriptor.equals("()[B")) {
                            chunks.add(method);
                            break;
                        }
            }
        if (chunks.size() > 1) chunks.removeIf(method -> !callsChunkProvider(symbols, method));
        chunk = NativeSeedSymbols.only(chunks, "CHUNK_QUERY");
        chunkType = chunk.getReturnType();
        List<Method> heights = new ArrayList<>();
        LegacyClassFile chunkClass = symbols.classes.get(chunkType.getName().replace('.', '/'));
        for (LegacyClassFile.Member member : chunkClass.methods)
            if ((member.access & 1) != 0
                    && member.descriptor.equals("(II)I")
                    && member.code != null
                    && member.code.length <= 20
                    && member.code[member.code.length - 2] == 0x2e
                    && (member.code[member.code.length - 1] & 255) == 0xac)
                heights.add(chunkType.getMethod(member.name, int.class, int.class));
        height = NativeSeedSymbols.only(heights, "HEIGHT_QUERY");
        List<Method> blocks = new ArrayList<>();
        for (Method method : chunkType.getMethods())
            if (position != null
                    && Arrays.equals(
                            method.getParameterTypes(),
                            new Class<?>[] {position.getDeclaringClass()})
                    && method.getReturnType().isInterface()) blocks.add(method);
        if (position == null)
            for (Method method : chunkType.getMethods()) {
                LegacyClassFile result =
                        symbols.classes.get(method.getReturnType().getName().replace('.', '/'));
                if (Arrays.equals(
                                method.getParameterTypes(),
                                new Class<?>[] {int.class, int.class, int.class})
                        && result != null
                        && NativeSeedSymbols.contains(result, "tile.", "air", "stone"))
                    blocks.add(method);
            }
        if (position == null && blocks.isEmpty() && symbols.staticBlockType != null)
            blocks.add(NativeAncientBlocks.blockQuery(symbols, chunkType));
        block = NativeSeedSymbols.only(blocks, "BLOCK_QUERY");
        ancientBlocks =
                position == null
                        ? new NativeAncientBlocks(
                                symbols,
                                chunkType,
                                block.getReturnType().isPrimitive()
                                        ? symbols.staticBlockType
                                        : block.getReturnType(),
                                registry(
                                        block.getReturnType().isPrimitive()
                                                ? symbols.staticBlockType
                                                : block.getReturnType()))
                        : null;
        List<Object> generators = new ArrayList<>();
        List<Method> generate = new ArrayList<>();
        for (Field field : NativeSeedSymbols.fields(world.getClass())) {
            Object provider = field.get(world);
            if (provider == null
                    || provider == server
                    || provider.getClass().getClassLoader() == null
                    || !hasChunk(provider.getClass())) continue;
            for (Field member : NativeSeedSymbols.fields(provider.getClass())) {
                Object value = member.get(provider);
                if (value == null || value == world || value.getClass().getClassLoader() == null)
                    continue;
                for (Method method : value.getClass().getMethods())
                    if (method.getReturnType() == chunkType
                            && Arrays.equals(
                                    method.getParameterTypes(),
                                    new Class<?>[] {int.class, int.class})) {
                        boolean duplicate = false;
                        for (int i = 0; i < generators.size(); i++)
                            if (generators.get(i) == value && generate.get(i).equals(method))
                                duplicate = true;
                        if (!duplicate) {
                            generators.add(value);
                            generate.add(method);
                        }
                    }
            }
        }
        if (generators.size() > 1)
            for (int i = generators.size() - 1; i >= 0; i--)
                if (delegatesChunkQuery(symbols, generate.get(i))) {
                    generators.remove(i);
                    generate.remove(i);
                }
        generator = NativeSeedSymbols.only(generators, "CHUNK_GENERATOR");
        baseChunk = NativeSeedSymbols.only(generate, "BASE_CHUNK");
        Object origin = baseChunk.invoke(generator, 0, 0);
        List<Integer> limits = new ArrayList<>();
        for (Field field : NativeSeedSymbols.fields(chunkType))
            if (field.getType().isArray() && !field.getType().getComponentType().isPrimitive()) {
                Class<?> section = field.getType().getComponentType();
                for (Method method : section.getMethods())
                    if (method.getReturnType() == block.getReturnType()
                            && Arrays.equals(
                                    method.getParameterTypes(),
                                    new Class<?>[] {int.class, int.class, int.class})) {
                        limits.add(Array.getLength(field.get(origin)) * 16);
                        break;
                    }
            }
        minimumY = 0;
        heightLimit = NativeSeedSymbols.only(limits, "VERTICAL_STORAGE");
        structures = new NativeSeedStructures(symbols, this);
    }

    @Override
    List<String> possibleBiomes() throws Exception {
        return dimension == 0 ? super.possibleBiomes() : Arrays.asList(biome(0, 64, 0));
    }

    private static boolean callsChunkProvider(NativeSeedSymbols symbols, Method query) {
        LegacyClassFile type =
                symbols.classes.get(query.getDeclaringClass().getName().replace('.', '/'));
        for (LegacyClassFile.Member method : type.methods)
            if (method.name.equals(query.getName())
                    && method.descriptor.equals(
                            "(II)L" + query.getReturnType().getName().replace('.', '/') + ";")
                    && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb9
                            && type.referenceType(LegacyClassFile.u2(method.code, p + 1))
                                    .equals(
                                            "(II)L"
                                                    + query.getReturnType()
                                                            .getName()
                                                            .replace('.', '/')
                                                    + ";")) return true;
        return false;
    }

    private static boolean delegatesChunkQuery(NativeSeedSymbols symbols, Method query) {
        LegacyClassFile type =
                symbols.classes.get(query.getDeclaringClass().getName().replace('.', '/'));
        String descriptor = "(II)L" + query.getReturnType().getName().replace('.', '/') + ";";
        for (LegacyClassFile.Member method : type.methods)
            if (method.name.equals(query.getName())
                    && method.descriptor.equals(descriptor)
                    && method.code != null) {
                byte[] code = method.code;
                if (code.length == 7
                        && code[0] == 0x2a
                        && code[1] == 0x1b
                        && code[2] == 0x1c
                        && (code[3] & 255) == 0xb6
                        && (code[6] & 255) == 0xb0) {
                    int ref = LegacyClassFile.u2(code, 4);
                    return type.name.equals(type.referenceOwner(ref))
                            && descriptor.equals(type.referenceType(ref));
                }
            }
        return false;
    }

    private boolean hasChunk(Class<?> type) {
        for (Method method : type.getMethods())
            if (method.getReturnType() == chunkType
                    && Arrays.equals(
                            method.getParameterTypes(), new Class<?>[] {int.class, int.class}))
                return true;
        return false;
    }

    private Map<Object, String> registry(Class<?> entryType) throws Exception {
        List<Map<Object, String>> candidates = new ArrayList<>();
        for (Field field : entryType.getFields())
            if (Modifier.isStatic(field.getModifiers())
                    && Iterable.class.isAssignableFrom(field.getType())) {
                Object registry = field.get(null);
                List<Object> values = new ArrayList<>();
                for (Object value : (Iterable<?>) registry) {
                    if (!entryType.isInstance(value)) {
                        values.clear();
                        break;
                    }
                    values.add(value);
                }
                if (values.isEmpty()) continue;
                for (Method method : registry.getClass().getMethods())
                    if (Arrays.equals(method.getParameterTypes(), new Class<?>[] {Object.class})
                            && !method.getReturnType().isPrimitive()) {
                        Map<Object, String> result = new IdentityHashMap<>();
                        Set<String> keys = new HashSet<>();
                        boolean valid = true;
                        for (Object value : values) {
                            Object key;
                            try {
                                key = method.invoke(registry, value);
                            } catch (ReflectiveOperationException error) {
                                valid = false;
                                break;
                            }
                            if (key instanceof String && key.toString().matches("[a-z0-9_/.-]+"))
                                key = "minecraft:" + key;
                            if (key == null
                                    || !key.toString().matches("[a-z0-9_.-]+:[a-z0-9_/.-]+")
                                    || !keys.add(key.toString())) {
                                valid = false;
                                break;
                            }
                            result.put(value, key.toString().replaceFirst("^minecraft:", ""));
                        }
                        if (valid) candidates.add(result);
                    }
            }
        if (candidates.isEmpty()) {
            for (Field field : entryType.getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers())
                        || field.getType() != Array.newInstance(entryType, 0).getClass()) continue;
                field.setAccessible(true);
                Object array = field.get(null);
                if (array == null || Array.getLength(array) > 65536) continue;
                Map<Object, String> result = new IdentityHashMap<>();
                for (int i = 0; i < Array.getLength(array); i++) {
                    Object biome = Array.get(array, i);
                    if (biome == null) continue;
                    String id =
                            entryType == symbols.biomeType
                                    ? "legacy:biome_" + i
                                    : i == 0 ? "minecraft:air" : "legacy:block_" + i;
                    result.put(biome, id);
                    List<String> labels = new ArrayList<>();
                    for (Field name : entryType.getDeclaredFields())
                        if (!Modifier.isStatic(name.getModifiers())
                                && name.getType() == String.class) {
                            name.setAccessible(true);
                            Object label = name.get(biome);
                            if (label instanceof String && !((String) label).isEmpty())
                                labels.add((String) label);
                        }
                    if (entryType == symbols.biomeType && labels.size() == 1)
                        biomeNames.put(id, labels.get(0));
                }
                if (!result.isEmpty()) candidates.add(result);
            }
        }
        return NativeSeedSymbols.only(candidates, "REGISTRY_KEYS_" + entryType.getName());
    }

    String biome(int x, int y, int z) throws Exception {
        String id =
                biomeIds.get(
                        position == null
                                ? biome.invoke(world, x, z)
                                : biome.invoke(world, position.newInstance(x, y, z)));
        if (id == null) throw new IOException("SEED_BIOME_UNREGISTERED");
        return id;
    }

    private Object sampledChunk(int x, int z) throws Exception {
        long key = ((long) (x >> 4) << 32) ^ ((z >> 4) & 0xffffffffL);
        Object value = samples.get(key);
        if (value == null) {
            value = baseChunk.invoke(generator, x >> 4, z >> 4);
            samples.put(key, value);
        }
        return value;
    }

    int[] column(int x, int z, Integer slice, boolean terrain) throws Exception {
        Object value = sampledChunk(x, z);
        int top =
                slice != null
                        ? slice
                        : ((Number) height.invoke(value, x & 15, z & 15)).intValue() - 1;
        // Some original dimension generators leave the height map uninitialized in base chunks.
        // Read the original block column instead of treating that cache's zero as an empty chunk.
        if (slice == null && top < minimumY) {
            top = heightLimit - 1;
            while (top >= minimumY
                    && "minecraft:air".equals(state(blockAt(value, x, top, z)).get("id"))) top--;
        }
        if (top < minimumY || slice != null || !terrain) return new int[] {top, 0};
        int ground = top, fluid = fluid(blockAt(value, x, top, z));
        while (ground >= minimumY && fluid(blockAt(value, x, ground, z)) != 0) ground--;
        return new int[] {ground, fluid};
    }

    private Object blockAt(Object chunk, int x, int y, int z) throws Exception {
        if (position != null) return block.invoke(chunk, position.newInstance(x, y, z));
        return ancientBlocks.state(
                block.invoke(chunk, x & 15, y, z & 15), chunk, x & 15, y, z & 15);
    }

    private static int fluid(Object state) {
        if (state instanceof NativeAncientBlocks.State)
            return ((NativeAncientBlocks.State) state).fluid;
        String id = state.toString().split("\\[", 2)[0];
        return id.equals("minecraft:water") || id.equals("minecraft:flowing_water")
                ? 1
                : id.equals("minecraft:lava") || id.equals("minecraft:flowing_lava") ? 2 : 0;
    }

    Map<String, Object> spawn() throws Exception {
        int x = symbols.serializedField(symbols.worldInfo, "SpawnX", "I").getInt(info),
                y = symbols.serializedField(symbols.worldInfo, "SpawnY", "I").getInt(info),
                z = symbols.serializedField(symbols.worldInfo, "SpawnZ", "I").getInt(info);
        return Json.object("id", "spawn", "kind", "spawn", "x", x, "y", y, "z", z, "pieces", 0);
    }

    Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        @SuppressWarnings("unchecked")
        Map<String, Object> bounds = (Map<String, Object>) query.get("bounds");
        int minX = ((Number) bounds.get("minX")).intValue(),
                minY = ((Number) bounds.get("minY")).intValue(),
                minZ = ((Number) bounds.get("minZ")).intValue();
        int maxX = ((Number) bounds.get("maxX")).intValue(),
                maxY = ((Number) bounds.get("maxY")).intValue(),
                maxZ = ((Number) bounds.get("maxZ")).intValue();
        boolean include = Boolean.TRUE.equals(query.get("includeBlocks"));
        long volume =
                ((long) maxX - minX + 1) * ((long) maxY - minY + 1) * ((long) maxZ - minZ + 1);
        if (minY < minimumY
                || maxY >= heightLimit
                || maxX < minX
                || maxZ < minZ
                || maxY < minY
                || volume > (include ? 4194304 : 16777216))
            throw new IllegalArgumentException("SEED_VOLUME_BUDGET");
        ByteArrayOutputStream blocks = new ByteArrayOutputStream(include ? (int) volume * 2 : 0);
        Map<Object, Integer> indices = new IdentityHashMap<>();
        List<Object> palette = new ArrayList<>();
        Map<String, Long> materials = new TreeMap<>();
        Map<Long, Object> chunks = new HashMap<>();
        long started = System.nanoTime();
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++) {
                    if (cancelled.get()) throw new java.util.concurrent.CancellationException();
                    long key = ((long) (x >> 4) << 32) ^ ((z >> 4) & 0xffffffffL);
                    Object generated = chunks.get(key);
                    if (generated == null) {
                        generated = chunk.invoke(world, x >> 4, z >> 4);
                        chunks.put(key, generated);
                    }
                    Object state = blockAt(generated, x, y, z);
                    Integer index = indices.get(state);
                    if (index == null) {
                        index = palette.size();
                        if (index >= 65536) throw new IOException("SEED_PALETTE_BUDGET");
                        indices.put(state, index);
                        palette.add(state(state));
                    }
                    @SuppressWarnings("unchecked")
                    String id = (String) ((Map<String, Object>) palette.get(index)).get("id");
                    materials.put(id, materials.getOrDefault(id, 0L) + 1);
                    if (include) {
                        blocks.write(index & 255);
                        blocks.write((index >>> 8) & 255);
                    }
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

    private static Map<String, Object> state(Object original) throws IOException {
        if (original instanceof NativeAncientBlocks.State)
            return ((NativeAncientBlocks.State) original).json();
        String text = original.toString();
        int open = text.indexOf('[');
        String id = open < 0 ? text : text.substring(0, open);
        if (!id.matches("[a-z0-9_.-]+:[a-z0-9_/.-]+"))
            throw new IOException("SEED_STATE_IDENTIFIER_INVALID");
        Map<String, String> properties = new LinkedHashMap<>();
        if (open >= 0) {
            if (!text.endsWith("]")) throw new IOException("SEED_STATE_PROPERTIES_INVALID");
            String values = text.substring(open + 1, text.length() - 1);
            if (!values.isEmpty())
                for (String entry : values.split(",")) {
                    String[] pair = entry.split("=", 2);
                    if (pair.length != 2) throw new IOException("SEED_STATE_PROPERTY_INVALID");
                    properties.put(pair[0], pair[1]);
                }
        }
        return Json.object("id", id, "properties", properties);
    }

    File save(int minX, int minZ, int maxX, int maxZ, boolean includeLoot, AtomicBoolean cancelled)
            throws Exception {
        if ((long) (maxX >> 4) - (minX >> 4) > 32 || (long) (maxZ >> 4) - (minZ >> 4) > 32)
            throw new IOException("SEED_REGION_BUDGET");
        for (int z = (minZ >> 4) - 1; z <= (maxZ >> 4) + 1; z++)
            for (int x = (minX >> 4) - 1; x <= (maxX >> 4) + 1; x++) {
                if (cancelled.get()) throw new java.util.concurrent.CancellationException();
                chunk.invoke(world, x, z);
            }
        readInventories(includeLoot, cancelled);
        List<Method> saves = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods())
            if (method.getReturnType() == void.class
                    && method.getParameterTypes().length == 2
                    && method.getParameterTypes()[0] == boolean.class
                    && method.getParameterTypes()[1].isInterface()) saves.add(method);
        NativeSeedSymbols.only(saves, "WORLD_SAVE").invoke(world, true, null);
        List<Method> flush = new ArrayList<>();
        for (Method method : store.getClass().getMethods())
            if (method.getParameterTypes().length == 0 && method.getReturnType() == void.class) {
                LegacyClassFile owner =
                        symbols.classes.get(method.getDeclaringClass().getName().replace('.', '/'));
                if (owner == null) continue;
                for (LegacyClassFile.Member member : owner.methods)
                    if (member.name.equals(method.getName())
                            && member.descriptor.equals("()V")
                            && member.code != null)
                        for (int p = 0;
                                p < member.code.length;
                                p = LegacyClassFile.next(member.code, p))
                            if ((member.code[p] & 255) == 0xb8
                                    && owner.referenceType(LegacyClassFile.u2(member.code, p + 1))
                                            .equals("()V")) {
                                flush.add(method);
                                break;
                            }
            }
        NativeSeedSymbols.only(flush, "SAVE_FLUSH").invoke(store);
        return new File(directory, "preview");
    }

    private void readInventories(boolean evaluate, AtomicBoolean cancelled) throws Exception {
        Set<Object> seen = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        for (Field field : NativeSeedSymbols.fields(world.getClass()))
            if (List.class.isAssignableFrom(field.getType())) {
                Object values = field.get(world);
                if (!(values instanceof List<?>)) continue;
                for (Object value : new ArrayList<>((List<?>) values)) {
                    if (cancelled.get()) throw new java.util.concurrent.CancellationException();
                    if (value == null
                            || value.getClass().getClassLoader() == null
                            || !seen.add(value)) continue;
                    if (seen.size() > 10000) throw new IOException("SEED_INVENTORY_BUDGET");
                    List<Method> readers = new ArrayList<>();
                    for (Class<?> type = value.getClass();
                            type != null && type.getClassLoader() != null;
                            type = type.getSuperclass()) {
                        LegacyClassFile shape =
                                symbols.classes.get(type.getName().replace('.', '/'));
                        if (shape == null) continue;
                        for (LegacyClassFile.Member member : shape.methods)
                            if ((member.access & 1) != 0
                                    && member.descriptor.startsWith("(I)L")
                                    && member.code != null) {
                                String result =
                                        member.descriptor.substring(
                                                4, member.descriptor.length() - 1);
                                LegacyClassFile stack = symbols.classes.get(result);
                                if (stack == null
                                        || !NativeSeedSymbols.contains(
                                                stack, "id", "Count", "Damage")) continue;
                                boolean getter = false, mutation = false;
                                for (int p = 0;
                                        p < member.code.length;
                                        p = LegacyClassFile.next(member.code, p)) {
                                    int op = member.code[p] & 255;
                                    if (op == 0xb5 || op == 0x53) mutation = true;
                                    if (op == 0xb6 || op == 0xb9) {
                                        int ref = LegacyClassFile.u2(member.code, p + 1);
                                        if (shape.referenceName(ref).equals("get")
                                                && shape.referenceType(ref)
                                                        .equals("(I)Ljava/lang/Object;"))
                                            getter = true;
                                    }
                                }
                                if (getter && !mutation)
                                    readers.add(type.getMethod(member.name, int.class));
                            }
                        if (!readers.isEmpty()) break;
                    }
                    if (evaluate && !readers.isEmpty())
                        NativeSeedSymbols.only(readers, "INVENTORY_READ").invoke(value, 0);
                    List<Integer> slots = new ArrayList<>();
                    for (Field inventory : NativeSeedSymbols.fields(value.getClass()))
                        if (List.class.isAssignableFrom(inventory.getType())
                                && inventory.getGenericType() instanceof ParameterizedType) {
                            for (Type argument :
                                    ((ParameterizedType) inventory.getGenericType())
                                            .getActualTypeArguments())
                                if (argument instanceof Class<?>) {
                                    LegacyClassFile stack =
                                            symbols.classes.get(
                                                    ((Class<?>) argument)
                                                            .getName()
                                                            .replace('.', '/'));
                                    if (stack != null
                                            && NativeSeedSymbols.contains(
                                                    stack, "id", "Count", "Damage")) {
                                        Object items = inventory.get(value);
                                        if (items instanceof List<?>)
                                            slots.add(((List<?>) items).size());
                                    }
                                }
                        }
                    for (Field inventory : NativeSeedSymbols.fields(value.getClass()))
                        if (inventory.getType().isArray()
                                && !inventory.getType().getComponentType().isPrimitive()) {
                            LegacyClassFile stack =
                                    symbols.classes.get(
                                            inventory
                                                    .getType()
                                                    .getComponentType()
                                                    .getName()
                                                    .replace('.', '/'));
                            if (stack != null
                                    && NativeSeedSymbols.contains(stack, "id", "Count", "Damage")) {
                                Object items = inventory.get(value);
                                if (items != null)
                                    slots.add(
                                            NativeAncientInventory.capacity(
                                                    symbols.classes,
                                                    value,
                                                    inventory,
                                                    Array.getLength(items)));
                            }
                        }
                    if (slots.isEmpty()) continue;
                    List<Method> serializers = new ArrayList<>();
                    for (Method method : value.getClass().getMethods())
                        if (!Modifier.isStatic(method.getModifiers())
                                && method.getParameterTypes().length == 1
                                && (method.getReturnType() == method.getParameterTypes()[0]
                                        || method.getReturnType() == void.class)
                                && method.getParameterTypes()[0] != Object.class) {
                            LegacyClassFile tag =
                                    symbols.classes.get(
                                            method.getParameterTypes()[0]
                                                    .getName()
                                                    .replace('.', '/'));
                            if (tag != null
                                    && NativeSeedSymbols.contains(tag, "java/util/Map")
                                    && (method.getReturnType() == method.getParameterTypes()[0]
                                            || writesCoordinates(method))) serializers.add(method);
                        }
                    if (serializers.isEmpty()) continue;
                    Method serializer = NativeSeedSymbols.only(serializers, "INVENTORY_NBT");
                    Object tag = serializer.getParameterTypes()[0].getConstructor().newInstance();
                    serializer.invoke(value, tag);
                    List<Method> keys = new ArrayList<>(), integers = new ArrayList<>();
                    for (Method method : tag.getClass().getMethods()) {
                        if (method.getParameterTypes().length == 0
                                && Set.class.isAssignableFrom(method.getReturnType()))
                            keys.add(method);
                        if (method.getReturnType() == int.class
                                && Arrays.equals(
                                        method.getParameterTypes(), new Class<?>[] {String.class}))
                            integers.add(method);
                    }
                    Set<?> names;
                    if (keys.isEmpty()) {
                        List<Map<?, ?>> maps = new ArrayList<>();
                        for (Field entryField : NativeSeedSymbols.fields(tag.getClass()))
                            if (!Modifier.isStatic(entryField.getModifiers())
                                    && Map.class.isAssignableFrom(entryField.getType())) {
                                Object entries = entryField.get(tag);
                                if (entries instanceof Map<?, ?>) maps.add((Map<?, ?>) entries);
                            }
                        names = NativeSeedSymbols.only(maps, "NBT_ENTRY_MAP").keySet();
                    } else names = (Set<?>) NativeSeedSymbols.only(keys, "NBT_KEYS").invoke(tag);
                    if (!names.containsAll(Arrays.asList("x", "y", "z"))) continue;
                    Method integer = NativeSeedSymbols.only(integers, "NBT_INTEGER");
                    int x = ((Number) integer.invoke(tag, "x")).intValue(),
                            y = ((Number) integer.invoke(tag, "y")).intValue(),
                            z = ((Number) integer.invoke(tag, "z")).intValue();
                    Object originalState = blockAt(chunk.invoke(world, x >> 4, z >> 4), x, y, z);
                    inventories.add(
                            Json.object(
                                    "x",
                                    x,
                                    "y",
                                    y,
                                    "z",
                                    z,
                                    "id",
                                    state(originalState).get("id"),
                                    "slots",
                                    NativeSeedSymbols.only(slots, "INVENTORY_STORAGE")));
                }
            }
    }

    private boolean writesCoordinates(Method serializer) {
        String descriptor =
                "(L" + serializer.getParameterTypes()[0].getName().replace('.', '/') + ";)";
        for (Class<?> current = serializer.getDeclaringClass();
                current != null && current.getClassLoader() != null;
                current = current.getSuperclass()) {
            LegacyClassFile shape = symbols.classes.get(current.getName().replace('.', '/'));
            if (shape == null) continue;
            for (LegacyClassFile.Member method : shape.methods)
                if (method.name.equals(serializer.getName())
                        && method.descriptor.startsWith(descriptor)
                        && method.code != null) {
                    boolean key = false;
                    int budget = 0;
                    for (int p = 0;
                            p < method.code.length;
                            p = LegacyClassFile.next(method.code, p)) {
                        int opcode = method.code[p] & 255;
                        if (opcode == 0x12 || opcode == 0x13) {
                            int constant =
                                    opcode == 0x12
                                            ? method.code[p + 1] & 255
                                            : LegacyClassFile.u2(method.code, p + 1);
                            key =
                                    shape.tags[constant] == 8
                                            && "x".equals(shape.text[shape.first[constant]]);
                            budget = 8;
                        } else if (key
                                && --budget > 0
                                && (opcode == 0xb4
                                                && shape.referenceType(
                                                                LegacyClassFile.u2(
                                                                        method.code, p + 1))
                                                        .equals("I")
                                        || opcode == 0xb6
                                                && shape.referenceType(
                                                                LegacyClassFile.u2(
                                                                        method.code, p + 1))
                                                        .equals("()I"))) return true;
                    }
                }
        }
        return false;
    }
}
