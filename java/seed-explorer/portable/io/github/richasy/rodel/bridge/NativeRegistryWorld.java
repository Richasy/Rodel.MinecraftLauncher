package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Predicate;

/** Native built-in registries and WorldGenSettings used before WorldLoader's registry pipeline. */
final class NativeRegistryWorld extends NativeMappedContext {
    final Object settings, source, resources;
    private final String dimension;
    private final Predicate<Object> opaque;
    private Object server, level, chunks;
    private final Map<Long, Object> fullChunks = new HashMap<>();
    private boolean closed;
    private Boolean columnUsesPosition;

    @SuppressWarnings("unchecked")
    NativeRegistryWorld(
            NativeMappedSymbols s, File directory, long seed, Map<String, Object> config)
            throws Exception {
        super(s, directory, seed);
        dimension = Json.string(config, "dimension");
        try {
            try {
                registries = s.call(s.type("net.minecraft.core.RegistryAccess"), "builtinCopy");
            } catch (NoSuchMethodException older) {
                registries = s.call(s.type("net.minecraft.core.RegistryAccess"), "builtin");
            }
            biomeRegistry = registry("BIOME_REGISTRY");
            Properties properties = new Properties();
            properties.setProperty("level-seed", Long.toString(seed));
            properties.setProperty("generate-structures", "true");
            String preset = Json.string(config, "preset");
            properties.setProperty(
                    "level-type",
                    preset.equals("large_biomes")
                            ? "largebiomes"
                            : preset.equals("single_biome") ? "default" : preset);
            Object nativeSettings;
            try {
                nativeSettings =
                        s.call(
                                s.type("net.minecraft.world.level.levelgen.WorldGenSettings"),
                                "create",
                                registries,
                                properties);
            } catch (NoSuchMethodException older) {
                Object worldProperties =
                        s.make(
                                "net.minecraft.server.dedicated.DedicatedServerProperties$WorldGenProperties",
                                Long.toString(seed),
                                s.make("com.google.gson.JsonObject"),
                                true,
                                properties.getProperty("level-type"));
                nativeSettings =
                        s.call(
                                s.type("net.minecraft.world.level.levelgen.WorldGenSettings"),
                                "create",
                                registries,
                                worldProperties);
            }
            nativeSettings = s.call(nativeSettings, "withSeed", false, OptionalLong.of(seed));
            if (preset.equals("single_biome")) {
                Object location =
                        s.make(
                                "net.minecraft.resources.ResourceLocation",
                                "minecraft:" + Json.string(config, "biome"));
                Object biome = s.call(biomeRegistry, "get", location), fixed;
                if (biome == null)
                    throw new IllegalArgumentException("Unregistered original biome.");
                try {
                    fixed = s.make("net.minecraft.world.level.biome.FixedBiomeSource", biome);
                } catch (NoSuchMethodException holderApi) {
                    Object key =
                            s.call(
                                    s.type("net.minecraft.resources.ResourceKey"),
                                    "create",
                                    s.field("net.minecraft.core.Registry", "BIOME_REGISTRY"),
                                    location);
                    fixed =
                            s.make(
                                    "net.minecraft.world.level.biome.FixedBiomeSource",
                                    s.call(biomeRegistry, "getHolderOrThrow", key));
                }
                Object overworld = s.call(nativeSettings, "overworld"), replacement;
                try {
                    replacement =
                            s.make(
                                    "net.minecraft.world.level.levelgen.NoiseBasedChunkGenerator",
                                    fixed,
                                    seed,
                                    s.field(overworld, "settings"));
                } catch (NoSuchMethodException holderApi) {
                    replacement =
                            s.make(
                                    "net.minecraft.world.level.levelgen.NoiseBasedChunkGenerator",
                                    registry("NOISE_REGISTRY"),
                                    registry("DENSITY_FUNCTION_REGISTRY"),
                                    fixed,
                                    seed,
                                    s.field(overworld, "settings"));
                }
                Object dimensions =
                        s.call(
                                s.type("net.minecraft.world.level.levelgen.WorldGenSettings"),
                                "withOverworld",
                                registry("DIMENSION_TYPE_REGISTRY"),
                                s.call(nativeSettings, "dimensions"),
                                replacement);
                nativeSettings =
                        s.make(
                                "net.minecraft.world.level.levelgen.WorldGenSettings",
                                seed,
                                true,
                                false,
                                dimensions);
            }
            settings = nativeSettings;
            String field =
                    dimension.equals("overworld")
                            ? "OVERWORLD"
                            : dimension.equals("the_nether")
                                    ? "NETHER"
                                    : dimension.equals("the_end") ? "END" : null;
            if (field == null) throw new IllegalArgumentException("Unknown original dimension.");
            Object nativeDimension =
                    s.call(
                            s.call(settings, "dimensions"),
                            "get",
                            s.field("net.minecraft.world.level.dimension.LevelStem", field));
            generator = s.call(nativeDimension, "generator");
            source = s.call(generator, "getBiomeSource");
            Object type;
            try {
                type = s.call(nativeDimension, "type");
            } catch (NoSuchMethodException holderApi) {
                type = s.call(s.call(nativeDimension, "typeHolder"), "value");
            }
            if (s.hasType("net.minecraft.world.level.LevelHeightAccessor")) {
                minimumY = ((Number) s.call(type, "minY")).intValue();
                heightLimit = minimumY + ((Number) s.call(type, "height")).intValue();
                height = heightAccessor(minimumY, heightLimit - minimumY);
            } else {
                Object proto =
                        s.make(
                                "net.minecraft.world.level.chunk.ProtoChunk",
                                s.make("net.minecraft.world.level.ChunkPos", 0, 0),
                                s.field("net.minecraft.world.level.chunk.UpgradeData", "EMPTY"));
                minimumY = 0;
                heightLimit = ((Number) s.call(proto, "getMaxBuildHeight")).intValue();
            }
            Object sampler = null;
            try {
                sampler = s.call(generator, "climateSampler");
            } catch (NoSuchMethodException older) {
            }
            final Object climate = sampler;
            Object noiseBiomes =
                    s.function(
                            "net.minecraft.world.level.biome.BiomeManager$NoiseBiomeSource",
                            args -> {
                                try {
                                    return climate == null
                                            ? s.call(
                                                    source,
                                                    "getNoiseBiome",
                                                    args[0],
                                                    args[1],
                                                    args[2])
                                            : s.call(
                                                    source,
                                                    "getNoiseBiome",
                                                    args[0],
                                                    args[1],
                                                    args[2],
                                                    climate);
                                } catch (Exception error) {
                                    throw new CompletionException(error);
                                }
                            });
            Object hashed =
                    s.call(
                            s.type("net.minecraft.world.level.biome.BiomeManager"),
                            "obfuscateSeed",
                            seed);
            try {
                biomes =
                        s.make("net.minecraft.world.level.biome.BiomeManager", noiseBiomes, hashed);
            } catch (NoSuchMethodException older) {
                biomes =
                        s.make(
                                "net.minecraft.world.level.biome.BiomeManager",
                                noiseBiomes,
                                hashed,
                                s.field(
                                        "net.minecraft.world.level.biome.FuzzyOffsetBiomeZoomer",
                                        "INSTANCE"));
            }
            biomeIds = new IdentityHashMap<>();
            for (Object biome : (Iterable<?>) biomeRegistry)
                biomeIds.put(biome, NativeMappedWorld.id(s.call(biomeRegistry, "getKey", biome)));
            opaque =
                    (Predicate<Object>)
                            s.call(
                                    s.enumeration(
                                            "net.minecraft.world.level.levelgen.Heightmap$Types",
                                            "WORLD_SURFACE_WG"),
                                    "isOpaque");
            Files.createDirectories(directory.toPath());
            Object storage =
                    s.call(
                            s.type("net.minecraft.world.level.storage.LevelStorageSource"),
                            "createDefault",
                            directory.toPath());
            access = s.call(storage, "createAccess", "preview");
            Object sources =
                    Array.newInstance(
                            s.type("net.minecraft.server.packs.repository.RepositorySource"), 1);
            Array.set(
                    sources, 0, s.make("net.minecraft.server.packs.repository.ServerPacksSource"));
            Object packType = s.enumeration("net.minecraft.server.packs.PackType", "SERVER_DATA");
            try {
                packs =
                        s.make(
                                "net.minecraft.server.packs.repository.PackRepository",
                                packType,
                                sources);
            } catch (NoSuchMethodException older) {
                packs = s.make("net.minecraft.server.packs.repository.PackRepository", sources);
            }
            s.call(packs, "reload");
            s.call(packs, "setSelected", Arrays.asList("vanilla"));
            Object packList = s.call(packs, "openAllSelected");
            Executor direct = Runnable::run;
            Object command =
                    s.enumeration("net.minecraft.commands.Commands$CommandSelection", "DEDICATED");
            if (s.hasType("net.minecraft.server.ReloadableServerResources")) {
                Object manager =
                        s.make(
                                "net.minecraft.server.packs.resources.MultiPackResourceManager",
                                packType,
                                packList);
                Object frozen = s.call(registries, "freeze");
                resources =
                        ((CompletableFuture<?>)
                                        s.call(
                                                s.type(
                                                        "net.minecraft.server.ReloadableServerResources"),
                                                "loadResources",
                                                manager,
                                                frozen,
                                                command,
                                                2,
                                                direct,
                                                direct))
                                .join();
                s.call(resources, "updateRegistryTags", registries);
                stem =
                        s.make(
                                "net.minecraft.server.WorldStem",
                                manager,
                                resources,
                                frozen,
                                worldData());
            } else {
                CompletableFuture<?> pending;
                try {
                    pending =
                            (CompletableFuture<?>)
                                    s.call(
                                            s.type("net.minecraft.server.ServerResources"),
                                            "loadResources",
                                            packList,
                                            registries,
                                            command,
                                            2,
                                            direct,
                                            direct);
                } catch (NoSuchMethodException older) {
                    pending =
                            (CompletableFuture<?>)
                                    s.call(
                                            s.type("net.minecraft.server.ServerResources"),
                                            "loadResources",
                                            packList,
                                            command,
                                            2,
                                            direct,
                                            direct);
                }
                resources = pending.join();
                s.call(resources, "updateGlobals");
            }
            Object manager =
                    stem == null
                            ? s.call(resources, "getResourceManager")
                            : s.call(stem, "resourceManager");
            templates =
                    s.make(
                            "net.minecraft.world.level.levelgen.structure.templatesystem.StructureManager",
                            manager,
                            access,
                            s.call(
                                    s.type("net.minecraft.util.datafix.DataFixers"),
                                    "getDataFixer"));
            try {
                structureManager =
                        s.make(
                                "net.minecraft.world.level.StructureFeatureManager",
                                null,
                                settings,
                                null);
            } catch (NoSuchMethodException older) {
                structureManager =
                        s.make("net.minecraft.world.level.StructureFeatureManager", null, settings);
            }
            structureState = generator;
            if (s.hasType("net.minecraft.world.level.levelgen.structure.StructureSet")) {
                structureRegistry = registry("CONFIGURED_STRUCTURE_FEATURE_REGISTRY");
                structures = new NativeMappedStructures(this);
            } else {
                structureRegistry = s.field("net.minecraft.core.Registry", "STRUCTURE_FEATURE");
                structures = new NativeRegistryStructures(this);
            }
        } catch (Exception | Error failure) {
            try {
                close();
            } catch (Exception cleanup) {
                failure.addSuppressed(cleanup);
            }
            throw failure;
        }
    }

