package io.github.richasy.rodel.bridge;

import java.io.*;
import java.util.*;

/**
 * Reads class-file facts and adds straight-line observation wrappers without loading game classes.
 */
final class LegacyClassFile {
    final byte[] bytes;
    final String[] text;
    final int[] first, second, tags;
    final String name, parent;
    final List<Member> fields = new ArrayList<Member>(), methods = new ArrayList<Member>();
    final int poolEnd, methodsOffset, classAttributes;
    private final int thisClass;

    static final class Member {
        final int access, nameIndex, descriptorIndex, start, end;
        final String name, descriptor;
        final byte[] code;

        Member(
                int access,
                int nameIndex,
                int descriptorIndex,
                int start,
                int end,
                String name,
                String descriptor,
                byte[] code) {
            this.access = access;
            this.nameIndex = nameIndex;
            this.descriptorIndex = descriptorIndex;
            this.start = start;
            this.end = end;
            this.name = name;
            this.descriptor = descriptor;
            this.code = code;
        }
    }

    LegacyClassFile(byte[] bytes) throws IOException {
        if (bytes.length > 4 * 1024 * 1024) throw new IOException("CLASS_BUDGET");
        this.bytes = bytes;
        DataInputStream in = new DataInputStream(new ByteArrayInputStream(bytes));
        if (in.readInt() != 0xcafebabe) throw new IOException("CLASS_MAGIC");
        in.readInt();
        int count = in.readUnsignedShort();
        text = new String[count];
        first = new int[count];
        second = new int[count];
        tags = new int[count];
        for (int i = 1; i < count; i++) {
            int tag = tags[i] = in.readUnsignedByte();
            switch (tag) {
                case 1:
                    text[i] = in.readUTF();
                    break;
                case 3:
                case 4:
                    in.readInt();
                    break;
                case 5:
                case 6:
                    in.readLong();
                    i++;
                    break;
                case 7:
                case 8:
                case 16:
                case 19:
                case 20:
                    first[i] = in.readUnsignedShort();
                    break;
                case 9:
                case 10:
                case 11:
                case 12:
                case 17:
                case 18:
                    first[i] = in.readUnsignedShort();
                    second[i] = in.readUnsignedShort();
                    break;
                case 15:
                    in.readUnsignedByte();
                    in.readUnsignedShort();
                    break;
                default:
                    throw new IOException("CLASS_POOL");
            }
        }
        poolEnd = bytes.length - in.available();
        in.readUnsignedShort();
        thisClass = in.readUnsignedShort();
        name = className(thisClass);
        parent = className(in.readUnsignedShort());
        skip(in, in.readUnsignedShort() * 2);
        readMembers(in, fields);
        methodsOffset = bytes.length - in.available();
        readMembers(in, methods);
        classAttributes = bytes.length - in.available();
    }

    private String className(int index) {
        return index == 0 ? null : text[first[index]];
    }

    String referenceName(int index) {
        return text[first[second[index]]];
    }

    String referenceType(int index) {
        return text[second[second[index]]];
    }

    String referenceOwner(int index) {
        return className(first[index]);
    }

    private void readMembers(DataInputStream in, List<Member> result) throws IOException {
        int count = in.readUnsignedShort();
        for (int i = 0; i < count; i++) {
            int start = bytes.length - in.available();
            int access = in.readUnsignedShort(),
                    n = in.readUnsignedShort(),
                    d = in.readUnsignedShort(),
                    attributes = in.readUnsignedShort();
            byte[] code = null;
            for (int a = 0; a < attributes; a++) {
                int key = in.readUnsignedShort(), length = in.readInt();
                if (length < 0 || length > in.available()) throw new IOException("CLASS_ATTRIBUTE");
                if ("Code".equals(text[key])) {
                    byte[] body = new byte[length];
                    in.readFully(body);
                    DataInputStream method = new DataInputStream(new ByteArrayInputStream(body));
                    method.readUnsignedShort();
                    method.readUnsignedShort();
                    int size = method.readInt();
                    if (size < 0 || size > method.available()) throw new IOException("CLASS_CODE");
                    code = new byte[size];
                    method.readFully(code);
                } else skip(in, length);
            }
            result.add(
                    new Member(
                            access,
                            n,
                            d,
                            start,
                            bytes.length - in.available(),
                            text[n],
                            text[d],
                            code));
        }
    }

