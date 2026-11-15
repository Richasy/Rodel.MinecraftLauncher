package io.github.richasy.rodel.bridge;

import io.github.richasy.rodel.input.KeyHooks;
import java.io.*;
import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;

/** Wraps the exact straight-line KeyMapping getter, retaining native return-value modifiers. */
final class KeyInputTransformer implements ClassFileTransformer {
    private final String officialOwner, officialMethod;
    private String owner, method;
    private final Instrumentation instrumentation;
    private boolean mappingAttempted;
    KeyInputTransformer(Bindings bindings, Instrumentation instrumentation) {
        officialOwner = bindings.raw("keyIsDown.owner"); officialMethod = bindings.raw("keyIsDown");
        owner = officialOwner == null ? "" : officialOwner.replace('.', '/'); method = officialMethod;
        this.instrumentation = instrumentation;
    }

    @Override public byte[] transform(ClassLoader loader, String name, Class<?> redefining, ProtectionDomain domain, byte[] bytes) {
        try {
            // Fabric initializes its mappings before asking Knot to define the actual game entrypoint.
            // Observe that definition callback, never load Main or a game input class from premain.
            if ("net/minecraft/client/main/Main".equals(name) && !mappingAttempted) resolveFabric();
            if (!owner.equals(name) || redefining != null) return null;
            byte[] result = rewrite(bytes, method);
            if (result != null) KeyHooks.transformed();
            return result;
        } catch (Throwable unsupported) { System.err.println("[rodel-bridge] native input getter unsupported"); return null; }
    }

    private void resolveFabric() throws Exception {
        mappingAttempted = true;
        for (Class<?> type : instrumentation.getAllLoadedClasses()) if (type.getName().equals("net.fabricmc.loader.api.FabricLoader")) {
            Object loader = type.getMethod("getInstance").invoke(null), resolver = type.getMethod("getMappingResolver").invoke(loader);
            Class<?> api = Class.forName("net.fabricmc.loader.api.MappingResolver", false, type.getClassLoader());
            owner = ((String)api.getMethod("mapClassName", String.class, String.class).invoke(resolver, "official", officialOwner)).replace('.', '/');
            method = (String)api.getMethod("mapMethodName", String.class, String.class, String.class, String.class).invoke(resolver, "official", officialOwner, officialMethod, "()Z");
            return;
        }
    }