    Object registry(String key) throws Exception {
        return s.call(registries, "registryOrThrow", s.field("net.minecraft.core.Registry", key));
    }

    private Object worldData() throws Exception {
        Object data = s.field("net.minecraft.world.level.DataPackConfig", "DEFAULT");
        Object options =
                s.make(
                        "net.minecraft.world.level.LevelSettings",
                        "Seed exploration",
                        s.enumeration("net.minecraft.world.level.GameType", "CREATIVE"),
                        false,
                        s.enumeration("net.minecraft.world.Difficulty", "PEACEFUL"),
                        true,
                        s.make("net.minecraft.world.level.GameRules"),
                        data);
        return s.make(
                "net.minecraft.world.level.storage.PrimaryLevelData",
                options,
                settings,
                s.call(s.type("com.mojang.serialization.Lifecycle"), "stable"));
    }

    private Object value(Object possibleHolder) throws Exception {
        try {
            return s.call(possibleHolder, "value");
        } catch (NoSuchMethodException older) {
            return possibleHolder;
        }
    }

    @Override
    String biome(int x, int y, int z) throws Exception {
        return biomeIds.get(
                value(s.call(biomes, "getBiome", s.make("net.minecraft.core.BlockPos", x, y, z))));
    }

    @Override
    List<String> possibleBiomes() throws Exception {
        Set<String> result = new TreeSet<>();
        for (Object biome : (Iterable<?>) s.call(source, "possibleBiomes"))
            result.add(biomeIds.get(value(biome)));
        return new ArrayList<>(result);
    }