    int clientScore() {
        int score = name.equals("net/minecraft/client/Minecraft") ? 200 : 0;
        boolean display = false, identity = false, terrain = false, runnable = false;
        for (String value : text)
            if (value != null) {
                if (value.startsWith("Setting user:") || value.startsWith("LWJGL Version:"))
                    identity = true;
                if (value.equals("org/lwjgl/opengl/Display")) display = true;
                if (value.equals("terrain.png")
                        || value.equals("/terrain.png")
                        || value.equals("textures/terrain.png")) terrain = true;
                if (value.equals("java/lang/Runnable")) runnable = true;
            }
        if (score == 0 && !display && !identity) return 0;
        score +=
                (identity ? 150 : 0)
                        + (display ? 30 : 0)
                        + (terrain ? 20 : 0)
                        + (runnable ? 10 : 0);
        if (methods.size() > 40) score += 10;
        int singletons = 0;
        for (Member f : fields)
            if ((f.access & 8) != 0 && f.descriptor.equals("L" + name + ";")) singletons++;
        if (singletons == 1) score += 40;
        if (runnable && display && nativeDisplayLoop("run", "()V", new HashSet<String>(), 0))
            score += 100;
        return score;
    }

    private boolean nativeDisplayLoop(
            String methodName, String descriptor, Set<String> visited, int depth) {
        if (depth > 4 || !visited.add(methodName + descriptor)) return false;
        for (Member method : methods) {
            if (!method.name.equals(methodName)
                    || !method.descriptor.equals(descriptor)
                    || method.code == null) continue;
            for (int p = 0; p < method.code.length; p = next(method.code, p)) {
                int op = method.code[p] & 255;
                if (op < 0xb6 || op > 0xb8) continue;
                int ref = u2(method.code, p + 1);
                String owner = referenceOwner(ref),
                        called = referenceName(ref),
                        type = referenceType(ref);
                if (op == 0xb8
                        && owner.equals("org/lwjgl/opengl/Display")
                        && called.equals("update")
                        && type.equals("()V")) return true;
                if (owner.equals(name) && nativeDisplayLoop(called, type, visited, depth + 1))
                    return true;
            }
        }
        return false;
    }

    /**
     * Matches direct XYZ writes in the native positioning method, requiring bounding-box
     * construction, mutation of an owned box, or the native persistent Pos list as independent
     * evidence.
     */
    String[] positionFields() {
        List<String[]> candidates = new ArrayList<String[]>();
        for (Member m : methods)
            if (("(DDD)V".equals(m.descriptor) || "(FFF)V".equals(m.descriptor))
                    && m.code != null) {
                boolean floating = "(FFF)V".equals(m.descriptor);
                String[] values = new String[3];
                boolean box = false;
                Set<String> ownedObjects = new HashSet<String>();
                int previous = -1;
                for (int p = 0; p < m.code.length; ) {
                    int op = m.code[p] & 255;
                    if (op == 0xb4 && previous >= 0 && (m.code[previous] & 255) == 0x2a) {
                        int ref = u2(m.code, p + 1);
                        String type = referenceType(ref);
                        if (name.equals(referenceOwner(ref))
                                && type.startsWith("L")
                                && type.endsWith(";"))
                            ownedObjects.add(type.substring(1, type.length() - 1));
                    }
                    // Older native entities mutate their own box instead of constructing a new one.
                    // Require a direct this.field receiver type, rather than accepting any
                    // six-double call.
                    if ((op == 0xb7
                                    || op == 0xb8
                                    || op == 0xb6
                                            && ownedObjects.contains(
                                                    referenceOwner(u2(m.code, p + 1))))
                            && (referenceType(u2(m.code, p + 1)).startsWith("(DDDDDD)")
                                    || referenceType(u2(m.code, p + 1)).startsWith("(FFFFFF)")))
                        box = true;
                    // aload_0; dload[_n]; putfield preserves the exact argument-to-coordinate
                    // order.
                    if (op == 0x2a && p + 5 < m.code.length) {
                        int load = m.code[p + 1] & 255,
                                slot =
                                        load == (floating ? 0x17 : 0x18)
                                                ? m.code[p + 2] & 255
                                                : floating
                                                        ? load >= 0x22 && load <= 0x25
                                                                ? load - 0x22
                                                                : -1
                                                        : load >= 0x26 && load <= 0x29
                                                                ? load - 0x26
                                                                : -1;
                        int put = p + (load == (floating ? 0x17 : 0x18) ? 3 : 2);
                        if (slot >= 1
                                && slot <= (floating ? 3 : 5)
                                && (floating || slot % 2 == 1)
                                && put + 2 < m.code.length
                                && (m.code[put] & 255) == 0xb5) {
                            int ref = u2(m.code, put + 1);
                            if (name.equals(referenceOwner(ref))
                                    && (floating ? "F" : "D").equals(referenceType(ref)))
                                values[(slot - 1) / (floating ? 1 : 2)] = referenceName(ref);
                        }
                    }
                    previous = p;
                    p = next(m.code, p);
                }
                if (box && values[0] != null && values[1] != null && values[2] != null)
                    return values;
                if (values[0] != null && values[1] != null && values[2] != null)
                    candidates.add(values);
            }
        String[] serialized = serializedPositionFields();
        if (serialized != null)
            for (String[] candidate : candidates)
                if (Arrays.equals(serialized, candidate)) return candidate;
        return null;
    }