    static byte[] rewrite(byte[] bytes, String method) throws IOException {
        try (DataInputStream input = new DataInputStream(new ByteArrayInputStream(bytes))) {
            if (input.readInt() != 0xcafebabe) return null;
            input.skipNBytes(4); int count = input.readUnsignedShort();
            if (count > 65529) return null;
            String[] text = new String[count];
            int[] kinds = new int[count], links = new int[count];
            for (int index = 1; index < count; index++) {
                int kind = kinds[index] = input.readUnsignedByte();
                switch (kind) {
                    case 1 -> text[index] = input.readUTF();
                    case 9, 10, 11, 12 -> { input.skipNBytes(2); links[index] = input.readUnsignedShort(); }
                    case 3, 4, 17, 18 -> input.skipNBytes(4);
                    case 5, 6 -> { input.skipNBytes(8); index++; }
                    case 7, 8, 16, 19, 20 -> input.skipNBytes(2);
                    case 15 -> input.skipNBytes(3);
                    default -> throw new IOException("Unsupported constant pool.");
                }
            }
            int poolEnd = bytes.length - input.available();
            input.skipNBytes(6); input.skipNBytes(input.readUnsignedShort() * 2L);
            int fields = input.readUnsignedShort();
            for (int index = 0; index < fields; index++) { input.skipNBytes(6); skipAttributes(input); }
            int methods = input.readUnsignedShort();
            for (int index = 0; index < methods; index++) {
                int access = input.readUnsignedShort(), name = input.readUnsignedShort(), descriptor = input.readUnsignedShort(), attributes = input.readUnsignedShort();
                boolean selected = (access & 0x0008) == 0 && method.equals(text[name]) && "()Z".equals(text[descriptor]);
                for (int attribute = 0; attribute < attributes; attribute++) {
                    int attributeName = input.readUnsignedShort(), lengthOffset = bytes.length - input.available(), length = input.readInt();
                    int body = bytes.length - input.available();
                    if (length < 0 || length > input.available()) throw new IOException("Invalid class attribute.");
                    if (selected && "Code".equals(text[attributeName])) {
                        byte[] old = input.readNBytes(length);
                        if (!getter(old, kinds, links, text)) {
                            if (old.length >= 17) System.err.println("[rodel-bridge] unsupported native input getter: stack=" + u2(old, 0)
                                + " locals=" + u2(old, 2) + " code=" + java.util.HexFormat.of().formatHex(old, 8, Math.min(old.length, 8 + Math.max(0, Math.min(i4(old, 4), 64)))));
                            return null;
                        }
                        int codeLength = i4(old, 4), newCodeLength = codeLength + 4;
                        // Prepending this retains every existing modifier call. No branch relocation,
                        // exception handlers or schema changes; Code-local metadata is omitted.
                        ByteArrayOutputStream target = new ByteArrayOutputStream(bytes.length + 160);
                        DataOutputStream output = new DataOutputStream(target);
                        output.write(bytes, 0, 8); output.writeShort(count + 6); output.write(bytes, 10, poolEnd - 10);
                        output.writeByte(1); output.writeUTF("io/github/richasy/rodel/input/KeyHooks");
                        output.writeByte(7); output.writeShort(count);
                        output.writeByte(1); output.writeUTF("resolve");
                        output.writeByte(1); output.writeUTF("(Ljava/lang/Object;Z)Z");
                        output.writeByte(12); output.writeShort(count + 2); output.writeShort(count + 3);
                        output.writeByte(10); output.writeShort(count + 1); output.writeShort(count + 4);
                        output.write(bytes, poolEnd, lengthOffset - poolEnd); output.writeInt(12 + newCodeLength);
                        output.writeShort(u2(old, 0) + 1); output.writeShort(1); output.writeInt(newCodeLength);
                        output.writeByte(0x2a); output.write(old, 8, codeLength - 1); output.writeByte(0xb8); output.writeShort(count + 5); output.writeByte(0xac);
                        output.writeShort(0); output.writeShort(0);
                        output.write(bytes, body + length, bytes.length - body - length);
                        return target.toByteArray();
                    }
                    input.skipNBytes(length);
                }
            }
            return null;
        }
    }
    private static boolean getter(byte[] code, int[] kinds, int[] links, String[] text) {
        if (code.length < 17 || u2(code, 2) != 1 || u2(code, 0) == 65535) return false;
        int length = i4(code, 4), end = 8 + length, cursor = 8, height = 0, fields = 0;
        if (length < 5 || length > 65531 || end + 4 > code.length || u2(code, end) != 0) return false;
        byte[] stack = new byte[u2(code, 0)];
        while (cursor < end) {
            int opcode = code[cursor++] & 255;
            if (opcode == 0x2a) {
                if (height == stack.length) return false;
                stack[height++] = 1;
            } else if (opcode == 0xb4) {
                if (cursor + 2 > end || height < 1 || stack[height - 1] != 1 || ++fields != 1
                    || !descriptor(u2(code, cursor), 9, "Z", kinds, links, text)) return false;
                cursor += 2; stack[height - 1] = 2;
            } else if (opcode == 0x5f) {
                // MixinExtras inserts ALOAD_0 + SWAP before a one-word return modifier.
                if (height < 2) return false;
                byte top = stack[height - 1]; stack[height - 1] = stack[height - 2]; stack[height - 2] = top;
            } else if (opcode == 0xb6 || opcode == 0xb7) {
                if (cursor + 2 > end || height < 2 || stack[height - 1] != 2 || stack[height - 2] != 1
                    || !descriptor(u2(code, cursor), 10, "(Z)Z", kinds, links, text)) return false;
                cursor += 2; stack[--height - 1] = 2;
            } else if (opcode == 0xac) {
                return cursor == end && fields == 1 && height == 1 && stack[0] == 2;
            } else return false;
        }
        return false;
    }
    private static boolean descriptor(int index, int kind, String expected, int[] kinds, int[] links, String[] text) {
        if (index <= 0 || index >= kinds.length || kinds[index] != kind) return false;
        int nameAndType = links[index];
        if (nameAndType <= 0 || nameAndType >= kinds.length || kinds[nameAndType] != 12) return false;
        int descriptor = links[nameAndType];
        return descriptor > 0 && descriptor < text.length && expected.equals(text[descriptor]);
    }
    private static int u2(byte[] data, int offset) { return ((data[offset] & 255) << 8) | (data[offset + 1] & 255); }
    private static int i4(byte[] data, int offset) { return (u2(data, offset) << 16) | u2(data, offset + 2); }
    private static void skipAttributes(DataInputStream input) throws IOException {
        int count = input.readUnsignedShort();
        for (int index = 0; index < count; index++) { input.skipNBytes(2); int length = input.readInt(); if (length < 0) throw new IOException("Invalid attribute."); input.skipNBytes(length); }
    }
}
