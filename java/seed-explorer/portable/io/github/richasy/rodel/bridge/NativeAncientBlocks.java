package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;

/** Reads native pre-state blocks and the metadata nibble proven by original NBT serialization. */
final class NativeAncientBlocks {
    private final Method metadata;
    private final Map<Object, String> names;
    private final Object numericBlocks;
    private final Class<?> blockType;
    private final Map<Object, State[]> states = new IdentityHashMap<>();

    NativeAncientBlocks(
            NativeSeedSymbols symbols, Class<?> chunk, Class<?> block, Map<Object, String> names)
            throws Exception {
        this.names = names;
        blockType = block;
        List<Object> arrays = new ArrayList<>();
        for (Field field : block.getDeclaredFields())
            if (Modifier.isStatic(field.getModifiers())
                    && field.getType() == Array.newInstance(block, 0).getClass()) {
                field.setAccessible(true);
                if (field.get(null) != null) arrays.add(field.get(null));
            }
        numericBlocks =
                arrays.isEmpty() ? null : NativeSeedSymbols.only(arrays, "NUMERIC_BLOCK_REGISTRY");
        metadata = sectionQuery(symbols.classes, chunk, "Data");
    }

    static Method blockQuery(NativeSeedSymbols symbols, Class<?> chunk) throws Exception {
        return blockQuery(symbols.classes, chunk);
    }

    static Method blockQuery(Map<String, LegacyClassFile> classes, Class<?> chunk)
            throws Exception {
        return sectionQuery(classes, chunk, "Blocks");
    }

    static Method metadataQuery(Map<String, LegacyClassFile> classes, Class<?> chunk)
            throws Exception {
        return sectionQuery(classes, chunk, "Data");
    }

    private static Method sectionQuery(
            Map<String, LegacyClassFile> classes, Class<?> chunk, String key) throws Exception {
        Set<String> nibbleGetters = new LinkedHashSet<>();
        for (LegacyClassFile owner : classes.values()) {
            if (!NativeSeedSymbols.contains(owner, "Sections", "Blocks", "Data")) continue;
            for (LegacyClassFile.Member method : owner.methods) {
                if (method.code == null) continue;
                int budget = 0;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode == 0x12 || opcode == 0x13) {
                        int constant =
                                opcode == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        budget =
                                owner.tags[constant] == 8
                                                && key.equals(owner.text[owner.first[constant]])
                                        ? 8
                                        : 0;
                    } else if (budget > 0 && --budget > 0 && opcode == 0xb6) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        String desc = owner.referenceType(ref);
                        LegacyClassFile section = classes.get(owner.referenceOwner(ref));
                        if (section == null || !desc.startsWith("()")) continue;
                        boolean containsSection = false;
                        for (Field storage : chunk.getDeclaredFields())
                            if (storage.getType().isArray()
                                    && storage.getType()
                                            .getComponentType()
                                            .getName()
                                            .replace('.', '/')
                                            .equals(section.name)) containsSection = true;
                        if (containsSection) {
                            nibbleGetters.add(
                                    section.name + "#" + owner.referenceName(ref) + "#" + desc);
                            budget = 0;
                        }
                    }
                }
            }
        }
        String getter =
                NativeSeedSymbols.only(new ArrayList<>(nibbleGetters), "METADATA_ARRAY_SERIALIZER");
        String[] parts = getter.split("#", 3);
        LegacyClassFile section = classes.get(parts[0]);
        Set<String> fields = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : section.methods)
            if (method.name.equals(parts[1])
                    && method.descriptor.equals(parts[2])
                    && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (section.name.equals(section.referenceOwner(ref)))
                            fields.add(section.referenceName(ref));
                    }
        String field = NativeSeedSymbols.only(new ArrayList<>(fields), "METADATA_NIBBLE_FIELD");
        Set<String> readers = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : section.methods)
            if (method.descriptor.equals("(III)I") && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb4) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (section.name.equals(section.referenceOwner(ref))
                                && field.equals(section.referenceName(ref)))
                            readers.add(method.name);
                    }
        String reader = NativeSeedSymbols.only(new ArrayList<>(readers), "METADATA_SECTION_READER");
        LegacyClassFile chunkShape = classes.get(chunk.getName().replace('.', '/'));
        List<Method> chunkReaders = new ArrayList<>();
        for (LegacyClassFile.Member method : chunkShape.methods)
            if (method.descriptor.equals("(III)I") && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb6) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (section.name.equals(chunkShape.referenceOwner(ref))
                                && reader.equals(chunkShape.referenceName(ref))) {
                            chunkReaders.add(
                                    chunk.getMethod(method.name, int.class, int.class, int.class));
                            break;
                        }
                    }
        return NativeSeedSymbols.only(chunkReaders, "METADATA_CHUNK_READER_" + key);
    }

    State state(Object block, Object chunk, int x, int y, int z) throws Exception {
        boolean air = false;
        if (block instanceof Number) {
            int id = ((Number) block).intValue();
            if (numericBlocks == null || id < 0 || id >= Array.getLength(numericBlocks))
                throw new IOException("SEED_BLOCK_UNREGISTERED");
            air = id == 0;
            block = Array.get(numericBlocks, id);
        }
        String id = air ? "minecraft:air" : names.get(block);
        if (id == null) throw new IOException("SEED_BLOCK_UNREGISTERED");
        if (id.indexOf(':') < 0) id = "minecraft:" + id;
        int data = ((Number) metadata.invoke(chunk, x, y, z)).intValue();
        if (data < 0 || data > 15) throw new IOException("SEED_METADATA_INVALID");
        State[] entries = states.get(block);
        if (entries == null) {
            entries = new State[16];
            states.put(block, entries);
        }
        if (entries[data] != null) return entries[data];
        int fluid = 0;
        if (block != null)
            for (Method method : blockType.getMethods())
                if (method.getReturnType() == String.class
                        && method.getParameterTypes().length == 0) {
                    String name = (String) method.invoke(block);
                    if ("tile.water".equals(name)) fluid = 1;
                    if ("tile.lava".equals(name)) fluid = 2;
                }
        if (entries[data] == null) entries[data] = new State(id, data, fluid);
        return entries[data];
    }

    static final class State {
        final String id;
        final int data;
        final int fluid;

        State(String id, int data, int fluid) {
            this.id = id;
            this.data = data;
            this.fluid = fluid;
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
