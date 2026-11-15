package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Original LevelSettings/ServerLevel generation before registry-backed WorldGenSettings. */
final class NativePreRegistryWorld extends NativeMappedContext {
    final Object level, source;
    private final Object server, data, chunks, settings, nativeDimension;
    private final String dimension;
    private final Map<Long, Object> fullChunks = new HashMap<>();
    private final Map<Long, Object> sampledChunks =
            new LinkedHashMap<Long, Object>(128, .75f, true) {
                @Override
                protected boolean removeEldestEntry(Map.Entry<Long, Object> entry) {
                    return size() > 128;
                }
            };
    private final Object surface;
    private boolean closed;

    NativePreRegistryWorld(
            NativeMappedSymbols s, File directory, long seed, Map<String, Object> config)
            throws Exception {
        super(s, directory, seed);
        dimension = Json.string(config, "dimension");
        if (!directory.mkdirs() && !directory.isDirectory())
            throw new IOException("SEED_WORKSPACE_UNAVAILABLE");
        Object fixer = s.call(s.type("net.minecraft.util.datafix.DataFixers"), "getDataFixer");
        Object commands = s.make("net.minecraft.commands.Commands", true);
        Object auth =
                s.make(
                        "com.mojang.authlib.yggdrasil.YggdrasilAuthenticationService",
                        java.net.Proxy.NO_PROXY,
                        "seed-explorer");
        Object session = s.call(auth, "createMinecraftSessionService"),
                repository = s.call(auth, "createProfileRepository");
        Object profile =
                s.make(
                        "net.minecraft.server.players.GameProfileCache",
                        repository,
                        new File(directory, "usercache.json"));
        Object listener =
                s.function(
                        "net.minecraft.server.level.progress.ChunkProgressListener", args -> null);
        Object factory =
                s.function(
                        "net.minecraft.server.level.progress.ChunkProgressListenerFactory",
                        args -> listener);
        Class<?> serverType = s.type("net.minecraft.server.MinecraftServer");
        Set<Method> booleans = new HashSet<>();
        for (Method method : serverType.getMethods())
            if (Modifier.isAbstract(method.getModifiers())
                    && method.getReturnType() == boolean.class
                    && method.getParameterTypes().length == 0
                    && s.names
                            .getOrDefault(s.key(method), method.getName())
                            .equals("canGenerateStructures")) booleans.add(method);
        Constructor<?> constructor =
                NativeSeedSymbols.only(
                        Arrays.asList(serverType.getConstructors()),
                        "PRE_REGISTRY_SERVER_CONSTRUCTOR");
        server =
                NativeSeedHostClass.create(constructor, booleans)
                        .getConstructors()[0]
                        .newInstance(
                                directory,
                                java.net.Proxy.NO_PROXY,
                                fixer,
                                commands,
                                auth,
                                session,
                                repository,
                                profile,
                                factory,
                                "preview");
        try {
            Constructor<?> playerConstructor =
                    NativeSeedSymbols.only(
                            Arrays.asList(
                                    s.type("net.minecraft.server.players.PlayerList")
                                            .getConstructors()),
                            "PRE_REGISTRY_PLAYERS_CONSTRUCTOR");
            Object players =
                    NativeSeedHostClass.create(playerConstructor)
                            .getConstructors()[0]
                            .newInstance(server, 0);
            s.call(server, "setPlayerList", players);
            String preset = Json.string(config, "preset");
            String type =
                    preset.equals("normal")
                            ? "NORMAL"
                            : preset.equals("large_biomes")
                                    ? "LARGE_BIOMES"
                                    : preset.equals("amplified")
                                            ? "AMPLIFIED"
                                            : preset.equals("single_biome") ? "BUFFET" : null;
            if (type == null)
                throw new IllegalArgumentException("Unregistered original world type.");
            settings =
                    s.make(
                            "net.minecraft.world.level.LevelSettings",
                            seed,
                            s.enumeration("net.minecraft.world.level.GameType", "CREATIVE"),
                            true,
                            false,
                            s.field("net.minecraft.world.level.LevelType", type));
            if (preset.equals("single_biome")) {
                String id = Json.string(config, "biome");
                Object registry = s.field("net.minecraft.core.Registry", "BIOME");
                if (s.call(
                                registry,
                                "get",
                                s.make(
                                        "net.minecraft.resources.ResourceLocation",
                                        id.contains(":") ? id : "minecraft:" + id))
                        == null) throw new IllegalArgumentException("Unregistered original biome.");
                String json =
                        Json.encode(
                                Json.object(
                                        "chunk_generator",
                                        Json.object("type", "minecraft:surface"),
                                        "biome_source",
                                        Json.object(
                                                "type",
                                                "minecraft:fixed",
                                                "options",
                                                Json.object(
                                                        "biomes",
                                                        Arrays.asList(
                                                                id.contains(":")
                                                                        ? id
                                                                        : "minecraft:" + id)))));
                Object options = s.call(s.make("com.google.gson.JsonParser"), "parse", json);
                s.call(settings, "setLevelTypeOptions", options);
            }
            data =
                    s.make(
                            "net.minecraft.world.level.storage.LevelData",
                            settings,
                            "Seed exploration");
            access = s.call(s.call(server, "getStorageSource"), "selectLevel", "preview", server);
            s.call(server, "loadDataPacks", new File(directory, "preview"), data);
            s.call(server, "createLevels", access, data, settings, listener);
            String dimensionField =
                    dimension.equals("overworld")
                            ? "OVERWORLD"
                            : dimension.equals("the_nether")
                                    ? "NETHER"
                                    : dimension.equals("the_end") ? "THE_END" : null;
            if (dimensionField == null)
                throw new IllegalArgumentException("Unknown original dimension.");
            nativeDimension =
                    s.field("net.minecraft.world.level.dimension.DimensionType", dimensionField);
            level = s.call(server, "getLevel", nativeDimension);
            if (level == null)
                throw new IllegalStateException("Original dimension was not created.");
            chunks = s.call(level, "getChunkSource");
            generator = s.call(chunks, "getGenerator");
            source = s.call(generator, "getBiomeSource");
            try {
                biomes = s.call(s.call(level, "getBiomeManager"), "withDifferentSource", source);
            } catch (NoSuchMethodException older) {
                biomes = source;
            }
            biomeRegistry = s.field("net.minecraft.core.Registry", "BIOME");
            biomeIds = new IdentityHashMap<>();
            for (Object biome : (Iterable<?>) biomeRegistry)
                biomeIds.put(biome, NativeMappedWorld.id(s.call(biomeRegistry, "getKey", biome)));
            minimumY = 0;
            heightLimit = ((Number) s.call(level, "getMaxBuildHeight")).intValue();
            surface =
                    s.enumeration(
                            "net.minecraft.world.level.levelgen.Heightmap$Types",
                            "WORLD_SURFACE_WG");
            structureRegistry = s.field("net.minecraft.core.Registry", "STRUCTURE_FEATURE");
            templates = s.call(access, "getStructureManager");
            structures = new NativePreRegistryStructures(this);
        } catch (Exception error) {
            try {
                s.call(server, "stopServer");
            } catch (Exception cleanup) {
                error.addSuppressed(cleanup);
            }
            throw error;
        }
    }

