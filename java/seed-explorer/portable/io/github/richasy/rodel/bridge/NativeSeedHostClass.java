package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;

/**
 * Emits constructor forwarding and abstract host defaults for the installed server ABI, without
 * rewriting original game code.
 */
final class NativeSeedHostClass extends ClassLoader {
    private final List<byte[]> pool = new ArrayList<>();
    private final Map<String, Integer> ids = new HashMap<>();

    private NativeSeedHostClass(ClassLoader parent) {
        super(parent);
    }

    private int utf(String s) throws Exception {
        String key = "u" + s;
        if (ids.containsKey(key)) return ids.get(key);
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        DataOutputStream d = new DataOutputStream(b);
        d.writeByte(1);
        d.writeUTF(s);
        return put(key, b.toByteArray());
    }

    private int put(String k, byte[] b) {
        pool.add(b);
        int id = pool.size();
        ids.put(k, id);
        return id;
    }

    private int ref(int tag, String s, int a, int b) throws Exception {
        String key = tag + s;
        if (ids.containsKey(key)) return ids.get(key);
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        DataOutputStream d = new DataOutputStream(bytes);
        d.writeByte(tag);
        d.writeShort(a);
        if (b != 0) d.writeShort(b);
        return put(key, bytes.toByteArray());
    }

    private int type(String s) throws Exception {
        return ref(7, s, utf(s), 0);
    }

    private int method(String owner, String name, String desc) throws Exception {
        return ref(
                10, owner + name + desc, type(owner), ref(12, name + desc, utf(name), utf(desc)));
    }

    private static String descriptor(Class<?> c) {
        if (c.isArray()) return c.getName().replace('.', '/');
        if (!c.isPrimitive()) return "L" + c.getName().replace('.', '/') + ";";
        return c == void.class
                ? "V"
                : c == boolean.class
                        ? "Z"
                        : c == int.class
                                ? "I"
                                : c == long.class
                                        ? "J"
                                        : c == double.class
                                                ? "D"
                                                : c == float.class
                                                        ? "F"
                                                        : c == short.class
                                                                ? "S"
                                                                : c == byte.class ? "B" : "C";
    }

    private static String descriptor(Class<?>[] args, Class<?> result) {
        StringBuilder b = new StringBuilder("(");
        for (Class<?> c : args) b.append(descriptor(c));
        return b.append(')').append(descriptor(result)).toString();
    }

    private byte[] member(int access, String name, String desc, byte[] code, int stack, int locals)
            throws Exception {
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        DataOutputStream d = new DataOutputStream(b);
        d.writeShort(access);
        d.writeShort(utf(name));
        d.writeShort(utf(desc));
        d.writeShort(1);
        d.writeShort(utf("Code"));
        d.writeInt(12 + code.length);
        d.writeShort(stack);
        d.writeShort(locals);
        d.writeInt(code.length);
        d.write(code);
        d.writeShort(0);
        d.writeShort(0);
        return b.toByteArray();
    }

    static Class<?> create(Constructor<?> base) throws Exception {
        return create(base, Collections.emptySet());
    }

    /**
     * Enables only explicitly descriptor-bound, zero-argument abstract boolean generation
     * capabilities.
     */
    static Class<?> create(Constructor<?> base, Set<Method> enabledBooleans) throws Exception {
        for (Method method : enabledBooleans)
            if (!Modifier.isAbstract(method.getModifiers())
                    || method.getReturnType() != boolean.class
                    || method.getParameterTypes().length != 0
                    || !method.getDeclaringClass().isAssignableFrom(base.getDeclaringClass()))
                throw new IllegalArgumentException("Invalid native host boolean capability.");
        return new NativeSeedHostClass(base.getDeclaringClass().getClassLoader())
                .build(base, enabledBooleans);
    }

