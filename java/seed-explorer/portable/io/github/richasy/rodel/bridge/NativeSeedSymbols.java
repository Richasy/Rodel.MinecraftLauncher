package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.jar.*;

/**
 * Binds original world APIs by class-file and constructor evidence; names and version numbers are
 * never guessed.
 */
final class NativeSeedSymbols implements Closeable {
    final Map<String, LegacyClassFile> classes = new LinkedHashMap<>();
    final Class<?> worldInfo, settings, worldType, serverType, presetType, gameType, biomeType;
    final Constructor<?> worldConstructor, settingsConstructor;
    final Method bootstrap;
    final Class<?> staticBlockType;
    private final JarFile jar;

    NativeSeedSymbols(String path) throws Exception {
        jar = new JarFile(path);
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
                    if (bytes.size() + count > 4194304) throw new IOException("SEED_CLASS_BUDGET");
                    bytes.write(buffer, 0, count);
                }
            }
            LegacyClassFile type = new LegacyClassFile(bytes.toByteArray());
            classes.put(type.name, type);
        }
        LegacyClassFile info =
                unique(
                        "WORLD_INFO",
                        type -> contains(type, "RandomSeed", "SpawnX", "SpawnY", "SpawnZ"));
        worldInfo = load(info.name);
        Constructor<?> selectedSettings = null;
        Class<?> selectedInfoSettings = null;
        for (Constructor<?> candidate : worldInfo.getConstructors()) {
            Class<?>[] parameters = candidate.getParameterTypes();
            if (parameters.length != 2 || parameters[1] != String.class) continue;
            for (Constructor<?> value : parameters[0].getConstructors()) {
                Class<?>[] args = value.getParameterTypes();
                if (args.length == 5
                        && args[0] == long.class
                        && args[1].isEnum()
                        && args[2] == boolean.class
                        && args[3] == boolean.class
                        && !args[4].isPrimitive()) {
                    if (selectedSettings != null) throw new IOException("SEED_SETTINGS_AMBIGUOUS");
                    selectedSettings = value;
                    selectedInfoSettings = parameters[0];
                }
            }
        }
        if (selectedSettings == null)
            throw new IOException(
                    "SEED_WORLD_API_SHAPE: Missing original WorldSettings constructor.");
        settingsConstructor = selectedSettings;
        settings = selectedInfoSettings;
        gameType = selectedSettings.getParameterTypes()[1];
        presetType = selectedSettings.getParameterTypes()[4];
        LegacyClassFile world =
                unique("SERVER_WORLD", type -> contains(type, "Unable to find spawn biome"));
        worldType = load(world.name);
        Constructor<?> selectedWorld = null;
        for (Constructor<?> candidate : worldType.getConstructors()) {
            Class<?>[] args = candidate.getParameterTypes();
            if (args.length == 5 && args[2] == worldInfo && args[3] == int.class
                    || args.length >= 6
                            && args[2] == String.class
                            && args[3] == int.class
                            && args[4] == settings) {
                if (selectedWorld != null) throw new IOException("SEED_WORLD_AMBIGUOUS");
                selectedWorld = candidate;
            }
        }
        if (selectedWorld == null)
            throw new IOException(
                    "SEED_WORLD_API_SHAPE: Missing original server-world constructor.");
        worldConstructor = selectedWorld;
        serverType = selectedWorld.getParameterTypes()[0];
        List<LegacyClassFile> blockHolders = new ArrayList<>();
        for (LegacyClassFile candidate : classes.values())
            if (contains(candidate, "Accessed Blocks before Bootstrap!"))
                blockHolders.add(candidate);
        LegacyClassFile blocks = blockHolders.isEmpty() ? null : only(blockHolders, "BLOCKS");
        LegacyClassFile boot = null;
        if (blocks != null)
            for (LegacyClassFile.Member member : blocks.methods)
                if ("<clinit>".equals(member.name) && member.code != null)
                    for (int p = 0;
                            p < member.code.length;
                            p = LegacyClassFile.next(member.code, p))
                        if ((member.code[p] & 255) == 0xb8) {
                            int ref = LegacyClassFile.u2(member.code, p + 1);
                            if ("()Z".equals(blocks.referenceType(ref))) {
                                boot = classes.get(blocks.referenceOwner(ref));
                                break;
                            }
                        }
        if (boot == null && blocks == null) {
            bootstrap = registrationBootstrap();
            if (bootstrap == null) {
                List<Class<?>> nativeBlocks = new ArrayList<>();
                for (LegacyClassFile candidate : classes.values())
                    if (contains(candidate, "tile."))
                        for (LegacyClassFile.Member field : candidate.fields)
                            if ((field.access & 8) != 0
                                    && field.descriptor.equals("[L" + candidate.name + ";")) {
                                nativeBlocks.add(load(candidate.name));
                                break;
                            }
                staticBlockType = only(nativeBlocks, "STATIC_BLOCK_REGISTRY");
            } else staticBlockType = null;
        } else {
            if (boot == null) throw new IOException("SEED_BOOTSTRAP_UNAVAILABLE");
            List<Method> initializers = new ArrayList<>();
            for (Method method : load(boot.name).getDeclaredMethods())
                if (Modifier.isPublic(method.getModifiers())
                        && Modifier.isStatic(method.getModifiers())
                        && method.getReturnType() == void.class
                        && method.getParameterTypes().length == 0) initializers.add(method);
            bootstrap = only(initializers, "BOOTSTRAP");
            staticBlockType = null;
        }
        biomeType =
                load(
                        unique(
                                        "BIOME_REGISTRY",
                                        type -> {
                                            if (!contains(type, "ocean", "plains", "hell", "sky")
                                                    && !contains(
                                                            type, "Ocean", "Plains", "Hell",
                                                            "The End")
                                                    && !contains(
                                                            type, "Ocean", "Plains", "Hell", "Sky"))
                                                return false;
                                            for (LegacyClassFile.Member method : type.methods)
                                                if ((method.access & 8) != 0
                                                        && method.descriptor.equals(
                                                                "(I)L" + type.name + ";"))
                                                    return true;
                                            for (LegacyClassFile.Member field : type.fields)
                                                if ((field.access & 8) != 0
                                                        && field.descriptor.equals(
                                                                "[L" + type.name + ";"))
                                                    return true;
                                            return false;
                                        })
                                .name);
    }

    /** Finds the original bootstrap by its call to the native block-registration method. */
    private Method registrationBootstrap() throws Exception {
        List<String> registrations = new ArrayList<>();
        for (LegacyClassFile owner : classes.values())
            for (LegacyClassFile.Member member : owner.methods) {
                if ((member.access & 9) != 9
                        || !member.descriptor.equals("()V")
                        || member.code == null) continue;
                Set<String> strings = new HashSet<>();
                for (int p = 0; p < member.code.length; p = LegacyClassFile.next(member.code, p)) {
                    int opcode = member.code[p] & 255;
                    if (opcode != 0x12 && opcode != 0x13) continue;
                    int constant =
                            opcode == 0x12
                                    ? member.code[p + 1] & 255
                                    : LegacyClassFile.u2(member.code, p + 1);
                    if (owner.tags[constant] == 8) strings.add(owner.text[owner.first[constant]]);
                }
                if (strings.containsAll(Arrays.asList("air", "stone", "grass", "planks")))
                    registrations.add(owner.name + "#" + member.name);
            }
        if (registrations.isEmpty()) return null;
        String registration = only(registrations, "BLOCK_REGISTRATION");
        List<Method> bootstraps = new ArrayList<>();
        for (LegacyClassFile owner : classes.values())
            for (LegacyClassFile.Member member : owner.methods) {
                if ((member.access & 9) != 9
                        || !member.descriptor.equals("()V")
                        || member.code == null) continue;
                for (int p = 0; p < member.code.length; p = LegacyClassFile.next(member.code, p))
                    if ((member.code[p] & 255) == 0xb8) {
                        int reference = LegacyClassFile.u2(member.code, p + 1);
                        if ((owner.referenceOwner(reference) + "#" + owner.referenceName(reference))
                                .equals(registration)) {
                            bootstraps.add(load(owner.name).getMethod(member.name));
                            break;
                        }
                    }
            }
        return only(bootstraps, "BOOTSTRAP_REGISTRATION_CALL");
    }

    /**
     * Executes the selected original registration path, including older native static initializers.
     */
    void initialize() throws Exception {
        if (bootstrap != null) bootstrap.invoke(null);
        else
            Class.forName(
                    staticBlockType.getName(), true, NativeSeedSymbols.class.getClassLoader());
    }

    Object factory(Class<?> result) throws Exception {
        List<Method> factories = new ArrayList<>();
        String desc = "()L" + result.getName().replace('.', '/') + ";";
        for (LegacyClassFile type : classes.values())
            for (LegacyClassFile.Member member : type.methods)
                if ((member.access & 9) == 9 && member.descriptor.equals(desc))
                    factories.add(load(type.name).getMethod(member.name));
        return only(factories, "FACTORY_" + result.getName()).invoke(null);
    }

    Object named(Class<?> type, String name) throws Exception {
        List<Method> factories = new ArrayList<>();
        for (Method method : type.getMethods())
            if (Modifier.isStatic(method.getModifiers())
                    && !(type.isEnum() && method.getName().equals("valueOf"))
                    && method.getReturnType() == type
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {String.class}))
                factories.add(method);
        return only(factories, "NAMED_" + type.getName()).invoke(null, name);
    }

    LegacyClassFile unique(String role, java.util.function.Predicate<LegacyClassFile> predicate)
            throws IOException {
        List<LegacyClassFile> result = new ArrayList<>();
        for (LegacyClassFile type : classes.values()) if (predicate.test(type)) result.add(type);
        return only(result, role);
    }

    static boolean contains(LegacyClassFile type, String... values) {
        List<String> pool = Arrays.asList(type.text);
        for (String value : values) if (!pool.contains(value)) return false;
        return true;
    }

    Class<?> load(String name) throws ClassNotFoundException {
        return Class.forName(
                name.replace('/', '.'), false, NativeSeedSymbols.class.getClassLoader());
    }

    Field serializedField(Class<?> type, String key, String descriptor) throws Exception {
        LegacyClassFile shape = classes.get(type.getName().replace('.', '/'));
        Set<String> names = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : shape.methods)
            if (method.code != null) {
                boolean found = false;
                int budget = 0;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int op = method.code[p] & 255;
                    if (op == 0x12 || op == 0x13) {
                        int constant =
                                op == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        found =
                                shape.tags[constant] == 8
                                        && key.equals(shape.text[shape.first[constant]]);
                        budget = 0;
                    }
                    if (found && ++budget > 8) found = false;
                    if (found && op == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (shape.name.equals(shape.referenceOwner(ref))
                                && descriptor.equals(shape.referenceType(ref)))
                            names.add(shape.referenceName(ref));
                        found = false;
                    }
                }
            }
        Field field = type.getDeclaredField(only(new ArrayList<>(names), "NBT_FIELD_" + key));
        field.setAccessible(true);
        return field;
    }

    static <T> T only(List<T> values, String role) throws IOException {
        if (values.size() != 1)
            throw new IOException(
                    "SEED_" + role + "_AMBIGUOUS: " + values.size() + " native candidates.");
        return values.get(0);
    }

    static List<Field> fields(Class<?> type) {
        List<Field> fields = new ArrayList<>();
        for (Class<?> current = type; current != null; current = current.getSuperclass())
            for (Field field : current.getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers())) {
                    field.setAccessible(true);
                    fields.add(field);
                }
        return fields;
    }

    @Override
    public void close() throws IOException {
        jar.close();
    }
}