    private String[] serializedPositionFields() {
        for (Member method : methods) {
            if (method.code == null) continue;
            int remaining = 0;
            int previous = -1;
            boolean array = false;
            String[] coordinates = new String[3];
            for (int p = 0; p < method.code.length; p = next(method.code, p)) {
                int op = method.code[p] & 255;
                if (remaining > 0 && --remaining == 0) array = false;
                if (op == 0x12 || op == 0x13) {
                    int index = op == 0x12 ? method.code[p + 1] & 255 : u2(method.code, p + 1);
                    if (tags[index] == 8 && "Pos".equals(text[first[index]])) {
                        remaining = 64;
                        array = false;
                        coordinates = new String[3];
                    }
                }
                if (remaining == 0) continue;
                if (op == 0xbc
                        && previous >= 0
                        && (method.code[previous] & 255) == 0x06
                        && (method.code[p + 1] & 255) == 7) array = true;
                if (array
                        && op == 0x59
                        && p + 6 < method.code.length
                        && (method.code[p + 2] & 255) == 0x2a
                        && (method.code[p + 3] & 255) == 0xb4
                        && (method.code[p + 6] & 255) == 0x52) {
                    int index = (method.code[p + 1] & 255) - 0x03, field = u2(method.code, p + 4);
                    if (index >= 0
                            && index < 3
                            && referenceOwner(field).equals(name)
                            && referenceType(field).equals("D"))
                        coordinates[index] = referenceName(field);
                }
                if (array
                        && op == 0xb6
                        && coordinates[0] != null
                        && coordinates[1] != null
                        && coordinates[2] != null) {
                    String descriptor = referenceType(u2(method.code, p + 1));
                    if (descriptor.startsWith("(Ljava/lang/String;L") && descriptor.endsWith(")V"))
                        return coordinates;
                }
                previous = p;
            }
        }
        return null;
    }

