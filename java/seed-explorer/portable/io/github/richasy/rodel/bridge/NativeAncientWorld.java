package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Disposable direct world driven entirely by the installed pre-integrated-server generator. */
final class NativeAncientWorld extends NativeSeedContext {
    private final NativeAncientSymbols symbols;
    private final NativeAncientFluids fluidSource;
    private final NativeAncientColors surfaceColors;
    private final Object world, store, format, source, generator;
    private final File directory;
    private final Method chunk, baseChunk, block, metadata, height, biome;
    private final Field nativeBlocks, nativeHeights;
    private final Object info;
    private final Map<Integer, NumericState[]> states = new HashMap<>();
    private final LinkedHashMap<Long, Object> samples =
            new LinkedHashMap<Long, Object>(128, .75f, true) {
                @Override
                protected boolean removeEldestEntry(Map.Entry<Long, Object> entry) {
                    return size() > 128;
                }
            };

    NativeAncientWorld(
            NativeAncientSymbols symbols,
            File directory,
            long seed,
            String preset,
            String dimension)
            throws Exception {
        this.symbols = symbols;
        this.directory = directory;
        Object options = symbols.options(seed, preset);
        if (!directory.mkdirs() && !directory.isDirectory())
            throw new IOException("SEED_WORKSPACE_UNAVAILABLE");
        Class<?> storeType = symbols.worldConstructor.getParameterTypes()[0];
        if (storeType == File.class) {
            format = null;
            store = directory;
        } else {
            List<Class<?>> factories = new ArrayList<>();
            for (LegacyClassFile shape : symbols.classes.values()) {
                boolean constructor = false, factory = false;
                for (LegacyClassFile.Member method : shape.methods) {
                    if ((method.access & 1) != 0
                            && method.name.equals("<init>")
                            && method.descriptor.equals("(Ljava/io/File;)V")) constructor = true;
                    if ((method.access & 1) != 0
                            && method.descriptor.equals(
                                    "(Ljava/lang/String;Z)L"
                                            + storeType.getName().replace('.', '/')
                                            + ";")) factory = true;
                }
                if (constructor && factory) factories.add(NativeAncientSymbols.load(shape.name));
            }
            List<Class<?>> all = new ArrayList<>(factories);
            factories.removeIf(
                    candidate ->
                            all.stream()
                                    .anyMatch(
                                            other ->
                                                    other != candidate
                                                            && candidate.isAssignableFrom(other)));
            Class<?> factoryType = NativeSeedSymbols.only(factories, "ANCIENT_SAVE_FORMAT");
            format = factoryType.getConstructor(File.class).newInstance(directory);
            List<Method> createStore = new ArrayList<>();
            for (Method method : factoryType.getMethods())
                if (method.getReturnType() == storeType
                        && Arrays.equals(
                                method.getParameterTypes(),
                                new Class<?>[] {String.class, boolean.class}))
                    createStore.add(method);
            store =
                    NativeSeedSymbols.only(createStore, "ANCIENT_SAVE_FACTORY")
                            .invoke(format, "preview", false);
        }
        int dimensionId =
                dimension.equals("overworld")
                        ? 0
                        : dimension.equals("the_nether")
                                ? -1
                                : dimension.equals(symbols.positiveDimension())
                                        ? 1
                                        : Integer.MIN_VALUE;
        if (dimensionId == Integer.MIN_VALUE)
            throw new IllegalArgumentException("Unregistered original dimension.");
        if (dimensionId == 0)
            world =
                    symbols.worldConstructor.newInstance(
                            store,
                            storeType == File.class ? "preview" : "Seed exploration",
                            options);
        else {
            if (symbols.providerFactory == null)
                throw new IllegalArgumentException(
                        "This original world API does not provide the selected dimension.");
            Object provider = symbols.provider(dimensionId);
            if (provider == null)
                throw new IllegalArgumentException(
                        "This original world API does not provide the selected dimension.");
            world =
                    symbols.worldType
                            .getConstructor(
                                    storeType,
                                    String.class,
                                    symbols.worldConstructor.getParameterTypes()[2],
                                    symbols.providerFactory.getReturnType())
                            .newInstance(
                                    store,
                                    storeType == File.class ? "preview" : "Seed exploration",
                                    options,
                                    provider);
        }
        if (symbols.worldInfo == symbols.worldType) info = world;
        else {
            List<Field> infos = new ArrayList<>();
            for (Field field : NativeSeedSymbols.fields(symbols.worldType))
                if (field.getType() == symbols.worldInfo) infos.add(field);
            info = NativeSeedSymbols.only(infos, "ANCIENT_WORLD_INFO_FIELD").get(world);
        }
        List<Method> chunkQueries = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods()) {
            Class<?>[] args = method.getParameterTypes();
            if (!Arrays.equals(args, new Class<?>[] {int.class, int.class})
                    || method.getReturnType().isPrimitive()) continue;
            LegacyClassFile result =
                    symbols.classes.get(method.getReturnType().getName().replace('.', '/'));
            if (result == null) continue;
            for (LegacyClassFile.Member member : result.methods)
                if (member.descriptor.equals("()[B")
                        || member.descriptor.equals("(III)I")
                                && member.code != null
                                && accessesByteArray(result, member)) {
                    chunkQueries.add(method);
                    break;
                }
        }
        if (chunkQueries.size() > 1) chunkQueries.removeIf(method -> !callsInterfaceChunk(method));
        chunk = NativeSeedSymbols.only(chunkQueries, "ANCIENT_CHUNK_QUERY");
        Class<?> chunkType = chunk.getReturnType();
        LegacyClassFile chunkShape = symbols.classes.get(chunkType.getName().replace('.', '/'));
        List<Method> blockQueries = new ArrayList<>(), heights = new ArrayList<>();
        for (LegacyClassFile.Member method : chunkShape.methods)
            if ((method.access & 1) != 0 && method.code != null) {
                if (method.descriptor.equals("(III)I") && accessesByteArray(chunkShape, method))
                    blockQueries.add(
                            chunkType.getMethod(method.name, int.class, int.class, int.class));
                if (method.descriptor.equals("(II)I") && accessesHeightArray(chunkShape, method))
                    heights.add(chunkType.getMethod(method.name, int.class, int.class));
            }
        boolean flat = !blockQueries.isEmpty();
        block =
                flat
                        ? NativeSeedSymbols.only(blockQueries, "ANCIENT_BLOCK_QUERY")
                        : NativeAncientBlocks.blockQuery(symbols.classes, chunkType);
        height = NativeSeedSymbols.only(heights, "ANCIENT_HEIGHT_QUERY");
        nativeBlocks = flat ? byteArrayField(chunkShape, block) : null;
        nativeHeights = byteArrayField(chunkShape, height);
        metadata =
                flat
                        ? metadataReader(chunkShape)
                        : NativeAncientBlocks.metadataQuery(symbols.classes, chunkType);
        fluidSource = new NativeAncientFluids(symbols.classes, symbols.blockType);
        surfaceColors = symbols.biomeType == null ? NativeAncientColors.open(symbols) : null;
        List<Object> generators = new ArrayList<>();
        List<Method> generateMethods = new ArrayList<>();
        for (Field field : NativeSeedSymbols.fields(symbols.worldType)) {
            Object provider = field.get(world);
            if (provider == null
                    || provider.getClass().getClassLoader() == null
                    || !providesChunks(provider.getClass(), chunkType)) continue;
            for (Field inner : NativeSeedSymbols.fields(provider.getClass())) {
                Object value = inner.get(provider);
                if (value == null || value == world || value.getClass().getClassLoader() == null)
                    continue;
                for (Method method : value.getClass().getMethods())
                    if (method.getReturnType() == chunkType
                            && Arrays.equals(
                                    method.getParameterTypes(),
                                    new Class<?>[] {int.class, int.class})
                            && !delegates(method)) {
                        boolean duplicate = false;
                        for (int i = 0; i < generators.size(); i++)
                            if (generators.get(i) == value && generateMethods.get(i).equals(method))
                                duplicate = true;
                        if (!duplicate) {
                            generators.add(value);
                            generateMethods.add(method);
                        }
                    }
            }
        }
        generator = NativeSeedSymbols.only(generators, "ANCIENT_NATIVE_GENERATOR");
        baseChunk = NativeSeedSymbols.only(generateMethods, "ANCIENT_BASE_CHUNK");
        Object origin = baseChunk.invoke(generator, 0, 0);
        minimumY = 0;
        if (flat) {
            int columns = Array.getLength(nativeHeights.get(origin)),
                    blocks = Array.getLength(nativeBlocks.get(origin));
            if (columns < 1 || blocks % columns != 0)
                throw new IOException("SEED_ANCIENT_STORAGE_INVALID");
            heightLimit = blocks / columns;
        } else heightLimit = Array.getLength(sections(chunkShape, block).get(origin)) * 16;
        biomeIds = new IdentityHashMap<>();
        if (symbols.biomeType == null) {
            source = null;
            biome = null;
        } else {
            List<Field> labels = new ArrayList<>();
            for (Field field : symbols.biomeType.getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers()) && field.getType() == String.class) {
                    field.setAccessible(true);
                    labels.add(field);
                }
            Field label = NativeSeedSymbols.only(labels, "ANCIENT_BIOME_NAME");
            for (Field field : symbols.biomeType.getDeclaredFields())
                if (Modifier.isStatic(field.getModifiers())
                        && symbols.biomeType.isAssignableFrom(field.getType())) {
                    field.setAccessible(true);
                    Object value = field.get(null);
                    if (value == null || biomeIds.containsKey(value)) continue;
                    String name = (String) label.get(value);
                    if (name == null || name.trim().isEmpty())
                        throw new IOException("SEED_ANCIENT_BIOME_NAME_INVALID");
                    String id =
                            "legacy:biome_"
                                    + name.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9]+", "_");
                    if (biomeNames.put(id, name) != null)
                        throw new IOException("SEED_ANCIENT_BIOME_AMBIGUOUS");
                    biomeIds.put(value, id);
                }
            List<Method> managers = new ArrayList<>(), biomeQueries = new ArrayList<>();
            for (Method method : symbols.worldType.getMethods())
                if (method.getParameterTypes().length == 0) {
                    for (Method query : method.getReturnType().getMethods())
                        if (query.getReturnType() == symbols.biomeType
                                && Arrays.equals(
                                        query.getParameterTypes(),
                                        new Class<?>[] {int.class, int.class})) {
                            managers.add(method);
                            biomeQueries.add(query);
                        }
                }
            source = NativeSeedSymbols.only(managers, "ANCIENT_BIOME_MANAGER").invoke(world);
            biome = NativeSeedSymbols.only(biomeQueries, "ANCIENT_BIOME_QUERY");
        }
        structures =
                symbols.settingsConstructor != null
                        ? new NativeSeedStructures(symbols.classes, world, generator)
                        : new NativeStructureSource() {
                            @Override
                            Set<String> ids() {
                                return Collections.emptySet();
                            }

                            @Override
                            List<Map<String, Object>> find(
                                    int minX,
                                    int minZ,
                                    int maxX,
                                    int maxZ,
                                    List<String> targets,
                                    AtomicBoolean cancelled) {
                                return new ArrayList<>();
                            }
                        };
    }

    private static boolean accessesByteArray(LegacyClassFile type, LegacyClassFile.Member method) {
        boolean array = false, read = false;
        for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
            int opcode = method.code[p] & 255;
            if (opcode == 0xb4
                    && type.name.equals(type.referenceOwner(LegacyClassFile.u2(method.code, p + 1)))
                    && "[B".equals(type.referenceType(LegacyClassFile.u2(method.code, p + 1))))
                array = true;
            if (opcode == 0x33) read = true;
        }
        return array && read;
    }

    private static boolean accessesHeightArray(
            LegacyClassFile type, LegacyClassFile.Member method) {
        boolean array = false, read = false;
        for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
            int opcode = method.code[p] & 255;
            if (opcode >= 0x99 && opcode <= 0xab
                    || opcode >= 0xb5 && opcode <= 0xb9
                    || opcode >= 0x36 && opcode <= 0x56) return false;
            if (opcode == 0xb4) {
                int ref = LegacyClassFile.u2(method.code, p + 1);
                array |=
                        type.name.equals(type.referenceOwner(ref))
                                && ("[B".equals(type.referenceType(ref))
                                        || "[I".equals(type.referenceType(ref)));
            }
            read |= opcode == 0x2e || opcode == 0x33;
        }
        return array && read;
    }

    private Field sections(LegacyClassFile type, Method query) throws Exception {
        Set<String> fields = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : type.methods)
            if (method.name.equals(query.getName())
                    && method.descriptor.equals("(III)I")
                    && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (type.name.equals(type.referenceOwner(ref))
                                && type.referenceType(ref).startsWith("[L"))
                            fields.add(type.referenceName(ref));
                    }
        Field field =
                query.getDeclaringClass()
                        .getDeclaredField(
                                NativeSeedSymbols.only(
                                        new ArrayList<>(fields), "ANCIENT_SECTION_ARRAY"));
        field.setAccessible(true);
        return field;
    }

    private Field byteArrayField(LegacyClassFile type, Method query) throws Exception {
        String descriptor = query.getParameterTypes().length == 3 ? "(III)I" : "(II)I";
        Set<String> fields = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : type.methods)
            if (method.name.equals(query.getName())
                    && method.descriptor.equals(descriptor)
                    && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (type.name.equals(type.referenceOwner(ref))
                                && ("[B".equals(type.referenceType(ref))
                                        || "[I".equals(type.referenceType(ref))))
                            fields.add(type.referenceName(ref));
                    }
        Field field =
                query.getDeclaringClass()
                        .getDeclaredField(
                                NativeSeedSymbols.only(
                                        new ArrayList<>(fields), "ANCIENT_STORAGE_FIELD"));
        field.setAccessible(true);
        return field;
    }

    private Method metadataReader(LegacyClassFile chunkType) throws Exception {
        Set<String> fields = new LinkedHashSet<>();
        for (LegacyClassFile serializer : symbols.classes.values())
            if (NativeSeedSymbols.contains(serializer, "Blocks", "Data", "TileEntities"))
                for (LegacyClassFile.Member method : serializer.methods) {
                    if (method.code == null) continue;
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
                            budget =
                                    serializer.tags[constant] == 8
                                                    && "Data"
                                                            .equals(
                                                                    serializer
                                                                            .text[
                                                                            serializer
                                                                                    .first[
                                                                                    constant]])
                                            ? 8
                                            : 0;
                        } else if (budget > 0 && --budget > 0 && opcode == 0xb4) {
                            int ref = LegacyClassFile.u2(method.code, p + 1);
                            if (chunkType.name.equals(serializer.referenceOwner(ref)))
                                fields.add(serializer.referenceName(ref));
                        }
                    }
                }
        String field =
                NativeSeedSymbols.only(new ArrayList<>(fields), "ANCIENT_METADATA_NBT_FIELD");
        List<Method> readers = new ArrayList<>();
        for (LegacyClassFile.Member method : chunkType.methods)
            if (method.descriptor.equals("(III)I") && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (chunkType.name.equals(chunkType.referenceOwner(ref))
                                && field.equals(chunkType.referenceName(ref))) {
                            readers.add(
                                    chunk.getReturnType()
                                            .getMethod(
                                                    method.name, int.class, int.class, int.class));
                            break;
                        }
                    }
        return NativeSeedSymbols.only(readers, "ANCIENT_METADATA_READER");
    }

    private boolean callsInterfaceChunk(Method query) {
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

    private boolean delegates(Method query) {
        LegacyClassFile type =
                symbols.classes.get(query.getDeclaringClass().getName().replace('.', '/'));
        String desc = "(II)L" + query.getReturnType().getName().replace('.', '/') + ";";
        for (LegacyClassFile.Member method : type.methods)
            if (method.name.equals(query.getName())
                    && method.descriptor.equals(desc)
                    && method.code != null) {
                byte[] code = method.code;
                if (code.length == 7
                        && code[0] == 0x2a
                        && code[1] == 0x1b
                        && code[2] == 0x1c
                        && (code[3] & 255) == 0xb6
                        && (code[6] & 255) == 0xb0) return true;
            }
        return false;
    }

    private static boolean providesChunks(Class<?> provider, Class<?> chunk) {
        for (Method method : provider.getMethods())
            if (method.getReturnType() == chunk
                    && Arrays.equals(
                            method.getParameterTypes(), new Class<?>[] {int.class, int.class}))
                return true;
        return false;
    }

    private Object baseChunk(int x, int z) throws Exception {
        long key = ((long) (x >> 4) << 32) ^ ((z >> 4) & 0xffffffffL);
        Object value = samples.get(key);
        if (value == null) {
            value = baseChunk.invoke(generator, x >> 4, z >> 4);
            samples.put(key, value);
        }
        return value;
    }

    private NumericState state(Object chunk, int x, int y, int z) throws Exception {
        int id = ((Number) block.invoke(chunk, x & 15, y, z & 15)).intValue(),
                data = ((Number) metadata.invoke(chunk, x & 15, y, z & 15)).intValue();
        if (id < 0 || data < 0 || data > 15) throw new IOException("SEED_ANCIENT_BLOCK_INVALID");
        NumericState[] values = states.computeIfAbsent(id, ignored -> new NumericState[16]);
        if (values[data] == null) values[data] = new NumericState(id, data);
        return values[data];
    }

    @Override
    String biome(int x, int y, int z) throws Exception {
        if (biome == null)
            throw new IOException("The installed original world has no biome system.");
        String id = biomeIds.get(biome.invoke(source, x, z));
        if (id == null) throw new IOException("SEED_BIOME_UNREGISTERED");
        return id;
    }

    @Override
    List<String> dimensions() throws Exception {
        List<String> result = new ArrayList<>(Arrays.asList("overworld"));
        if (symbols.provider(-1) != null) result.add("the_nether");
        if (symbols.provider(1) != null) result.add(symbols.positiveDimension());
        return result;
    }

    @Override
    boolean seedBoundSpawn() {
        return symbols.seedBoundSpawn;
    }

    @Override
    List<String> possibleBiomes() throws Exception {
        if (source == null) return Collections.emptyList();
        Set<String> fixed = new TreeSet<>();
        for (Field field : NativeSeedSymbols.fields(source.getClass()))
            if (field.getType() == symbols.biomeType) {
                Object value = field.get(source);
                if (value != null && biomeIds.containsKey(value)) fixed.add(biomeIds.get(value));
            }
        if (!fixed.isEmpty()) return new ArrayList<>(fixed);
        Set<String> result = new TreeSet<>();
        for (Field field : symbols.biomeType.getDeclaredFields())
            if (Modifier.isStatic(field.getModifiers())
                    && field.getType() == Array.newInstance(symbols.biomeType, 0).getClass()) {
                field.setAccessible(true);
                Object values = field.get(null);
                if (values != null)
                    for (int i = 0; i < Array.getLength(values); i++) {
                        String id = biomeIds.get(Array.get(values, i));
                        if (id != null) result.add(id);
                    }
            }
        return result.isEmpty() ? super.possibleBiomes() : new ArrayList<>(result);
    }

    @Override
    int[] column(int x, int z, Integer slice, boolean terrain) throws Exception {
        if (slice != null) return new int[] {slice, 0};
        Object chunk = baseChunk(x, z);
        int top = ((Number) height.invoke(chunk, x & 15, z & 15)).intValue() - 1;
        if (top < minimumY) {
            top = heightLimit - 1;
            while (top >= minimumY && state(chunk, x, top, z).numericId == 0) top--;
        }
        int fluid = top < minimumY ? 0 : fluid(state(chunk, x, top, z).numericId), ground = top;
        if (terrain && fluid != 0)
            while (ground >= minimumY && fluid(state(chunk, x, ground, z).numericId) != 0) ground--;
        return new int[] {terrain ? ground : top, terrain ? fluid : 0};
    }

    private int fluid(int id) throws Exception {
        return fluidSource.kind(id);
    }

    @Override
    int surfaceColor(int x, int y, int z) throws Exception {
        if (surfaceColors == null || y < minimumY || y >= heightLimit)
            return super.surfaceColor(x, y, z);
        NumericState value = state(baseChunk(x, z), x, y, z);
        return surfaceColors.color(value.numericId, value.data);
    }

    @Override
    Map<String, Object> spawn() throws Exception {
        int x = serializedField(symbols.worldInfo, "SpawnX").getInt(info),
                y = serializedField(symbols.worldInfo, "SpawnY").getInt(info),
                z = serializedField(symbols.worldInfo, "SpawnZ").getInt(info);
        return Json.object("id", "spawn", "kind", "spawn", "x", x, "y", y, "z", z, "pieces", 0);
    }

    private Field serializedField(Class<?> owner, String key) throws Exception {
        Set<String> names = new LinkedHashSet<>();
        LegacyClassFile type = symbols.classes.get(owner.getName().replace('.', '/'));
        for (LegacyClassFile.Member method : type.methods)
            if (method.code != null) {
                int budget = 0;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode == 0x12 || opcode == 0x13) {
                        int constant =
                                opcode == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        budget =
                                type.tags[constant] == 8
                                                && key.equals(type.text[type.first[constant]])
                                        ? 8
                                        : 0;
                    } else if (budget > 0 && --budget > 0 && opcode == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (type.name.equals(type.referenceOwner(ref))
                                && "I".equals(type.referenceType(ref)))
                            names.add(type.referenceName(ref));
                    }
                }
            }
        Field field =
                owner.getDeclaredField(
                        NativeSeedSymbols.only(new ArrayList<>(names), "ANCIENT_NBT_" + key));
        field.setAccessible(true);
        return field;
    }

    @Override
    File save(int minX, int minZ, int maxX, int maxZ, boolean loot, AtomicBoolean cancelled)
            throws Exception {
        if ((long) (maxX >> 4) - (minX >> 4) > 32 || (long) (maxZ >> 4) - (minZ >> 4) > 32)
            throw new IOException("SEED_REGION_BUDGET");
        for (int z = (minZ >> 4) - 1; z <= (maxZ >> 4) + 1; z++)
            for (int x = (minX >> 4) - 1; x <= (maxX >> 4) + 1; x++) {
                check(cancelled);
                chunk.invoke(world, x, z);
            }
        readInventories();
        List<Method> saves = new ArrayList<>();
        for (Method method : symbols.worldType.getMethods())
            if (method.getReturnType() == void.class
                    && method.getParameterTypes().length == 2
                    && method.getParameterTypes()[0] == boolean.class
                    && method.getParameterTypes()[1].isInterface()) saves.add(method);
        if (saves.isEmpty()) saveDirectWorld();
        else NativeSeedSymbols.only(saves, "ANCIENT_SAVE_WORLD").invoke(world, true, null);
        NativeAncientIO.await(symbols.classes);
        List<Method> flushes = new ArrayList<>();
        for (Method method : format == null ? new Method[0] : format.getClass().getMethods())
            if (method.getDeclaringClass() != Object.class
                    && method.getReturnType() == void.class
                    && method.getParameterTypes().length == 0) flushes.add(method);
        if (!flushes.isEmpty())
            NativeSeedSymbols.only(flushes, "ANCIENT_SAVE_FLUSH").invoke(format);
        return new File(directory, "preview");
    }

    /** Uses the original early world's combined metadata and chunk-save entry point. */
    private void saveDirectWorld() throws Exception {
        LegacyClassFile type = symbols.classes.get(symbols.worldType.getName().replace('.', '/'));
        List<Method> saves = new ArrayList<>();
        for (LegacyClassFile.Member method : type.methods) {
            if (!method.descriptor.equals("(Z)V") || method.code == null) continue;
            Set<String> keys = new HashSet<>();
            for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                int opcode = method.code[p] & 255;
                if (opcode != 0x12 && opcode != 0x13) continue;
                int index =
                        opcode == 0x12
                                ? method.code[p + 1] & 255
                                : LegacyClassFile.u2(method.code, p + 1);
                if (type.tags[index] == 8) keys.add(type.text[type.first[index]]);
            }
            if (keys.containsAll(
                    Arrays.asList("level.dat", "RandomSeed", "SpawnX", "SpawnY", "SpawnZ"))) {
                Method save = symbols.worldType.getDeclaredMethod(method.name, boolean.class);
                save.setAccessible(true);
                saves.add(save);
            }
        }
        NativeSeedSymbols.only(saves, "ANCIENT_DIRECT_SAVE_WORLD").invoke(world, true);
    }

    private void readInventories() throws Exception {
        Set<Object> seen = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        for (Field field : NativeSeedSymbols.fields(symbols.worldType))
            if (List.class.isAssignableFrom(field.getType())) {
                Object values = field.get(world);
                if (!(values instanceof List<?>)) continue;
                for (Object entity : new ArrayList<>((List<?>) values)) {
                    if (entity == null
                            || entity.getClass().getClassLoader() == null
                            || !seen.add(entity)) continue;
                    List<Integer> capacities = new ArrayList<>();
                    for (Field inventory : NativeSeedSymbols.fields(entity.getClass()))
                        if (inventory.getType().isArray()
                                && !inventory.getType().getComponentType().isPrimitive()) {
                            LegacyClassFile item =
                                    symbols.classes.get(
                                            inventory
                                                    .getType()
                                                    .getComponentType()
                                                    .getName()
                                                    .replace('.', '/'));
                            if (item != null
                                    && NativeSeedSymbols.contains(item, "id", "Count", "Damage")) {
                                Object items = inventory.get(entity);
                                if (items != null)
                                    capacities.add(
                                            NativeAncientInventory.capacity(
                                                    symbols.classes,
                                                    entity,
                                                    inventory,
                                                    Array.getLength(items)));
                            }
                        }
                    if (capacities.isEmpty()) continue;
                    Class<?> owner = entity.getClass();
                    while (owner != null) {
                        LegacyClassFile shape =
                                symbols.classes.get(owner.getName().replace('.', '/'));
                        if (shape != null && NativeSeedSymbols.contains(shape, "id", "x", "y", "z"))
                            break;
                        owner = owner.getSuperclass();
                    }
                    if (owner == null) throw new IOException("SEED_ANCIENT_INVENTORY_POSITION");
                    int x = serializedField(owner, "x").getInt(entity),
                            y = serializedField(owner, "y").getInt(entity),
                            z = serializedField(owner, "z").getInt(entity);
                    inventories.add(
                            Json.object(
                                    "x",
                                    x,
                                    "y",
                                    y,
                                    "z",
                                    z,
                                    "slots",
                                    NativeSeedSymbols.only(
                                            capacities, "ANCIENT_INVENTORY_CAPACITY"),
                                    "id",
                                    state(chunk.invoke(world, x >> 4, z >> 4), x, y, z).id));
                }
            }
    }

    @SuppressWarnings("unchecked")
    @Override
    Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
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
        if (minY < minimumY
                || maxY >= heightLimit
                || maxX < minX
                || maxY < minY
                || maxZ < minZ
                || volume > (include ? 4194304 : 16777216))
            throw new IllegalArgumentException("SEED_VOLUME_BUDGET");
        ByteArrayOutputStream blocks = new ByteArrayOutputStream(include ? (int) volume * 2 : 0);
        Map<NumericState, Integer> indices = new IdentityHashMap<>();
        List<Object> palette = new ArrayList<>();
        Map<String, Long> materials = new TreeMap<>();
        Map<Long, Object> chunks = new HashMap<>();
        long started = System.nanoTime();
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++) {
                    check(cancelled);
                    long key = ((long) (x >> 4) << 32) ^ ((z >> 4) & 0xffffffffL);
                    Object generated = chunks.get(key);
                    if (generated == null) {
                        generated = chunk.invoke(world, x >> 4, z >> 4);
                        chunks.put(key, generated);
                    }
                    NumericState state = state(generated, x, y, z);
                    Integer index = indices.get(state);
                    if (index == null) {
                        index = palette.size();
                        if (index >= 65536) throw new IOException("SEED_PALETTE_BUDGET");
                        indices.put(state, index);
                        palette.add(state.json());
                    }
                    materials.put(state.id, materials.getOrDefault(state.id, 0L) + 1);
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

    private static int number(Map<String, Object> object, String name) {
        return ((Number) object.get(name)).intValue();
    }

    private static void check(AtomicBoolean cancelled) {
        if (cancelled.get()) throw new java.util.concurrent.CancellationException();
    }

    private static final class NumericState {
        final int numericId, data;
        final String id;

        NumericState(int id, int data) {
            numericId = id;
            this.data = data;
            this.id = id == 0 ? "minecraft:air" : "legacy:block_" + id;
        }

        Map<String, Object> json() {
            return Json.object(
                    "id",
                    id,
                    "properties",
                    Collections.singletonMap("legacy_data", Integer.toString(data)));
        }
    }
}
