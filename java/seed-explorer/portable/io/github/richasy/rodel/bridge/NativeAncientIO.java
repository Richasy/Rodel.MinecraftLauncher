package io.github.richasy.rodel.bridge;

import java.lang.reflect.*;
import java.util.*;

/** Drains the original asynchronous chunk writer before its region files are frozen. */
final class NativeAncientIO {
    private NativeAncientIO() {}

    /** Calls the native queue completion barrier; early synchronous formats need no barrier. */
    static void await(Map<String, LegacyClassFile> classes) throws Exception {
        List<LegacyClassFile> writers = new ArrayList<>();
        for (LegacyClassFile type : classes.values())
            if (NativeSeedSymbols.contains(type, "File IO Thread")) writers.add(type);
        if (writers.isEmpty()) return;
        LegacyClassFile writer = NativeSeedSymbols.only(writers, "ANCIENT_FILE_WRITER");
        List<String> barriers = new ArrayList<>();
        for (LegacyClassFile.Member method : writer.methods) {
            if ((method.access & 9) != 1 || !method.descriptor.equals("()V") || method.code == null)
                continue;
            Set<String> counters = new LinkedHashSet<>();
            boolean compare = false, sleep = false;
            for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                int opcode = method.code[p] & 255;
                compare |= opcode == 0x94;
                if (opcode == 0xb4) {
                    int ref = LegacyClassFile.u2(method.code, p + 1);
                    if (!writer.name.equals(writer.referenceOwner(ref))
                            || !"J".equals(writer.referenceType(ref))) continue;
                    String name = writer.referenceName(ref);
                    for (LegacyClassFile.Member field : writer.fields)
                        if (field.name.equals(name)
                                && field.descriptor.equals("J")
                                && (field.access & 64) != 0) counters.add(name);
                } else if (opcode == 0xb8) {
                    int ref = LegacyClassFile.u2(method.code, p + 1);
                    sleep |=
                            "java/lang/Thread".equals(writer.referenceOwner(ref))
                                    && "sleep".equals(writer.referenceName(ref))
                                    && "(J)V".equals(writer.referenceType(ref));
                }
            }
            if (counters.size() == 2 && compare && sleep) barriers.add(method.name);
        }
        Class<?> type = NativeAncientSymbols.load(writer.name);
        List<Field> singletons = new ArrayList<>();
        for (Field field : type.getDeclaredFields())
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == type) {
                field.setAccessible(true);
                singletons.add(field);
            }
        Object singleton =
                NativeSeedSymbols.only(singletons, "ANCIENT_FILE_WRITER_INSTANCE").get(null);
        type.getMethod(NativeSeedSymbols.only(barriers, "ANCIENT_FILE_WRITE_BARRIER"))
                .invoke(singleton);
    }
}