    /**
     * Finds an actual original entity-registration call for the End's dragon, without loading game
     * classes.
     */
    String registeredEndEntity() {
        for (Member method : methods) {
            if ((method.access & 8) == 0 || method.code == null) continue;
            String entity = null;
            boolean endKey = false;
            int remaining = 0;
            for (int p = 0; p < method.code.length; p = next(method.code, p)) {
                if (remaining > 0 && --remaining == 0) {
                    entity = null;
                    endKey = false;
                }
                int op = method.code[p] & 255;
                if (op == 0x12 || op == 0x13) {
                    int index = op == 0x12 ? method.code[p + 1] & 255 : u2(method.code, p + 1);
                    if (tags[index] == 7) {
                        entity = className(index);
                        remaining = 16;
                    } else if (tags[index] == 8) {
                        String key = text[first[index]];
                        if ("EnderDragon".equals(key)
                                || "ender_dragon".equals(key)
                                || "minecraft:ender_dragon".equals(key)) {
                            endKey = true;
                            remaining = 16;
                        }
                    }
                } else if (op == 0xb8 && entity != null && endKey) {
                    int call = u2(method.code, p + 1);
                    String descriptor = referenceType(call);
                    if (referenceOwner(call).equals(name)
                            && descriptor.contains("Ljava/lang/Class;")
                            && descriptor.contains("Ljava/lang/String;")
                            && descriptor.endsWith(")V")) return entity;
                    entity = null;
                    endKey = false;
                    remaining = 0;
                }
            }
        }
        return null;
    }

    /**
     * Locates the native dimension field by its actual NBT serialization key, not an ordinal among
     * integer fields.
     */
    String dimensionField() {
        for (Member m : methods)
            if (m.code != null) {
                boolean dimension = false;
                int remaining = 0;
                for (int p = 0; p < m.code.length; ) {
                    int op = m.code[p] & 255;
                    if (op == 0x12 || op == 0x13) {
                        int index = op == 0x12 ? m.code[p + 1] & 255 : u2(m.code, p + 1);
                        dimension = tags[index] == 8 && "Dimension".equals(text[first[index]]);
                        remaining = 8;
                    }
                    if (dimension && remaining-- > 0 && op == 0xb4) {
                        int ref = u2(m.code, p + 1);
                        if (name.equals(referenceOwner(ref)) && "I".equals(referenceType(ref)))
                            return referenceName(ref);
                    }
                    p = next(m.code, p);
                }
            }
        return null;
    }

    /**
     * Proves a native thread field through the constructor's actual Thread.currentThread
     * assignment.
     */
    String clientThreadField() {
        Set<String> found = new LinkedHashSet<String>();
        for (Member method : methods)
            if (method.name.equals("<init>") && method.code != null) {
                for (int p = 0; p < method.code.length; p = next(method.code, p))
                    if ((method.code[p] & 255) == 0xb8) {
                        int ref = u2(method.code, p + 1), after = next(method.code, p);
                        if (!referenceOwner(ref).equals("java/lang/Thread")
                                || !referenceName(ref).equals("currentThread")
                                || !referenceType(ref).equals("()Ljava/lang/Thread;")
                                || after + 2 >= method.code.length
                                || (method.code[after] & 255) != 0xb5) continue;
                        int field = u2(method.code, after + 1);
                        if (referenceOwner(field).equals(name)
                                && referenceType(field).equals("Ljava/lang/Thread;"))
                            found.add(referenceName(field));
                    }
            }
        return found.size() == 1 ? found.iterator().next() : null;
    }