    Object position(int x, int y, int z) throws Exception {
        return s.make("net.minecraft.core.BlockPos", x, y, z);
    }

    @Override
    String biome(int x, int y, int z) throws Exception {
        String id = biomeIds.get(s.call(biomes, "getBiome", position(x, y, z)));
        if (id == null) throw new IOException("SEED_BIOME_UNREGISTERED");
        return id;
    }

    @Override
    List<String> possibleBiomes() throws Exception {
        Set<String> result = new TreeSet<>();
        Object values;
        try {
            values = s.field(source, "possibleBiomes");
        } catch (NoSuchFieldException fixed) {
            values = Arrays.asList(s.field(source, "biome"));
        }
        if (values.getClass().isArray()) {
            for (int i = 0; i < Array.getLength(values); i++)
                result.add(biomeIds.get(Array.get(values, i)));
        } else for (Object biome : (Iterable<?>) values) result.add(biomeIds.get(biome));
        return new ArrayList<>(result);
    }

    private static long key(int x, int z) {
        return ((long) x << 32) ^ (z & 0xffffffffL);
    }

    private Object sampledChunk(int x, int z) throws Exception {
        long key = key(x, z);
        Object chunk = sampledChunks.get(key);
        if (chunk == null) {
            chunk =
                    s.make(
                            "net.minecraft.world.level.chunk.ProtoChunk",
                            s.make("net.minecraft.world.level.ChunkPos", x, z),
                            s.field("net.minecraft.world.level.chunk.UpgradeData", "EMPTY"));
            s.call(generator, "fillFromNoise", level, chunk);
            sampledChunks.put(key, chunk);
        }
        return chunk;
    }

