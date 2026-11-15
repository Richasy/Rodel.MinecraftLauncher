package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.jar.*;

/**
 * Resolves installed class-file evidence and optional official symbols; no Minecraft version table
 * is used.
 */
final class LegacySymbols implements Closeable {
    final JarFile jar;
    final String client;
    final String clientThread;
    final Properties bindings;
    final boolean dimensions;
    final boolean netty;
    final boolean end;
    private final Map<String, LegacyClassFile> classes = new HashMap<String, LegacyClassFile>();

    LegacySymbols(Properties bindings) throws IOException {
        this.bindings = bindings;
        jar = new JarFile(bindings.getProperty("clientJar"));
        String selected = bindings.getProperty("clientClass");
        boolean hasDimensions = selected != null, hasNetty = false, hasEnd = false;
        boolean discoverClient =
                selected == null || jar.getJarEntry(selected.replace('.', '/') + ".class") == null;
        int best = 0, runnerUp = 0;
        {
            Enumeration<JarEntry> entries = jar.entries();
            int count = 0;
            while (entries.hasMoreElements()) {
                JarEntry entry = entries.nextElement();
                if (!entry.getName().endsWith(".class")) continue;
                if (++count > 50000) throw new IOException("CLASS_BUDGET");
                LegacyClassFile type = read(entry);
                for (String value : type.text) {
                    if ("Dimension".equals(value) || "minecraft:the_nether".equals(value))
                        hasDimensions = true;
                    if ("io/netty/channel/Channel".equals(value)) hasNetty = true;
                }
                if (!hasEnd) {
                    String entity = type.registeredEndEntity();
                    hasEnd = entity != null && originalEntity(entity);
                }
                if (discoverClient) {
                    int score = type.clientScore();
                    if (score > best) {
                        runnerUp = best;
                        best = score;
                        selected = type.name;
                    } else runnerUp = Math.max(score, runnerUp);
                }
            }
            if (discoverClient && (best < 100 || best - runnerUp < 30))
                throw new IOException("GAME_CLIENT_AMBIGUOUS");
        }
        client = selected.replace('.', '/');
        clientThread = read(jar.getJarEntry(client + ".class")).clientThreadField();
        dimensions = hasDimensions;
        netty = hasNetty || bindings.containsKey("entityClass");
        end = hasEnd;
    }

    private boolean originalEntity(String name) throws IOException {
        Set<String> visited = new HashSet<String>();
        while (name != null && visited.size() < 64 && visited.add(name)) {
            LegacyClassFile type = classes.get(name);
            if (type == null) {
                JarEntry entry = jar.getJarEntry(name + ".class");
                if (entry == null) return false;
                type = read(entry);
                classes.put(name, type);
            }
            if (type.positionFields() != null) return true;
            name = type.parent;
        }
        return false;
    }

    synchronized LegacyClassFile type(Class<?> type) throws IOException {
        String name = type.getName().replace('.', '/');
        LegacyClassFile found = classes.get(name);
        if (found != null) return found;
        JarEntry entry = jar.getJarEntry(name + ".class");
        if (entry == null) return null;
        found = read(entry);
        classes.put(name, found);
        return found;
    }

    private LegacyClassFile read(JarEntry entry) throws IOException {
        if (entry.getSize() > 4 * 1024 * 1024) throw new IOException("CLASS_BUDGET");
        InputStream input = jar.getInputStream(entry);
        try {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int size;
            while ((size = input.read(buffer)) != -1) {
                if (bytes.size() + size > 4 * 1024 * 1024) throw new IOException("CLASS_BUDGET");
                bytes.write(buffer, 0, size);
            }
            return new LegacyClassFile(bytes.toByteArray());
        } finally {
            input.close();
        }
    }

    Object field(Object owner, String role) throws Exception {
        String name = bindings.getProperty(role);
        if (name == null) return null;
        for (Class<?> type = owner.getClass(); type != null; type = type.getSuperclass())
            try {
                Field f = type.getDeclaredField(name);
                f.setAccessible(true);
                return f.get(owner);
            } catch (NoSuchFieldException absent) {
            }
        return null;
    }

    Object call(Object owner, String role) throws Exception {
        String name = bindings.getProperty(role);
        if (name == null) return null;
        for (Class<?> type = owner.getClass(); type != null; type = type.getSuperclass())
            try {
                Method method = type.getDeclaredMethod(name);
                method.setAccessible(true);
                return method.invoke(owner);
            } catch (NoSuchMethodException absent) {
            }
        return null;
    }

    /**
     * Reads a mapped static registry-name accessor whose single argument is its native declaring
     * type.
     */
    Object callStatic(Object value, String role) throws Exception {
        String name = bindings.getProperty(role);
        if (name == null) return null;
        for (Class<?> type = value.getClass(); type != null; type = type.getSuperclass())
            try {
                Method method = type.getDeclaredMethod(name, type);
                if (!Modifier.isStatic(method.getModifiers())) continue;
                method.setAccessible(true);
                return method.invoke(null, value);
            } catch (NoSuchMethodException absent) {
            }
        return null;
    }

    @Override
    public void close() throws IOException {
        jar.close();
    }
}
