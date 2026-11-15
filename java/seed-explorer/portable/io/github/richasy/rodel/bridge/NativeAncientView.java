package io.github.richasy.rodel.bridge;

import java.lang.reflect.*;
import java.util.*;

/** Supplies the native minimum view radius required by an empty disposable player-list host. */
final class NativeAncientView {
    static void configure(NativeSeedSymbols symbols, Object players) throws Exception {
        List<LegacyClassFile> managers = new ArrayList<>();
        for (LegacyClassFile type : symbols.classes.values())
            if (NativeSeedSymbols.contains(type, "Too small view radius!", "Too big view radius!"))
                managers.add(type);
        if (managers.isEmpty()) return;
        LegacyClassFile manager = NativeSeedSymbols.only(managers, "PLAYER_VIEW_GUARD");
        Set<Integer> minimums = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : manager.methods)
            if (method.name.equals("<init>") && method.code != null) {
                List<Integer> positions = new ArrayList<>();
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode == 0x12 || opcode == 0x13) {
                        int constant =
                                opcode == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        if (manager.tags[constant] == 8
                                && "Too small view radius!"
                                        .equals(manager.text[manager.first[constant]])) {
                            for (int i = positions.size() - 1;
                                    i >= Math.max(1, positions.size() - 6);
                                    i--) {
                                int previous = method.code[positions.get(i)] & 255;
                                if (previous != 0xa2 && previous != 0xa1) continue;
                                int value = integer(manager, method.code, positions.get(i - 1));
                                if (value > 0 && value < 1024) minimums.add(value);
                                break;
                            }
                        }
                    }
                    positions.add(p);
                }
            }
        if (minimums.isEmpty()) return;
        int minimum = NativeSeedSymbols.only(new ArrayList<>(minimums), "PLAYER_VIEW_MINIMUM");
        LegacyClassFile world = symbols.classes.get(symbols.worldType.getName().replace('.', '/'));
        Set<String> getters = new LinkedHashSet<>();
        for (LegacyClassFile.Member method : world.methods)
            if (method.name.equals("<init>") && method.code != null)
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p))
                    if ((method.code[p] & 255) == 0xb6) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if (!world.referenceType(ref).equals("()I")) continue;
                        Class<?> owner = symbols.load(world.referenceOwner(ref));
                        if (owner.isInstance(players))
                            getters.add(owner.getName() + "#" + world.referenceName(ref));
                    }
        if (getters.isEmpty()) return;
        String[] reference =
                NativeSeedSymbols.only(new ArrayList<>(getters), "PLAYER_VIEW_QUERY").split("#", 2);
        Class<?> owner =
                Class.forName(reference[0], false, NativeAncientView.class.getClassLoader());
        Method query = owner.getMethod(reference[1]);
        if (((Number) query.invoke(players)).intValue() >= minimum) return;
        LegacyClassFile shape = symbols.classes.get(owner.getName().replace('.', '/'));
        List<Field> fields = new ArrayList<>();
        for (LegacyClassFile.Member method : shape.methods)
            if (method.name.equals(reference[1])
                    && method.descriptor.equals("()I")
                    && method.code != null
                    && method.code.length == 5
                    && method.code[0] == 0x2a
                    && (method.code[1] & 255) == 0xb4
                    && (method.code[4] & 255) == 0xac) {
                int ref = LegacyClassFile.u2(method.code, 2);
                Field field =
                        symbols.load(shape.referenceOwner(ref))
                                .getDeclaredField(shape.referenceName(ref));
                field.setAccessible(true);
                fields.add(field);
            }
        NativeSeedSymbols.only(fields, "PLAYER_VIEW_FIELD").setInt(players, minimum);
    }

    private static int integer(LegacyClassFile owner, byte[] code, int position) {
        int opcode = code[position] & 255;
        if (opcode >= 0x02 && opcode <= 0x08) return opcode - 3;
        if (opcode == 0x10) return code[position + 1];
        if (opcode == 0x11) return (short) LegacyClassFile.u2(code, position + 1);
        if (opcode == 0x12 || opcode == 0x13) {
            int constant =
                    opcode == 0x12
                            ? code[position + 1] & 255
                            : LegacyClassFile.u2(code, position + 1);
            if (owner.tags[constant] == 3) return owner.first[constant];
        }
        return -1;
    }
}