    @Override
    int[] column(int x, int z, Integer slice, boolean terrain) throws Exception {
        if (slice != null) return new int[] {slice, 0, slice};
        int top = ((Number) s.call(generator, "getBaseHeight", x, z, surface)).intValue() - 1;
        if (!terrain || top < minimumY) return new int[] {top, 0, top};
        Object chunk = sampledChunk(x >> 4, z >> 4);
        int fluid = fluid(s.call(chunk, "getBlockState", position(x, top, z))), ground = top;
        if (fluid != 0)
            while (ground >= minimumY
                    && fluid(s.call(chunk, "getBlockState", position(x, ground, z))) != 0) ground--;
        return new int[] {ground, fluid, top};
    }

    private int fluid(Object state) throws Exception {
        Object fluid = s.call(state, "getFluidState");
        if (Boolean.TRUE.equals(s.call(fluid, "isEmpty"))) return 0;
        String id =
                s.call(
                                s.field("net.minecraft.core.Registry", "FLUID"),
                                "getKey",
                                s.call(fluid, "getType"))
                        .toString();
        return id.equals("minecraft:water") || id.equals("minecraft:flowing_water")
                ? 1
                : id.equals("minecraft:lava") || id.equals("minecraft:flowing_lava") ? 2 : 0;
    }

    @Override
    Map<String, Object> spawn() throws Exception {
        if (!dimension.equals("overworld"))
            throw new IllegalArgumentException("Native shared spawn belongs to the Overworld.");
        Object original = s.call(level, "getLevelData");
        return Json.object(
                "id",
                "spawn",
                "kind",
                "spawn",
                "x",
                s.call(original, "getXSpawn"),
                "y",
                s.call(original, "getYSpawn"),
                "z",
                s.call(original, "getZSpawn"),
                "pieces",
                0);
    }

    private Object fullChunk(int x, int z) throws Exception {
        long key = key(x, z);
        Object chunk = fullChunks.get(key);
        if (chunk == null) {
            chunk =
                    s.call(
                            chunks,
                            "getChunk",
                            x,
                            z,
                            s.field("net.minecraft.world.level.chunk.ChunkStatus", "FULL"),
                            true);
            fullChunks.put(key, chunk);
        }
        return chunk;
    }

    @Override
    Object block(int x, int y, int z) throws Exception {
        return s.call(fullChunk(x >> 4, z >> 4), "getBlockState", position(x, y, z));
    }

    @Override
    File save(int minX, int minZ, int maxX, int maxZ, boolean loot, AtomicBoolean cancelled)
            throws Exception {
        if ((long) (maxX >> 4) - (minX >> 4) > 32 || (long) (maxZ >> 4) - (minZ >> 4) > 32)
            throw new IOException("SEED_REGION_BUDGET");
        generateStages(chunks, minX, minZ, maxX, maxZ, cancelled);
        for (int z = minZ >> 4; z <= maxZ >> 4; z++)
            for (int x = minX >> 4; x <= maxX >> 4; x++) {
                check(cancelled);
                Object chunk = fullChunk(x, z);
                for (Object entity : ((Map<?, ?>) s.call(chunk, "getBlockEntities")).values()) {
                    if (!s.type("net.minecraft.world.Container").isInstance(entity)) continue;
                    Object pos = s.call(entity, "getBlockPos");
                    int size = ((Number) s.call(entity, "getContainerSize")).intValue();
                    inventories.add(
                            Json.object(
                                    "x",
                                    s.call(pos, "getX"),
                                    "y",
                                    s.call(pos, "getY"),
                                    "z",
                                    s.call(pos, "getZ"),
                                    "slots",
                                    size,
                                    "id",
                                    s.call(
                                                    blockRegistry(),
                                                    "getKey",
                                                    s.call(
                                                            s.call(entity, "getBlockState"),
                                                            "getBlock"))
                                            .toString()));
                    if (loot)
                        for (int slot = 0; slot < size; slot++) s.call(entity, "getItem", slot);
                }
            }
        s.call(server, "saveAllChunks", true, true, true);
        s.call(chunks, "save", true);
        return new File(directory, "preview");
    }

    @Override
    Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        return NativeMappedRegion.read(this, query, cancelled);
    }

    static void check(AtomicBoolean cancelled) {
        if (cancelled.get()) throw new CancellationException();
    }

    /** Saves and releases only this helper's native server, resources and chunk-storage handles. */
    @Override
    public void close() throws Exception {
        if (closed) return;
        closed = true;
        s.call(server, "stopServer");
    }
}
