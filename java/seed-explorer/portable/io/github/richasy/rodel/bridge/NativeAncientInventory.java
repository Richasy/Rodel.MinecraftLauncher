package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;

/** Reads logical inventory capacity independently of oversized original backing arrays. */
final class NativeAncientInventory {
    private NativeAncientInventory() {}

    /** Uses the original NBT loader's size query that allocates this exact ItemStack array. */
    static int capacity(
            Map<String, LegacyClassFile> classes, Object container, Field inventory, int fallback)
            throws Exception {
        String fieldOwner = inventory.getDeclaringClass().getName().replace('.', '/');
        String component = inventory.getType().getComponentType().getName().replace('.', '/');
        String descriptor = "[L" + component + ";";
        Set<Method> sizes = new LinkedHashSet<>();
        for (Class<?> owner = container.getClass(); owner != null; owner = owner.getSuperclass()) {
            LegacyClassFile shape = classes.get(owner.getName().replace('.', '/'));
            if (shape == null || !NativeSeedSymbols.contains(shape, "Items")) continue;
            for (LegacyClassFile.Member method : shape.methods) {
                if (method.code == null || method.name.equals("<init>")) continue;
                byte[] code = method.code;
                for (int p = 0; p < code.length; p = LegacyClassFile.next(code, p)) {
                    if ((code[p] & 255) != 0xb6) continue;
                    int call = LegacyClassFile.u2(code, p + 1);
                    if (!shape.referenceType(call).equals("()I")) continue;
                    int allocation = LegacyClassFile.next(code, p);
                    if (allocation >= code.length
                            || (code[allocation] & 255) != 0xbd
                            || !component.equals(
                                    shape.text[
                                            shape.first[LegacyClassFile.u2(code, allocation + 1)]]))
                        continue;
                    int write = LegacyClassFile.next(code, allocation);
                    if (write >= code.length || (code[write] & 255) != 0xb5) continue;
                    int field = LegacyClassFile.u2(code, write + 1);
                    if (!fieldOwner.equals(shape.referenceOwner(field))
                            || !inventory.getName().equals(shape.referenceName(field))
                            || !descriptor.equals(shape.referenceType(field))) continue;
                    Method query =
                            NativeAncientSymbols.load(shape.referenceOwner(call))
                                    .getMethod(shape.referenceName(call));
                    sizes.add(query);
                }
            }
        }
        if (sizes.isEmpty()) return fallback;
        int capacity =
                ((Number)
                                NativeSeedSymbols.only(
                                                new ArrayList<>(sizes),
                                                "ANCIENT_INVENTORY_SIZE_QUERY")
                                        .invoke(container))
                        .intValue();
        if (capacity < 0 || capacity > 65536)
            throw new IOException("SEED_ANCIENT_INVENTORY_SIZE_INVALID");
        return capacity;
    }
}
