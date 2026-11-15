package io.github.richasy.rodel.bridge;

import java.io.File;
import java.util.concurrent.atomic.AtomicBoolean;

/** Common registry objects, with native methods supplied by the selected API shape. */
abstract class NativeMappedContext extends NativeSeedContext {
    final NativeMappedSymbols s;
    final File directory;
    long seed;
    Object access, packs, stem, registries, generator, random, height, biomes, biomeRegistry;
    Object structureRegistry, structureState, templates, structureManager, dimensionKey;

    NativeMappedContext(NativeMappedSymbols s, File directory, long seed) {
        this.s = s;
        this.directory = directory;
        this.seed = seed;
    }

    abstract Object block(int x, int y, int z) throws Exception;

    Object heightAccessor(int minimum, int size) throws Exception {
        Class<?> type = s.type("net.minecraft.world.level.LevelHeightAccessor");
        try {
            return s.call(type, "create", minimum, size);
        } catch (NoSuchMethodException older) {
            return java.lang.reflect.Proxy.newProxyInstance(
                    type.getClassLoader(),
                    new Class<?>[] {type},
                    (proxy, method, args) -> {
                        if (method.getDeclaringClass() == Object.class)
                            return method.getName().equals("toString")
                                    ? "native-height-accessor"
                                    : method.getName().equals("hashCode")
                                            ? System.identityHashCode(proxy)
                                            : proxy == args[0];
                        String role = s.names.getOrDefault(s.key(method), method.getName());
                        if (role.equals("getHeight") && method.getParameterTypes().length == 0)
                            return size;
                        if (role.equals("getMinBuildHeight")
                                && method.getParameterTypes().length == 0) return minimum;
                        if (method.isDefault())
                            return NativeMappedSymbols.invokeDefault(proxy, method, args);
                        throw new UnsupportedOperationException(
                                "Unknown native height property: " + role);
                    });
        }
    }

    void generateStages(
            Object chunks, int minX, int minZ, int maxX, int maxZ, AtomicBoolean cancelled)
            throws Exception {
        Object features = chunkStatus("FEATURES");
        Object terrain = s.call(features, "getParent");
        for (int stage = 0; stage < 3; stage++) {
            int halo = stage == 0 ? 2 : stage == 1 ? 1 : 0;
            Object status = stage == 0 ? terrain : stage == 1 ? features : chunkStatus("FULL");
            String phase = stage == 0 ? "terrain" : stage == 1 ? "features" : "full";
            int
                    total =
                            ((maxX >> 4) - (minX >> 4) + 1 + halo * 2)
                                    * ((maxZ >> 4) - (minZ >> 4) + 1 + halo * 2),
                    done = 0;
            report(phase, 0, total);
            for (int z = (minZ >> 4) - halo; z <= (maxZ >> 4) + halo; z++)
                for (int x = (minX >> 4) - halo; x <= (maxX >> 4) + halo; x++) {
                    NativeMappedWorld.check(cancelled);
                    s.call(chunks, "getChunk", x, z, status, true);
                    report(phase, ++done, total);
                }
        }
    }

    Object chunkStatus(String field) throws Exception {
        return s.field(
                s.hasType("net.minecraft.world.level.chunk.status.ChunkStatus")
                        ? "net.minecraft.world.level.chunk.status.ChunkStatus"
                        : "net.minecraft.world.level.chunk.ChunkStatus",
                field);
    }

    Object blockRegistry() throws Exception {
        return s.field(
                s.hasType("net.minecraft.core.registries.BuiltInRegistries")
                        ? "net.minecraft.core.registries.BuiltInRegistries"
                        : "net.minecraft.core.Registry",
                "BLOCK");
    }
}
