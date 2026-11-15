package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;

/** Selects original customized options through the game's own parser and biome registry. */
final class NativeAncientPresets {
    private NativeAncientPresets() {}

    /** Finds native fixedBiome input by executing its parser, including original ID remapping. */
    static String singleBiome(
            NativeSeedSymbols symbols, Map<Object, String> biomes, String requested)
            throws Exception {
        Object target = null;
        for (Map.Entry<Object, String> biome : biomes.entrySet())
            if (normal(biome.getValue()).equals(normal(requested))) target = biome.getKey();
        if (target == null)
            throw unsupported(
                    "The selected biome is not registered by the installed original game.");
        Set<Field> fields = new LinkedHashSet<>();
        for (LegacyClassFile shape : symbols.classes.values()) {
            if (!NativeSeedSymbols.contains(shape, "fixedBiome")) continue;
            for (LegacyClassFile.Member method : shape.methods) {
                if (method.code == null) continue;
                int budget = 0;
                for (int p = 0; p < method.code.length; p = LegacyClassFile.next(method.code, p)) {
                    int opcode = method.code[p] & 255;
                    if (opcode == 0x12 || opcode == 0x13) {
                        int index =
                                opcode == 0x12
                                        ? method.code[p + 1] & 255
                                        : LegacyClassFile.u2(method.code, p + 1);
                        budget =
                                shape.tags[index] == 8
                                                && "fixedBiome"
                                                        .equals(shape.text[shape.first[index]])
                                        ? 8
                                        : 0;
                    } else if (budget > 0 && --budget > 0 && opcode == 0xb5) {
                        int ref = LegacyClassFile.u2(method.code, p + 1);
                        if ("I".equals(shape.referenceType(ref))) {
                            Field field =
                                    symbols.load(shape.referenceOwner(ref))
                                            .getDeclaredField(shape.referenceName(ref));
                            field.setAccessible(true);
                            fields.add(field);
                        }
                    }
                }
            }
        }
        if (fields.size() != 1)
            throw unsupported("This original generator has no fixed-biome options parser.");
        Field fixed = fields.iterator().next();
        List<Method> parsers = new ArrayList<>();
        for (Method method : fixed.getDeclaringClass().getMethods())
            if (Modifier.isStatic(method.getModifiers())
                    && method.getReturnType() == fixed.getDeclaringClass()
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {String.class}))
                parsers.add(method);
        if (parsers.size() != 1)
            throw unsupported("This original generator does not expose its native options parser.");
        Method parse = parsers.get(0);
        int desired = numericId(symbols, target), maximum = 0;
        for (Object biome : biomes.keySet()) maximum = Math.max(maximum, numericId(symbols, biome));
        if (maximum > 65535 || desired < 0)
            throw unsupported("The native biome registry cannot supply a fixed-biome ID.");
        for (int input = 0; input <= maximum; input++) {
            String json = "{\"fixedBiome\":" + input + "}";
            Object parsed = parse.invoke(null, json);
            if (parsed != null && fixed.getInt(parsed) == desired) return json;
        }
        throw unsupported("The original fixed-biome parser does not accept the selected biome.");
    }

    /** Calls the actual WorldSettings(String) fluent setter before the world is constructed. */
    static void apply(Object settings, String options) throws Exception {
        List<Method> setters = new ArrayList<>();
        for (Method method : settings.getClass().getMethods())
            if (!Modifier.isStatic(method.getModifiers())
                    && method.getReturnType() == settings.getClass()
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {String.class}))
                setters.add(method);
        if (setters.size() != 1)
            throw unsupported("This original WorldSettings API cannot receive generator options.");
        setters.get(0).invoke(settings, options);
    }

    private static int numericId(NativeSeedSymbols symbols, Object biome) throws Exception {
        List<Method> queries = new ArrayList<>();
        for (Method method : symbols.biomeType.getMethods())
            if (Modifier.isStatic(method.getModifiers())
                    && method.getReturnType() == int.class
                    && Arrays.equals(
                            method.getParameterTypes(), new Class<?>[] {symbols.biomeType}))
                queries.add(method);
        if (queries.size() == 1) return ((Number) queries.get(0).invoke(null, biome)).intValue();
        Set<Integer> ids = new LinkedHashSet<>();
        for (Field field : symbols.biomeType.getDeclaredFields())
            if (Modifier.isStatic(field.getModifiers())
                    && field.getType() == Array.newInstance(symbols.biomeType, 0).getClass()) {
                field.setAccessible(true);
                Object values = field.get(null);
                if (values == null || Array.getLength(values) > 65536) continue;
                for (int i = 0; i < Array.getLength(values); i++)
                    if (Array.get(values, i) == biome) ids.add(i);
            }
        if (ids.size() != 1)
            throw unsupported("The original biome has no unique native numeric registry ID.");
        return ids.iterator().next();
    }

    private static String normal(String id) {
        return id == null ? "" : id.replaceFirst("^minecraft:", "");
    }

    static IOException unsupported(String reason) {
        return new IOException("SEED_WORLD_UNSUPPORTED: " + reason);
    }
}
