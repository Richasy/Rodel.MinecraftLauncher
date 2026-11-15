package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;

/** Reads the six native bounds fields in the order proved by the original int-array constructor. */
final class NativeAncientBounds {
    private final Field[] fields = new Field[6];

    NativeAncientBounds(NativeSeedSymbols symbols, Class<?> bounds) throws Exception {
        this(symbols.classes, bounds);
    }

    NativeAncientBounds(java.util.Map<String, LegacyClassFile> classes, Class<?> bounds)
            throws Exception {
        LegacyClassFile shape = classes.get(bounds.getName().replace('.', '/'));
        for (LegacyClassFile.Member method : shape.methods)
            if (method.name.equals("<init>")
                    && method.descriptor.equals("([I)V")
                    && method.code != null) {
                int constant = -1, loaded = -1;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode >= 0x03 && opcode <= 0x08) constant = opcode - 3;
                    else if (opcode == 0x2e) loaded = constant;
                    else if (opcode == 0xb5 && loaded >= 0 && loaded < fields.length) {
                        int reference = LegacyClassFile.u2(method.code, p + 1);
                        if (shape.name.equals(shape.referenceOwner(reference))
                                && "I".equals(shape.referenceType(reference))) {
                            Field field = bounds.getDeclaredField(shape.referenceName(reference));
                            field.setAccessible(true);
                            if (fields[loaded] != null && !fields[loaded].equals(field))
                                throw new IOException("SEED_BOUNDS_FIELD_AMBIGUOUS");
                            fields[loaded] = field;
                            loaded = -1;
                        }
                    }
                }
            }
        if (fields[0] == null)
            for (LegacyClassFile.Member method : shape.methods)
                if (method.name.equals("<init>")
                        && method.descriptor.equals("(IIIIII)V")
                        && method.code != null) {
                    int parameter = -1;
                    for (int p = 0;
                            p < method.code.length;
                            p = LegacyClassFile.next(method.code, p)) {
                        int opcode = method.code[p] & 255;
                        if (opcode >= 0x1b && opcode <= 0x1d) parameter = opcode - 0x1a;
                        else if (opcode == 0x15) parameter = method.code[p + 1] & 255;
                        else if (opcode == 0xb5 && parameter > 0 && parameter <= 6) {
                            int ref = LegacyClassFile.u2(method.code, p + 1);
                            if (shape.name.equals(shape.referenceOwner(ref))
                                    && "I".equals(shape.referenceType(ref))) {
                                Field field = bounds.getDeclaredField(shape.referenceName(ref));
                                field.setAccessible(true);
                                fields[parameter - 1] = field;
                                parameter = -1;
                            }
                        }
                    }
                }
        for (Field field : fields)
            if (field == null) throw new IOException("SEED_BOUNDS_ARRAY_CONSTRUCTOR_UNAVAILABLE");
    }

    int[] read(Object box) throws Exception {
        int[] result = new int[fields.length];
        for (int i = 0; i < result.length; i++) result[i] = fields[i].getInt(box);
        return result;
    }
}