    private Object columnBlock(Object column, int x, int y, int z) throws Exception {
        if (columnUsesPosition == null) {
            try {
                Object result = s.call(column, "getBlock", y);
                columnUsesPosition = false;
                return result;
            } catch (NoSuchMethodException positional) {
                columnUsesPosition = true;
            }
        }
        return columnUsesPosition
                ? s.call(column, "getBlockState", s.make("net.minecraft.core.BlockPos", x, y, z))
                : s.call(column, "getBlock", y);
    }

    @Override
    int[] column(int x, int z, Integer slice, boolean terrain) throws Exception {
        if (slice != null) return new int[] {slice, 0, slice};
        if (!terrain) {
            Object surface =
                    s.enumeration(
                            "net.minecraft.world.level.levelgen.Heightmap$Types",
                            "WORLD_SURFACE_WG");
            int top =
                    ((Number)
                                            (height == null
                                                    ? s.call(
                                                            generator,
                                                            "getBaseHeight",
                                                            x,
                                                            z,
                                                            surface)
                                                    : s.call(
                                                            generator,
                                                            "getBaseHeight",
                                                            x,
                                                            z,
                                                            surface,
                                                            height)))
                                    .intValue()
                            - 1;
            return new int[] {top, 0, top};
        }
        Object column =
                height == null
                        ? s.call(generator, "getBaseColumn", x, z)
                        : s.call(generator, "getBaseColumn", x, z, height);
        int top = heightLimit - 1;
        while (top >= minimumY && !opaque.test(columnBlock(column, x, top, z))) top--;
        int fluid = top >= minimumY ? fluid(columnBlock(column, x, top, z)) : 0, ground = top;
        if (terrain && fluid != 0)
            while (ground >= minimumY && fluid(columnBlock(column, x, ground, z)) != 0) ground--;
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

    private void openServer(boolean initializeSpawn) throws Exception {
        if (server != null) return;
        Object data = stem == null ? worldData() : s.call(stem, "worldData");
        if (!initializeSpawn) s.call(data, "setInitialized", true);
        Object profile =
                s.make(
                        "net.minecraft.server.players.GameProfileCache",
                        null,
                        new File(directory, "usercache.json"));
        Object
                listener =
                        s.function(
                                "net.minecraft.server.level.progress.ChunkProgressListener",
                                args -> null),
                factory =
                        s.function(
                                "net.minecraft.server.level.progress.ChunkProgressListenerFactory",
                                args -> listener);
        Object fixer = s.call(s.type("net.minecraft.util.datafix.DataFixers"), "getDataFixer");
        Constructor<?> ctor =
                NativeSeedSymbols.only(
                        Arrays.asList(
                                s.type("net.minecraft.server.MinecraftServer").getConstructors()),
                        "REGISTRY_SERVER_CONSTRUCTOR");
        Object[] args =
                stem != null
                        ? new Object[] {
                            Thread.currentThread(),
                            access,
                            packs,
                            stem,
                            java.net.Proxy.NO_PROXY,
                            fixer,
                            null,
                            null,
                            profile,
                            factory
                        }
                        : new Object[] {
                            Thread.currentThread(),
                            registries,
                            access,
                            data,
                            packs,
                            java.net.Proxy.NO_PROXY,
                            fixer,
                            resources,
                            null,
                            null,
                            profile,
                            factory
                        };
        server = NativeSeedHostClass.create(ctor).getConstructors()[0].newInstance(args);
        Constructor<?> playerCtor =
                NativeSeedSymbols.only(
                        Arrays.asList(
                                s.type("net.minecraft.server.players.PlayerList")
                                        .getConstructors()),
                        "REGISTRY_PLAYERS_CONSTRUCTOR");
        Object playerRegistry = stem == null ? registries : s.call(stem, "registryAccess");
        Object players =
                NativeSeedHostClass.create(playerCtor)
                        .getConstructors()[0]
                        .newInstance(
                                server, playerRegistry, s.field(server, "playerDataStorage"), 0);
        s.call(server, "setPlayerList", players);
        s.call(server, "createLevels", listener);
        Object key =
                s.field(
                        "net.minecraft.world.level.Level",
                        dimension.equals("overworld")
                                ? "OVERWORLD"
                                : dimension.equals("the_nether") ? "NETHER" : "END");
        level = s.call(server, "getLevel", key);
        chunks = s.call(level, "getChunkSource");
    }

    @Override
    Map<String, Object> spawn() throws Exception {
        if (!dimension.equals("overworld"))
            throw new IllegalArgumentException("Native shared spawn belongs to the Overworld.");
        openServer(true);
        Object pos = s.call(level, "getSharedSpawnPos");
        return Json.object(
                "id",
                "spawn",
                "kind",
                "spawn",
                "x",
                s.call(pos, "getX"),
                "y",
                s.call(pos, "getY"),
                "z",
                s.call(pos, "getZ"),
                "pieces",
                0);
    }

    private Object fullChunk(int x, int z) throws Exception {
        long key = ((long) x << 32) ^ (z & 0xffffffffL);
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
        return s.call(
                fullChunk(x >> 4, z >> 4),
                "getBlockState",
                s.make("net.minecraft.core.BlockPos", x, y, z));
    }

    @Override
    File save(int minX, int minZ, int maxX, int maxZ, boolean loot, AtomicBoolean cancelled)
            throws Exception {
        if ((long) (maxX >> 4) - (minX >> 4) > 32 || (long) (maxZ >> 4) - (minZ >> 4) > 32)
            throw new IOException("SEED_REGION_BUDGET");
        openServer(false);
        generateStages(chunks, minX, minZ, maxX, maxZ, cancelled);
        for (int z = minZ >> 4; z <= maxZ >> 4; z++)
            for (int x = minX >> 4; x <= maxX >> 4; x++) {
                NativeMappedWorld.check(cancelled);
                Object chunk = fullChunk(x, z);
                for (Object entity : ((Map<?, ?>) s.call(chunk, "getBlockEntities")).values())
                    if (s.type("net.minecraft.world.Container").isInstance(entity)) {
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
                                                        s.field(
                                                                "net.minecraft.core.Registry",
                                                                "BLOCK"),
                                                        "getKey",
                                                        s.call(
                                                                s.call(entity, "getBlockState"),
                                                                "getBlock"))
                                                .toString()));
                        if (loot)
                            for (int slot = 0; slot < size; slot++) s.call(entity, "getItem", slot);
                    }
            }
        try {
            s.call(server, "saveEverything", true, true, true);
        } catch (NoSuchMethodException older) {
            s.call(server, "saveAllChunks", true, true, true);
        }
        s.call(chunks, "save", true);
        return new File(directory, "preview");
    }

    @Override
    Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        return NativeMappedRegion.read(this, query, cancelled);
    }

    /** Releases this isolated world's resources and storage lock. */
    @Override
    public void close() throws Exception {
        if (closed) return;
        closed = true;
        try {
            if (server != null) s.call(server, "stopServer");
        } finally {
            try {
                if (access != null) s.call(access, "close");
            } finally {
                if (stem != null) s.call(stem, "close");
                else if (resources != null) s.call(resources, "close");
            }
        }
    }
}