    private Class<?> build(Constructor<?> ctor, Set<Method> enabledBooleans) throws Exception {
        String name = "io/github/richasy/rodel/bridge/GeneratedSeedHost",
                parent = ctor.getDeclaringClass().getName().replace('.', '/');
        int self = type(name), superId = type(parent);
        List<byte[]> members = new ArrayList<>();
        String desc = descriptor(ctor.getParameterTypes(), void.class);
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        DataOutputStream d = new DataOutputStream(b);
        d.writeByte(0x2a);
        int slot = 1;
        for (Class<?> arg : ctor.getParameterTypes()) {
            d.writeByte(
                    !arg.isPrimitive()
                            ? 0x19
                            : arg == long.class
                                    ? 0x16
                                    : arg == double.class
                                            ? 0x18
                                            : arg == float.class ? 0x17 : 0x15);
            d.writeByte(slot);
            slot += (arg == long.class || arg == double.class) ? 2 : 1;
        }
        d.writeByte(0xb7);
        d.writeShort(method(parent, "<init>", desc));
        d.writeByte(0xb1);
        members.add(member(1, "<init>", desc, b.toByteArray(), slot, slot));
        Map<String, Method> actual = new LinkedHashMap<>();
        for (Class<?> c = ctor.getDeclaringClass(); c != null; c = c.getSuperclass())
            for (Method m : c.getDeclaredMethods()) {
                String key = m.getName() + descriptor(m.getParameterTypes(), m.getReturnType());
                if (!actual.containsKey(key)) actual.put(key, m);
            }
        for (Method m : ctor.getDeclaringClass().getMethods()) {
            String key = m.getName() + descriptor(m.getParameterTypes(), m.getReturnType());
            if (!actual.containsKey(key)) actual.put(key, m);
        }
        for (Method m : actual.values())
            if (Modifier.isAbstract(m.getModifiers())) {
                b = new ByteArrayOutputStream();
                d = new DataOutputStream(b);
                Class<?> r = m.getReturnType();
                if (r == void.class) d.writeByte(0xb1);
                else if (r.isPrimitive()) {
                    d.writeByte(
                            r == long.class
                                    ? 0x09
                                    : r == double.class
                                            ? 0x0e
                                            : r == float.class
                                                    ? 0x0b
                                                    : enabledBooleans.contains(m) ? 0x04 : 0x03);
                    d.writeByte(
                            r == long.class
                                    ? 0xad
                                    : r == double.class ? 0xaf : r == float.class ? 0xae : 0xac);
                } else {
                    d.writeByte(0x13);
                    d.writeShort(type(r.getName().replace('.', '/')));
                    d.writeByte(0xb8);
                    d.writeShort(
                            method(
                                    "io/github/richasy/rodel/bridge/OriginalSeedWorker",
                                    "defaultValue",
                                    "(Ljava/lang/Class;)Ljava/lang/Object;"));
                    d.writeByte(0xc0);
                    d.writeShort(type(r.getName().replace('.', '/')));
                    d.writeByte(0xb0);
                }
                int local = 1;
                for (Class<?> p : m.getParameterTypes())
                    local += (p == long.class || p == double.class) ? 2 : 1;
                members.add(
                        member(
                                1,
                                m.getName(),
                                descriptor(m.getParameterTypes(), r),
                                b.toByteArray(),
                                2,
                                local));
            }
        b = new ByteArrayOutputStream();
        d = new DataOutputStream(b);
        d.writeInt(0xcafebabe);
        d.writeShort(0);
        d.writeShort(49);
        d.writeShort(pool.size() + 1);
        for (byte[] p : pool) d.write(p);
        d.writeShort(0x21);
        d.writeShort(self);
        d.writeShort(superId);
        d.writeShort(0);
        d.writeShort(0);
        d.writeShort(members.size());
        for (byte[] m : members) d.write(m);
        d.writeShort(0);
        byte[] code = b.toByteArray();
        return defineClass(name.replace('/', '.'), code, 0, code.length);
    }
}
