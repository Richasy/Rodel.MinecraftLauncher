package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.jar.*;

/** Native direct-world APIs predating an integrated server, with or without WorldSettings. */
final class NativeAncientSymbols implements Closeable {
    final Map<String, LegacyClassFile> classes = new LinkedHashMap<>();
    final Class<?> worldInfo, worldType, biomeType, blockType;
    final Constructor<?> worldConstructor;
    final Constructor<?> settingsConstructor;
    final boolean seedBoundSpawn;
    final Method providerFactory;
    private final Field providerDimension;
    private final JarFile jar;

    private NativeAncientSymbols(
            JarFile jar, Map<String, LegacyClassFile> classes, Class<?> info, Constructor<?> world)
            throws Exception {
        this.jar = jar;
        this.classes.putAll(classes);
        worldInfo = info;
        worldConstructor = world;
        worldType = world.getDeclaringClass();
        settingsConstructor =
                world.getParameterTypes()[2] == long.class
                        ? null
                        : settingsConstructor(world.getParameterTypes()[2]);
        seedBoundSpawn =
                settingsConstructor != null
                        && usesSeededSpawn(classes.get(worldType.getName().replace('.', '/')));
        List<Class<?>> biomes = new ArrayList<>(), blocks = new ArrayList<>();
        for (LegacyClassFile type : classes.values()) {
            if (NativeSeedSymbols.contains(type, "Rainforest", "Plains", "Swampland")
                    || NativeSeedSymbols.contains(type, "Ocean", "Plains", "Hell"))
                biomes.add(load(type.name));
            if (NativeSeedSymbols.contains(type, "tile."))
                for (LegacyClassFile.Member field : type.fields)
                    if ((field.access & 8) != 0
                            && field.descriptor.equals("[L" + type.name + ";")) {
                        blocks.add(load(type.name));
                        break;
                    }
        }
        if (blocks.isEmpty())
            for (LegacyClassFile type : classes.values()) {
                boolean registry = false, materialConstructor = false;
                for (LegacyClassFile.Member field : type.fields)
                    if ((field.access & 8) != 0 && field.descriptor.equals("[L" + type.name + ";"))
                        registry = true;
                for (LegacyClassFile.Member method : type.methods)
                    if (method.name.equals("<init>")
                            && method.descriptor.matches("\\(II?L[^;]+;\\)V"))
                        materialConstructor = true;
                if (registry && materialConstructor) blocks.add(load(type.name));
            }
        biomeType =
                biomes.isEmpty() ? null : NativeSeedSymbols.only(biomes, "ANCIENT_BIOME_SOURCE");
        blockType = NativeSeedSymbols.only(blocks, "ANCIENT_BLOCK_REGISTRY");
        List<Method> providers = new ArrayList<>();
        for (Constructor<?> candidate : worldType.getConstructors()) {
            Class<?>[] args = candidate.getParameterTypes();
            if (args.length != 4
                    || args[0] != world.getParameterTypes()[0]
                    || args[1] != String.class
                    || args[2] != world.getParameterTypes()[2]) continue;
            for (Method method : args[3].getMethods())
                if (Modifier.isStatic(method.getModifiers())
                        && method.getReturnType() == args[3]
                        && Arrays.equals(method.getParameterTypes(), new Class<?>[] {int.class}))
                    providers.add(method);
        }
        providerFactory =
                providers.isEmpty()
                        ? null
                        : NativeSeedSymbols.only(providers, "ANCIENT_DIMENSION_FACTORY");
        List<Field> dimensions = new ArrayList<>();
        if (providerFactory != null)
            for (Field field : providerFactory.getReturnType().getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers()) && field.getType() == int.class) {
                    field.setAccessible(true);
                    dimensions.add(field);
                }
        providerDimension = dimensions.size() == 1 ? dimensions.get(0) : null;
    }

    /** Opens only when actual world-info and direct-world constructors prove this API family. */
    static NativeAncientSymbols tryOpen(String path) throws Exception {
        JarFile jar = new JarFile(path);
        try {
            Map<String, LegacyClassFile> classes = new LinkedHashMap<>();
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
            List<LegacyClassFile> infoTypes = new ArrayList<>();
            for (LegacyClassFile type : classes.values())
                if (NativeSeedSymbols.contains(type, "RandomSeed", "SpawnX", "SpawnY", "SpawnZ"))
                    for (LegacyClassFile.Member method : type.methods)
                        if (method.name.equals("<init>")
                                && method.descriptor.equals("(JLjava/lang/String;)V"))
                            infoTypes.add(type);
            if (infoTypes.isEmpty()) {
                List<Constructor<?>> alphaWorlds = new ArrayList<>();
                for (LegacyClassFile type : classes.values())
                    if (NativeSeedSymbols.contains(
                            type, "RandomSeed", "SpawnX", "SpawnY", "SpawnZ", "level.dat"))
                        for (Constructor<?> constructor : load(type.name).getDeclaredConstructors())
                            if (Arrays.equals(
                                    constructor.getParameterTypes(),
                                    new Class<?>[] {File.class, String.class, long.class}))
                                alphaWorlds.add(constructor);
                if (alphaWorlds.isEmpty()) {
                    NativeAncientSymbols direct = directSettings(jar, classes);
                    if (direct != null) return direct;
                    if (unseededFiniteWorld(classes))
                        throw new IOException(
                                "SEED_ORIGINAL_SEED_UNAVAILABLE: The installed original client"
                                        + " creates finite worlds without a native seed input or a"
                                        + " persisted world seed. Saved worlds remain readable.");
                    jar.close();
                    return null;
                }
                Constructor<?> alpha =
                        NativeSeedSymbols.only(alphaWorlds, "ANCIENT_FILE_WORLD_CONSTRUCTOR");
                alpha.setAccessible(true);
                return new NativeAncientSymbols(jar, classes, alpha.getDeclaringClass(), alpha);
            }
            LegacyClassFile info = NativeSeedSymbols.only(infoTypes, "ANCIENT_WORLD_INFO");
            List<Constructor<?>> worlds = new ArrayList<>();
            for (LegacyClassFile type : classes.values()) {
                boolean ownsInfo = false;
                for (LegacyClassFile.Member field : type.fields)
                    if ((field.access & 8) == 0 && field.descriptor.equals("L" + info.name + ";"))
                        ownsInfo = true;
                if (!ownsInfo) continue;
                for (Constructor<?> constructor : load(type.name).getConstructors()) {
                    Class<?>[] args = constructor.getParameterTypes();
                    if (args.length == 3
                            && args[0].isInterface()
                            && args[1] == String.class
                            && args[2] == long.class) worlds.add(constructor);
                }
            }
            if (worlds.isEmpty()) {
                jar.close();
                return null;
            }
            return new NativeAncientSymbols(
                    jar,
                    classes,
                    load(info.name),
                    NativeSeedSymbols.only(worlds, "ANCIENT_WORLD_CONSTRUCTOR"));
        } catch (Throwable error) {
            jar.close();
            throw error;
        }
    }

    /** Proves finite native generation and storage APIs that have no seed parameter or field. */
    private static boolean unseededFiniteWorld(Map<String, LegacyClassFile> classes) {
        for (LegacyClassFile generator : classes.values()) {
            if (NativeSeedSymbols.contains(
                            generator,
                            "level.dat",
                            "java/util/zip/GZIPInputStream",
                            "java/util/zip/GZIPOutputStream")
                    && finiteLevel(classes, generator)
                    && nativeArrayStorage(generator, generator.name)) return true;
            if (!NativeSeedSymbols.contains(generator, "Raising..", "Eroding..")) continue;
            boolean unseeded = false, seedInput = false;
            List<LegacyClassFile> levels = new ArrayList<>();
            for (LegacyClassFile.Member method : generator.methods) {
                if ((method.access & 1) != 0) {
                    String args =
                            method.descriptor.substring(0, method.descriptor.indexOf(')') + 1);
                    seedInput |= args.contains("J") || args.contains("Ljava/util/Random;");
                    if (method.descriptor.startsWith("(Ljava/lang/String;III)L")) {
                        String result =
                                method.descriptor.substring(
                                        method.descriptor.indexOf(')') + 2,
                                        method.descriptor.length() - 1);
                        LegacyClassFile level = classes.get(result);
                        if (level != null) levels.add(level);
                    } else if (method.descriptor.matches("\\(L[^;]+;Ljava/lang/String;III\\)Z")) {
                        String first =
                                method.descriptor.substring(2, method.descriptor.indexOf(';'));
                        LegacyClassFile level = classes.get(first);
                        if (level != null) levels.add(level);
                    }
                }
                if (method.code == null) continue;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode != 0xb7 && opcode != 0xb6) continue;
                    int ref = LegacyClassFile.u2(method.code, p + 1);
                    if (!"java/util/Random".equals(generator.referenceOwner(ref))) continue;
                    String name = generator.referenceName(ref),
                            signature = generator.referenceType(ref);
                    unseeded |= name.equals("<init>") && signature.equals("()V");
                    seedInput |=
                            name.equals("setSeed")
                                    || name.equals("<init>") && signature.equals("(J)V");
                }
            }
            if (!unseeded || seedInput) continue;
            for (LegacyClassFile level : levels) {
                if (!finiteLevel(classes, level)) continue;
                boolean transientRandom = false;
                for (LegacyClassFile.Member field : level.fields) {
                    if ((field.access & 8) != 0) continue;
                    transientRandom |=
                            field.descriptor.equals("Ljava/util/Random;")
                                    && (field.access & 128) != 0;
                }
                if (transientRandom && NativeSeedSymbols.contains(level, "java/io/Serializable"))
                    return true;
                for (LegacyClassFile storage : classes.values())
                    if (nativeArrayStorage(storage, level.name)) return true;
            }
        }
        return false;
    }

    /** Distinguishes finite voxel dimensions and clock metadata from a stored seed. */
    private static boolean finiteLevel(
            Map<String, LegacyClassFile> classes, LegacyClassFile level) {
        if (NativeSeedSymbols.contains(level, "RandomSeed")) return false;
        int dimensions = 0;
        boolean blocks = false;
        for (LegacyClassFile.Member field : level.fields) {
            if ((field.access & 8) != 0) continue;
            if (field.name.toLowerCase(Locale.ROOT).contains("seed")) return false;
            blocks |= field.descriptor.equals("[B");
            if (field.descriptor.equals("I")) dimensions++;
            if (field.descriptor.equals("J") && !clockField(classes, level.name, field.name))
                return false;
        }
        for (LegacyClassFile.Member method : level.methods)
            if ((method.access & 1) != 0) {
                String args = method.descriptor.substring(0, method.descriptor.indexOf(')') + 1);
                if (args.contains("J") || args.contains("Ljava/util/Random;")) return false;
            }
        return blocks && dimensions >= 3;
    }

    private static boolean clockField(
            Map<String, LegacyClassFile> classes, String owner, String name) {
        for (LegacyClassFile type : classes.values())
            for (LegacyClassFile.Member method : type.methods) {
                if (method.code == null) continue;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    if ((method.code[p] & 255) != 0xb8) continue;
                    int call = LegacyClassFile.u2(method.code, p + 1);
                    if (!"java/lang/System".equals(type.referenceOwner(call))
                            || !"currentTimeMillis".equals(type.referenceName(call))
                            || !"()J".equals(type.referenceType(call))) continue;
                    int next = LegacyClassFile.next(method.code, p);
                    if (next >= method.code.length || (method.code[next] & 255) != 0xb5) continue;
                    int field = LegacyClassFile.u2(method.code, next + 1);
                    if (owner.equals(type.referenceOwner(field))
                            && name.equals(type.referenceName(field))
                            && "J".equals(type.referenceType(field))) return true;
                }
            }
        return false;
    }

    /** Confirms native gzip serialization writes the level's actual voxel array, not a seed. */
    private static boolean nativeArrayStorage(LegacyClassFile storage, String level) {
        if (!NativeSeedSymbols.contains(storage, "java/util/zip/GZIPOutputStream")) return false;
        for (LegacyClassFile.Member method : storage.methods) {
            if (method.code == null) continue;
            boolean blocks = false, write = false;
            for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                int opcode = method.code[p] & 255;
                if (opcode == 0xb4) {
                    int ref = LegacyClassFile.u2(method.code, p + 1);
                    blocks |=
                            level.equals(storage.referenceOwner(ref))
                                    && "[B".equals(storage.referenceType(ref));
                } else if (opcode == 0xb6) {
                    int ref = LegacyClassFile.u2(method.code, p + 1);
                    write |=
                            storage.referenceOwner(ref).startsWith("java/io/")
                                    && "write".equals(storage.referenceName(ref))
                                    && "([B)V".equals(storage.referenceType(ref));
                }
            }
            if (blocks && write) return true;
        }
        return false;
    }

    private static Constructor<?> settingsConstructor(Class<?> settings) throws Exception {
        List<Constructor<?>> constructors = new ArrayList<>();
        for (Constructor<?> constructor : settings.getConstructors()) {
            Class<?>[] args = constructor.getParameterTypes();
            if ((args.length >= 3 && args.length <= 5)
                    && args[0] == long.class
                    && (args[1] == int.class || args[1].isEnum())
                    && args[2] == boolean.class
                    && (args.length == 3 || args[3] == boolean.class))
                constructors.add(constructor);
        }
        return constructors.isEmpty()
                ? null
                : NativeSeedSymbols.only(constructors, "ANCIENT_WORLD_SETTINGS");
    }

    private static NativeAncientSymbols directSettings(
            JarFile jar, Map<String, LegacyClassFile> classes) throws Exception {
        List<Constructor<?>> worlds = new ArrayList<>();
        List<Class<?>> infos = new ArrayList<>();
        for (LegacyClassFile info : classes.values())
            if (NativeSeedSymbols.contains(info, "RandomSeed", "SpawnX", "SpawnY", "SpawnZ")) {
                Class<?> type = load(info.name);
                for (Constructor<?> constructor : type.getConstructors()) {
                    Class<?>[] args = constructor.getParameterTypes();
                    if (args.length != 2
                            || args[1] != String.class
                            || settingsConstructor(args[0]) == null) continue;
                    for (LegacyClassFile candidate : classes.values()) {
                        boolean ownsInfo = false;
                        for (LegacyClassFile.Member field : candidate.fields)
                            if ((field.access & 8) == 0
                                    && field.descriptor.equals("L" + info.name + ";"))
                                ownsInfo = true;
                        if (!ownsInfo) continue;
                        for (Constructor<?> direct : load(candidate.name).getConstructors()) {
                            Class<?>[] parameters = direct.getParameterTypes();
                            if (parameters.length == 3
                                    && parameters[0].isInterface()
                                    && parameters[1] == String.class
                                    && parameters[2] == args[0]) {
                                worlds.add(direct);
                                infos.add(type);
                            }
                        }
                    }
                }
            }
        if (worlds.isEmpty()) return null;
        return new NativeAncientSymbols(
                jar,
                classes,
                NativeSeedSymbols.only(infos, "ANCIENT_SETTINGS_INFO"),
                NativeSeedSymbols.only(worlds, "ANCIENT_SETTINGS_WORLD"));
    }

    private static boolean usesSeededSpawn(LegacyClassFile world) {
        for (LegacyClassFile.Member method : world.methods)
            if (method.code != null) {
                boolean diagnostic = false, seeded = false, unseeded = false;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode == 0x12 || opcode == 0x13) {
                        int constant =
                                opcode == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        diagnostic |=
                                world.tags[constant] == 8
                                        && "Unable to find spawn biome"
                                                .equals(world.text[world.first[constant]]);
                    } else if (opcode == 0xb7) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if ("java/util/Random".equals(world.referenceOwner(ref))
                                && "<init>".equals(world.referenceName(ref))) {
                            seeded |= "(J)V".equals(world.referenceType(ref));
                            unseeded |= "()V".equals(world.referenceType(ref));
                        }
                    }
                }
                if (diagnostic && seeded && !unseeded) return true;
            }
        return false;
    }

    Object options(long seed, String preset) throws Exception {
        if (settingsConstructor == null) {
            if (!preset.equals("normal"))
                throw NativeAncientPresets.unsupported(
                        "This native world API has no selectable preset.");
            return seed;
        }
        Class<?>[] types = settingsConstructor.getParameterTypes();
        Object[] args = new Object[types.length];
        args[0] = seed;
        args[2] = true;
        if (args.length > 3) args[3] = false;
        if (types[1] == int.class) args[1] = 0;
        else {
            Object selected = null;
            for (Object value : types[1].getEnumConstants())
                if (((Enum<?>) value).name().equals("CREATIVE")) selected = value;
            if (selected == null) throw new IOException("SEED_NATIVE_GAME_MODE_UNAVAILABLE");
            args[1] = selected;
        }
        if (types.length == 5) {
            List<Method> factories = new ArrayList<>();
            for (Method method : types[4].getMethods())
                if (Modifier.isStatic(method.getModifiers())
                        && method.getReturnType() == types[4]
                        && Arrays.equals(method.getParameterTypes(), new Class<?>[] {String.class}))
                    factories.add(method);
            String name =
                    preset.equals("normal")
                            ? "default"
                            : preset.equals("large_biomes") ? "largeBiomes" : preset;
            args[4] = NativeSeedSymbols.only(factories, "ANCIENT_NATIVE_PRESET").invoke(null, name);
            if (args[4] == null)
                throw NativeAncientPresets.unsupported("This original preset is unavailable.");
        } else if (!preset.equals("normal"))
            throw NativeAncientPresets.unsupported(
                    "This native world API has no selectable preset.");
        return settingsConstructor.newInstance(args);
    }

    static Class<?> load(String name) throws ClassNotFoundException {
        return Class.forName(
                name.replace('/', '.'), false, NativeAncientSymbols.class.getClassLoader());
    }

    Object provider(int dimension) throws Exception {
        if (providerFactory == null) return null;
        Object value = providerFactory.invoke(null, dimension);
        LegacyClassFile owner =
                classes.get(providerFactory.getDeclaringClass().getName().replace('.', '/'));
        for (LegacyClassFile.Member method : owner.methods)
            if (method.name.equals(providerFactory.getName())
                    && method.descriptor.equals(
                            "(I)L"
                                    + providerFactory.getReturnType().getName().replace('.', '/')
                                    + ";")
                    && method.code != null
                    && method.code.length >= 2)
                if (method.code[method.code.length - 2] == 0x01
                        && (method.code[method.code.length - 1] & 255) == 0xb0) return value;
        return value != null
                        && providerDimension != null
                        && providerDimension.getInt(value) == dimension
                ? value
                : null;
    }

    /**
     * Keeps the early Sky provider numeric unless native entity registration proves End content.
     */
    String positiveDimension() {
        for (LegacyClassFile type : classes.values()) {
            if (!NativeSeedSymbols.contains(type, "EnderDragon")) continue;
            for (LegacyClassFile.Member method : type.methods) {
                if (method.code == null) continue;
                int classConstant = -1;
                boolean dragon = false;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode == 0x12 || opcode == 0x13) {
                        int index =
                                opcode == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        if (type.tags[index] == 7) classConstant = index;
                        else if (type.tags[index] == 8)
                            dragon = "EnderDragon".equals(type.text[type.first[index]]);
                    } else if (opcode == 0xb8) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (dragon
                                && classConstant >= 0
                                && "(Ljava/lang/Class;Ljava/lang/String;I)V"
                                        .equals(type.referenceType(ref))) return "the_end";
                        dragon = false;
                        classConstant = -1;
                    }
                }
            }
        }
        return "legacy:dimension_1";
    }

    /** Reads the selected original atlas with a strict bound, without consulting user resources. */
    byte[] terrainTexture() throws IOException {
        JarEntry entry = jar.getJarEntry("terrain.png");
        if (entry == null) return null;
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        try (InputStream input = jar.getInputStream(entry)) {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.read(buffer)) >= 0) {
                if (bytes.size() + count > 8388608) throw new IOException("SEED_TEXTURE_BUDGET");
                bytes.write(buffer, 0, count);
            }
        }
        return bytes.toByteArray();
    }

    /** Releases only the selected installed JAR's metadata handle. */
    @Override
    public void close() throws IOException {
        jar.close();
    }
}