    byte[] observeFrames() throws IOException {
        List<Member> selected = new ArrayList<Member>();
        for (Member m : methods) {
            if (m.name.startsWith("rodel$observed$")) return null;
            if ((m.access & (8 | 256 | 1024)) == 0
                    && "()V".equals(m.descriptor)
                    && m.code != null
                    && !m.name.startsWith("<")
                    && !m.name.equals("finalize")) selected.add(m);
        }
        if (selected.isEmpty() || selected.size() > 512) return null;
        Pool pool = new Pool(text.length);
        int hooks = pool.type("io/github/richasy/rodel/input/LegacyHooks");
        int callback = pool.method(hooks, "tick", "(Ljava/lang/Object;)V");
        int codeName = pool.utf("Code");
        Map<Member, Integer> renamed = new IdentityHashMap<Member, Integer>(),
                calls = new IdentityHashMap<Member, Integer>();
        for (int i = 0; i < selected.size(); i++) {
            Member m = selected.get(i);
            String n = "rodel$observed$" + i;
            int methodName = pool.utf(n);
            renamed.put(m, methodName);
            calls.put(m, pool.method(thisClass, n, "()V"));
        }
        if (pool.count >= 65535) return null;
        ByteArrayOutputStream buffer =
                new ByteArrayOutputStream(bytes.length + selected.size() * 100);
        DataOutputStream out = new DataOutputStream(buffer);
        out.write(bytes, 0, 8);
        out.writeShort(pool.count);
        out.write(bytes, 10, poolEnd - 10);
        out.write(pool.bytes.toByteArray());
        out.write(bytes, poolEnd, methodsOffset - poolEnd);
        out.writeShort(methods.size() + selected.size());
        for (Member m : methods) {
            if (!renamed.containsKey(m)) {
                out.write(bytes, m.start, m.end - m.start);
                continue;
            }
            out.writeShort((m.access & ~(1 | 4)) | 2);
            out.writeShort(renamed.get(m));
            out.write(bytes, m.start + 4, m.end - m.start - 4);
            out.writeShort(m.access);
            out.writeShort(m.nameIndex);
            out.writeShort(m.descriptorIndex);
            out.writeShort(1);
            out.writeShort(codeName);
            out.writeInt(21);
            out.writeShort(1);
            out.writeShort(1);
            out.writeInt(9);
            out.writeByte(0x2a);
            out.writeByte(0xb7);
            out.writeShort(calls.get(m));
            out.writeByte(0x2a);
            out.writeByte(0xb8);
            out.writeShort(callback);
            out.writeByte(0xb1);
            out.writeShort(0);
            out.writeShort(0);
        }
        out.write(bytes, classAttributes, bytes.length - classAttributes);
        return buffer.toByteArray();
    }

    private static final class Pool {
        final ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        final DataOutputStream out = new DataOutputStream(bytes);
        int count;

        Pool(int count) {
            this.count = count;
        }

        int utf(String value) throws IOException {
            int result = count++;
            out.writeByte(1);
            out.writeUTF(value);
            return result;
        }

        int type(String value) throws IOException {
            int name = utf(value), result = count++;
            out.writeByte(7);
            out.writeShort(name);
            return result;
        }

        int method(int owner, String name, String type) throws IOException {
            int n = utf(name), d = utf(type), pair = count++;
            out.writeByte(12);
            out.writeShort(n);
            out.writeShort(d);
            int result = count++;
            out.writeByte(10);
            out.writeShort(owner);
            out.writeShort(pair);
            return result;
        }
    }

    static int u2(byte[] b, int p) {
        return ((b[p] & 255) << 8) | (b[p + 1] & 255);
    }

    static int i4(byte[] b, int p) {
        return (u2(b, p) << 16) | u2(b, p + 2);
    }

    private static void skip(DataInputStream in, int count) throws IOException {
        if (count < 0 || count > in.available()) throw new EOFException();
        while (count > 0) {
            int n = in.skipBytes(count);
            if (n <= 0) throw new EOFException();
            count -= n;
        }
    }

    static int next(byte[] c, int p) {
        int op = c[p] & 255;
        if (op == 0xaa) {
            int a = (p + 4) & ~3;
            return a + 12 + (i4(c, a + 8) - i4(c, a + 4) + 1) * 4;
        }
        if (op == 0xab) {
            int a = (p + 4) & ~3;
            return a + 8 + i4(c, a + 4) * 8;
        }
        if (op == 0xc4) return p + ((c[p + 1] & 255) == 0x84 ? 6 : 4);
        if (op == 0xc8 || op == 0xc9 || op == 0xb9 || op == 0xba) return p + 5;
        if (op == 0xc5) return p + 4;
        if (op == 0x11
                || op == 0x13
                || op == 0x14
                || op == 0x84
                || op >= 0x99 && op <= 0xa8
                || op >= 0xb2 && op <= 0xb8
                || op == 0xbb
                || op == 0xbd
                || op == 0xc0
                || op == 0xc1
                || op == 0xc6
                || op == 0xc7) return p + 3;
        if (op == 0x10
                || op == 0x12
                || op >= 0x15 && op <= 0x19
                || op >= 0x36 && op <= 0x3a
                || op == 0xa9
                || op == 0xbc) return p + 2;
        return p + 1;
    }
}
