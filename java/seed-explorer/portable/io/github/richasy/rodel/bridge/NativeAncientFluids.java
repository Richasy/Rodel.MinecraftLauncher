package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;

/** Identifies original fluids through material guards around native water sounds/lava particles. */
final class NativeAncientFluids {
    private final Object blocks, water, lava;
    private final Field material;

    NativeAncientFluids(Map<String, LegacyClassFile> classes, Class<?> blockType) throws Exception {
        Set<Class<?>> materials = new LinkedHashSet<>();
        for (Constructor<?> constructor : blockType.getDeclaredConstructors()) {
            Class<?>[] args = constructor.getParameterTypes();
            if (args.length == 2 && args[0] == int.class && !args[1].isPrimitive())
                materials.add(args[1]);
        }
        Class<?> materialType =
                NativeSeedSymbols.only(new ArrayList<>(materials), "ANCIENT_BLOCK_MATERIAL_TYPE");
        List<Field> fields = new ArrayList<>();
        for (Field field : blockType.getDeclaredFields())
            if (!Modifier.isStatic(field.getModifiers()) && field.getType() == materialType) {
                field.setAccessible(true);
                fields.add(field);
            }
        material = NativeSeedSymbols.only(fields, "ANCIENT_BLOCK_MATERIAL_FIELD");
        List<Object> arrays = new ArrayList<>();
        for (Field field : blockType.getDeclaredFields())
            if (Modifier.isStatic(field.getModifiers())
                    && field.getType() == Array.newInstance(blockType, 0).getClass()) {
                field.setAccessible(true);
                if (field.get(null) != null) arrays.add(field.get(null));
            }
        blocks = NativeSeedSymbols.only(arrays, "ANCIENT_BLOCK_ARRAY");
        String materialDescriptor = "L" + materialType.getName().replace('.', '/') + ";";
        Set<Object> waters = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>()),
                lavas = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        for (LegacyClassFile owner : classes.values()) {
            if (!extendsBlock(classes, owner, blockType.getName().replace('.', '/'))) continue;
            for (LegacyClassFile.Member method : owner.methods)
                if (method.code != null) {
                    byte[] code = method.code;
                    for (int p = 0; p < code.length; p = LegacyClassFile.next(code, p)) {
                        if ((code[p] & 255) != 0xb4) continue;
                        int instance = LegacyClassFile.u2(code, p + 1);
                        if (!material.getName().equals(owner.referenceName(instance))
                                || !materialDescriptor.equals(owner.referenceType(instance)))
                            continue;
                        int constant = LegacyClassFile.next(code, p),
                                guard = LegacyClassFile.next(code, constant);
                        if ((code[constant] & 255) != 0xb2 || (code[guard] & 255) != 0xa6) continue;
                        int ref = LegacyClassFile.u2(code, constant + 1);
                        if (!materialDescriptor.equals(owner.referenceType(ref))) continue;
                        int end = guard + (short) LegacyClassFile.u2(code, guard + 1);
                        if (end <= guard || end > code.length) continue;
                        boolean waterBranch = false, lavaBranch = false;
                        for (int q = LegacyClassFile.next(code, guard);
                                q < end;
                                q = LegacyClassFile.next(code, q)) {
                            int opcode = code[q] & 255;
                            if (opcode != 0x12 && opcode != 0x13) continue;
                            int id =
                                    opcode == 0x12
                                            ? code[q + 1] & 255
                                            : LegacyClassFile.u2(code, q + 1);
                            if (owner.tags[id] != 8) continue;
                            String text = owner.text[owner.first[id]];
                            waterBranch |= "liquid.water".equals(text);
                            lavaBranch |= "lava".equals(text) || "liquid.lavapop".equals(text);
                        }
                        if (!waterBranch && !lavaBranch) continue;
                        Field value =
                                NativeAncientSymbols.load(owner.referenceOwner(ref))
                                        .getDeclaredField(owner.referenceName(ref));
                        value.setAccessible(true);
                        Object nativeMaterial = value.get(null);
                        if (waterBranch) waters.add(nativeMaterial);
                        if (lavaBranch) lavas.add(nativeMaterial);
                    }
                }
        }
        water = NativeSeedSymbols.only(new ArrayList<>(waters), "ANCIENT_WATER_MATERIAL");
        lava = NativeSeedSymbols.only(new ArrayList<>(lavas), "ANCIENT_LAVA_MATERIAL");
        if (water == lava) throw new IOException("SEED_ANCIENT_FLUID_AMBIGUOUS");
    }

    private static boolean extendsBlock(
            Map<String, LegacyClassFile> classes, LegacyClassFile owner, String block) {
        for (LegacyClassFile current = owner;
                current != null;
                current = classes.get(current.parent)) if (current.name.equals(block)) return true;
        return false;
    }

    int kind(int id) throws Exception {
        if (id == 0) return 0;
        if (id < 0 || id >= Array.getLength(blocks))
            throw new IOException("SEED_ANCIENT_BLOCK_UNREGISTERED");
        Object block = Array.get(blocks, id);
        if (block == null) throw new IOException("SEED_ANCIENT_BLOCK_UNREGISTERED");
        Object value = material.get(block);
        return value == water ? 1 : value == lava ? 2 : 0;
    }
}
