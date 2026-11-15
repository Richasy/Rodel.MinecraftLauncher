package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Predicate;

/** Registry-backed native generation APIs bound by installed official descriptors. */
final class NativeMappedWorld extends NativeMappedContext {
    private final Predicate<Object> opaque;
    private final String dimension;
    private Object server, level, chunks;
    private final Map<Long, Object> fullChunks = new HashMap<>();
    private boolean closed;

    @SuppressWarnings("unchecked")
    NativeMappedWorld(NativeMappedSymbols s, File directory, long seed, Map<String, Object> config)
            throws Exception {
        super(s, directory, seed);
        dimension = Json.string(config, "dimension");
        try {
            Files.createDirectories(directory.toPath());
            Object storage =
                    s.call(
                            s.type("net.minecraft.world.level.storage.LevelStorageSource"),
                            "createDefault",
                            directory.toPath());
            access = s.call(storage, "createAccess", "preview");
            try {
                packs =
                        s.call(
                                s.type("net.minecraft.server.packs.repository.ServerPacksSource"),
                                "createPackRepository",
                                access);
            } catch (NoSuchMethodException oldRepository) {
                Object sources =
                        Array.newInstance(
                                s.type("net.minecraft.server.packs.repository.RepositorySource"),
                                1);
                Array.set(
                        sources,
                        0,
                        s.make("net.minecraft.server.packs.repository.ServerPacksSource"));
                packs =
                        s.make(
                                "net.minecraft.server.packs.repository.PackRepository",
                                s.enumeration("net.minecraft.server.packs.PackType", "SERVER_DATA"),
                                sources);
            }
            final Object options;
            if (!s.hasType("net.minecraft.world.level.levelgen.WorldOptions")) {
                options = loadWorldGenSettings(config);
            } else {
                Object defaults =
                        s.field("net.minecraft.world.level.WorldDataConfiguration", "DEFAULT");
                Object packConfig =
                        s.make(
                                "net.minecraft.server.WorldLoader$PackConfig",
                                packs,
                                defaults,
                                false,
                                true);
                Object init =
                        s.make(
                                "net.minecraft.server.WorldLoader$InitConfig",
                                packConfig,
                                s.enumeration(
                                        "net.minecraft.commands.Commands$CommandSelection",
                                        "DEDICATED"),
                                s.hasType(
                                                "net.minecraft.server.permissions.LevelBasedPermissionSet")
                                        ? s.field(
                                                "net.minecraft.server.permissions.LevelBasedPermissionSet",
                                                "OWNER")
                                        : 2);
                Object settings = levelSettings(defaults);
                options =
                        s.make(
                                "net.minecraft.world.level.levelgen.WorldOptions",
                                seed,
                                true,
                                false);
                Object supplier =
                        s.function(
                                "net.minecraft.server.WorldLoader$WorldDataSupplier",
                                args -> {
                                    try {
                                        Object context = args[0],
                                                registry = s.call(context, "datapackWorldgen");
                                        String preset = Json.string(config, "preset");
                                        Object dimensions;
                                        if (preset.equals("normal"))
                                            dimensions =
                                                    s.call(
                                                            s.type(
                                                                    "net.minecraft.world.level.levelgen.presets.WorldPresets"),
                                                            "createNormalWorldDimensions",
                                                            registry);
                                        else {
                                            String field =
                                                    preset.equals("large_biomes")
                                                            ? "LARGE_BIOMES"
                                                            : preset.equals("amplified")
                                                                    ? "AMPLIFIED"
                                                                    : preset.equals("single_biome")
                                                                            ? "SINGLE_BIOME_SURFACE"
                                                                            : null;
                                            if (field == null)
                                                throw new IllegalArgumentException(
                                                        "Unregistered native preset.");
                                            Object presets = registry(registry, "WORLD_PRESET");
                                            Object selected =
                                                    registryValue(
                                                            presets,
                                                            s.field(
                                                                    "net.minecraft.world.level.levelgen.presets.WorldPresets",
                                                                    field));
                                            dimensions = s.call(selected, "createWorldDimensions");
                                            if (preset.equals("single_biome")) {
                                                Object biomeKey =
                                                        s.call(
                                                                s.type(
                                                                        "net.minecraft.resources.ResourceKey"),
                                                                "create",
                                                                s.field(
                                                                        "net.minecraft.core.registries.Registries",
                                                                        "BIOME"),
                                                                location(
                                                                        Json.string(
                                                                                config, "biome")));
                                                Object holder =
                                                        registryHolder(
                                                                registry(registry, "BIOME"),
                                                                biomeKey);
                                                Object original = s.call(dimensions, "overworld");
                                                Object fixed =
                                                        s.make(
                                                                "net.minecraft.world.level.biome.FixedBiomeSource",
                                                                holder);
                                                Object replacement =
                                                        s.make(
                                                                "net.minecraft.world.level.levelgen.NoiseBasedChunkGenerator",
                                                                fixed,
                                                                s.call(
                                                                        original,
                                                                        "generatorSettings"));
                                                dimensions =
                                                        s.call(
                                                                dimensions,
                                                                "replaceOverworldGenerator",
                                                                registry,
                                                                replacement);
                                            }
                                        }
                                        Object existing =
                                                registry(
                                                        s.call(context, "datapackDimensions"),
                                                        "LEVEL_STEM");
                                        Object baked = s.call(dimensions, "bake", existing);
                                        Object data;
                                        if (s.hasType(
                                                "net.minecraft.world.level.storage.LevelDataAndDimensions$WorldDataAndGenSettings")) {
                                            data =
                                                    s.make(
                                                            "net.minecraft.world.level.storage.PrimaryLevelData",
                                                            settings,
                                                            s.call(baked, "specialWorldProperty"),
                                                            s.call(baked, "lifecycle"));
                                            data =
                                                    s.make(
                                                            "net.minecraft.world.level.storage.LevelDataAndDimensions$WorldDataAndGenSettings",
                                                            data,
                                                            s.make(
                                                                    "net.minecraft.world.level.levelgen.WorldGenSettings",
                                                                    options,
                                                                    dimensions));
                                        } else {
                                            data =
                                                    s.make(
                                                            "net.minecraft.world.level.storage.PrimaryLevelData",
                                                            settings,
                                                            options,
                                                            s.call(baked, "specialWorldProperty"),
                                                            s.call(baked, "lifecycle"));
                                        }
                                        return s.make(
                                                "net.minecraft.server.WorldLoader$DataLoadOutput",
                                                data,
                                                s.call(baked, "dimensionsRegistryAccess"));
                                    } catch (Exception error) {
                                        throw new CompletionException(error);
                                    }
                                });
                Object factory =
                        s.function(
                                "net.minecraft.server.WorldLoader$ResultFactory",
                                args -> {
                                    try {
                                        return s.make("net.minecraft.server.WorldStem", args);
                                    } catch (Exception error) {
                                        throw new CompletionException(error);
                                    }
                                });
                Executor direct = Runnable::run;
                stem =
                        ((CompletableFuture<?>)
                                        s.call(
                                                s.type("net.minecraft.server.WorldLoader"),
                                                "load",
                                                init,
                                                supplier,
                                                factory,
                                                direct,
                                                direct))
                                .join();
            }
            if (registries == null)
                registries = s.call(s.call(stem, "registries"), "compositeAccess");
            Object dimensions =
                    s.hasType("net.minecraft.world.level.levelgen.WorldOptions")
                            ? registry(registries, "LEVEL_STEM")
                            : s.call(options, "dimensions");
            String key =
                    dimension.equals("overworld")
                            ? "OVERWORLD"
                            : dimension.equals("the_nether")
                                    ? "NETHER"
                                    : dimension.equals("the_end") ? "END" : null;
            if (key == null) throw new IllegalArgumentException("Unknown original dimension.");
            dimensionKey = s.field("net.minecraft.world.level.Level", key);
            Object nativeDimension =
                    registryValue(
                            dimensions,
                            s.field("net.minecraft.world.level.dimension.LevelStem", key));
            Object typeHolder;
            try {
                typeHolder = s.call(nativeDimension, "type");
            } catch (NoSuchMethodException oldHolder) {
                typeHolder = s.call(nativeDimension, "typeHolder");
            }
            Object type = s.call(typeHolder, "value");
            minimumY = ((Number) s.call(type, "minY")).intValue();
            heightLimit = minimumY + ((Number) s.call(type, "height")).intValue();
            height = heightAccessor(minimumY, heightLimit - minimumY);
            generator = s.call(nativeDimension, "generator");
            Object source = s.call(generator, "getBiomeSource");
            Object noise = registry(registries, "NOISE");
            random =
                    s.call(
                            s.type("net.minecraft.world.level.levelgen.RandomState"),
                            "create",
                            s.call(s.call(generator, "generatorSettings"), "value"),
                            lookup(noise),
                            seed);
            Object noiseBiomes =
                    s.function(
                            "net.minecraft.world.level.biome.BiomeManager$NoiseBiomeSource",
                            args -> {
                                try {
                                    return s.call(
                                            source,
                                            "getNoiseBiome",
                                            args[0],
                                            args[1],
                                            args[2],
                                            s.call(random, "sampler"));
                                } catch (Exception error) {
                                    throw new CompletionException(error);
                                }
                            });
            biomes =
                    s.make(
                            "net.minecraft.world.level.biome.BiomeManager",
                            noiseBiomes,
                            s.call(
                                    s.type("net.minecraft.world.level.biome.BiomeManager"),
                                    "obfuscateSeed",
                                    seed));
            biomeRegistry = registry(registries, "BIOME");
            biomeIds = new IdentityHashMap<>();
            for (Object biome : (Iterable<?>) biomeRegistry)
                biomeIds.put(biome, id(s.call(biomeRegistry, "getKey", biome)));
            Object surface =
                    s.enumeration(
                            "net.minecraft.world.level.levelgen.Heightmap$Types",
                            "WORLD_SURFACE_WG");
            opaque = (Predicate<Object>) s.call(surface, "isOpaque");
            structureRegistry = registry(registries, "STRUCTURE");
            try {
                structureState =
                        s.call(
                                generator,
                                "createState",
                                lookup(registry(registries, "STRUCTURE_SET")),
                                random,
                                seed);
            } catch (NoSuchMethodException oldStructureState) {
                s.call(generator, "generatePositions", random);
                structureState = generator;
            }
            Object blocks = blockRegistry();
            Object fixer = s.call(s.type("net.minecraft.util.datafix.DataFixers"), "getDataFixer");
            try {
                templates =
                        s.make(
                                "net.minecraft.world.level.levelgen.structure.templatesystem.StructureTemplateManager",
                                s.call(stem, "resourceManager"),
                                access,
                                fixer,
                                lookup(blocks));
            } catch (NoSuchMethodException oldTemplates) {
                templates =
                        s.make(
                                "net.minecraft.world.level.levelgen.structure.templatesystem.StructureTemplateManager",
                                s.call(stem, "resourceManager"),
                                access,
                                fixer);
            }
            structureManager =
                    s.make("net.minecraft.world.level.StructureManager", null, options, null);
            structures = new NativeMappedStructures(this);
        } catch (Exception | Error failure) {
            try {
                close();
            } catch (Exception cleanup) {
                failure.addSuppressed(cleanup);
            }
            throw failure;
        }
    }

    private Object lookup(Object registry) throws Exception {
        try {
            return s.call(registry, "asLookup");
        } catch (NoSuchMethodException oldLookup) {
            return registry;
        }
    }

    private Object location(String path) throws Exception {
        if (s.hasType("net.minecraft.resources.Identifier"))
            return s.call(
                    s.type("net.minecraft.resources.Identifier"), "withDefaultNamespace", path);
        try {
            return s.call(
                    s.type("net.minecraft.resources.ResourceLocation"),
                    "withDefaultNamespace",
                    path);
        } catch (NoSuchMethodException oldIdentifier) {
            return s.make("net.minecraft.resources.ResourceLocation", "minecraft:" + path);
        }
    }

    private Object registryHolder(Object registry, Object key) throws Exception {
        try {
            return s.call(registry, "getHolderOrThrow", key);
        } catch (NoSuchMethodException newer) {
            return s.call(registry, "getOrThrow", key);
        }
    }

    private Object registryValue(Object registry, Object key) throws Exception {
        try {
            return s.call(registry, "getValue", key);
        } catch (NoSuchMethodException older) {
            Object value = s.call(registry, "get", key);
            return value instanceof Optional<?>
                    ? s.call(((Optional<?>) value).get(), "value")
                    : value;
        }
    }

    private Object levelSettings(Object defaults) throws Exception {
        if (s.hasType("net.minecraft.world.level.LevelSettings$DifficultySettings")) {
            return s.make(
                    "net.minecraft.world.level.LevelSettings",
                    "Seed exploration",
                    s.enumeration("net.minecraft.world.level.GameType", "CREATIVE"),
                    s.make(
                            "net.minecraft.world.level.LevelSettings$DifficultySettings",
                            s.enumeration("net.minecraft.world.Difficulty", "PEACEFUL"),
                            false,
                            false),
                    true,
                    defaults);
        }
        return s.make(
                "net.minecraft.world.level.LevelSettings",
                "Seed exploration",
                s.enumeration("net.minecraft.world.level.GameType", "CREATIVE"),
                false,
                s.enumeration("net.minecraft.world.Difficulty", "PEACEFUL"),
                true,
                gameRules(defaults),
                defaults);
    }

    private Object gameRules(Object defaults) throws Exception {
        String type =
                s.hasType("net.minecraft.world.level.gamerules.GameRules")
                        ? "net.minecraft.world.level.gamerules.GameRules"
                        : "net.minecraft.world.level.GameRules";
        try {
            return s.make(type);
        } catch (NoSuchMethodException featureFlags) {
            return s.make(type, s.call(defaults, "enabledFeatures"));
        }
    }

    private Object playerRegistries() throws Exception {
        try {
            return s.call(stem, "registries");
        } catch (NoSuchMethodException oldRegistries) {
            return s.call(stem, "registryAccess");
        }
    }

    private Object worldData() throws Exception {
        try {
            return s.call(stem, "worldData");
        } catch (NoSuchMethodException savedSettings) {
            return s.call(s.call(stem, "worldDataAndGenSettings"), "data");
        }
    }

    /** Loads the original JSON registries through the earlier WorldLoader supplier contract. */
    private Object loadWorldGenSettings(Map<String, Object> config) throws Exception {
        Object defaults = s.field("net.minecraft.world.level.DataPackConfig", "DEFAULT");
        Object packConfig =
                s.make("net.minecraft.server.WorldLoader$PackConfig", packs, defaults, false);
        Object init =
                s.make(
                        "net.minecraft.server.WorldLoader$InitConfig",
                        packConfig,
                        s.enumeration(
                                "net.minecraft.commands.Commands$CommandSelection", "DEDICATED"),
                        2);
        final Object[] generated = new Object[1];
        Object supplier =
                s.function(
                        "net.minecraft.server.WorldLoader$WorldDataSupplier",
                        args -> {
                            try {
                                Object registry =
                                        s.call(
                                                s.type("net.minecraft.core.RegistryAccess"),
                                                "builtinCopy");
                                s.call(
                                        s.type("net.minecraft.resources.RegistryOps"),
                                        "createAndLoad",
                                        s.field("com.mojang.serialization.JsonOps", "INSTANCE"),
                                        registry,
                                        args[0]);
                                String preset = Json.string(config, "preset");
                                String field =
                                        preset.equals("normal")
                                                ? "NORMAL"
                                                : preset.equals("large_biomes")
                                                        ? "LARGE_BIOMES"
                                                        : preset.equals("amplified")
                                                                ? "AMPLIFIED"
                                                                : preset.equals("single_biome")
                                                                        ? "SINGLE_BIOME_SURFACE"
                                                                        : null;
                                if (field == null)
                                    throw new IllegalArgumentException(
                                            "Unregistered native preset.");
                                Object nativePreset =
                                        s.call(
                                                registry(registry, "WORLD_PRESET"),
                                                "get",
                                                s.field(
                                                        "net.minecraft.world.level.levelgen.presets.WorldPresets",
                                                        field));
                                Object options =
                                        s.call(
                                                nativePreset,
                                                "createWorldGenSettings",
                                                seed,
                                                true,
                                                false);
                                if (preset.equals("single_biome")) {
                                    Object key =
                                            s.call(
                                                    s.type("net.minecraft.resources.ResourceKey"),
                                                    "create",
                                                    s.field(
                                                            "net.minecraft.core.Registry",
                                                            "BIOME_REGISTRY"),
                                                    s.make(
                                                            "net.minecraft.resources.ResourceLocation",
                                                            "minecraft:"
                                                                    + Json.string(
                                                                            config, "biome")));
                                    Object fixed =
                                            s.make(
                                                    "net.minecraft.world.level.biome.FixedBiomeSource",
                                                    s.call(
                                                            registry(registry, "BIOME"),
                                                            "getHolderOrThrow",
                                                            key));
                                    Object original = s.call(options, "overworld");
                                    Object generator =
                                            s.make(
                                                    "net.minecraft.world.level.levelgen.NoiseBasedChunkGenerator",
                                                    registry(registry, "STRUCTURE_SET"),
                                                    registry(registry, "NOISE"),
                                                    fixed,
                                                    s.call(original, "generatorSettings"));
                                    options =
                                            s.call(
                                                    s.type(
                                                            "net.minecraft.world.level.levelgen.WorldGenSettings"),
                                                    "replaceOverworldGenerator",
                                                    registry,
                                                    options,
                                                    generator);
                                }
                                generated[0] = options;
                                Object settings =
                                        s.make(
                                                "net.minecraft.world.level.LevelSettings",
                                                "Seed exploration",
                                                s.enumeration(
                                                        "net.minecraft.world.level.GameType",
                                                        "CREATIVE"),
                                                false,
                                                s.enumeration(
                                                        "net.minecraft.world.Difficulty",
                                                        "PEACEFUL"),
                                                true,
                                                s.make("net.minecraft.world.level.GameRules"),
                                                args[1]);
                                Object data =
                                        s.make(
                                                "net.minecraft.world.level.storage.PrimaryLevelData",
                                                settings,
                                                options,
                                                s.call(
                                                        s.type(
                                                                "com.mojang.serialization.Lifecycle"),
                                                        "stable"));
                                return s.call(
                                        s.type("com.mojang.datafixers.util.Pair"),
                                        "of",
                                        data,
                                        s.call(registry, "freeze"));
                            } catch (Exception failure) {
                                throw new CompletionException(failure);
                            }
                        });
        Object factory =
                s.function(
                        "net.minecraft.server.WorldLoader$ResultFactory",
                        args -> {
                            try {
                                return s.make("net.minecraft.server.WorldStem", args);
                            } catch (Exception failure) {
                                throw new CompletionException(failure);
                            }
                        });
        Executor direct = Runnable::run;
        stem =
                ((CompletableFuture<?>)
                                s.call(
                                        s.type("net.minecraft.server.WorldLoader"),
                                        "load",
                                        init,
                                        supplier,
                                        factory,
                                        direct,
                                        direct))
                        .join();
        registries = s.call(stem, "registryAccess");
        return generated[0];
    }

    private Object registry(Object access, String key) throws Exception {
        Object nativeKey =
                s.field(
                        s.hasType("net.minecraft.core.registries.Registries")
                                ? "net.minecraft.core.registries.Registries"
                                : "net.minecraft.core.Registry",
                        s.hasType("net.minecraft.core.registries.Registries")
                                ? key
                                : key + "_REGISTRY");
        try {
            return s.call(access, "registryOrThrow", nativeKey);
        } catch (NoSuchMethodException newer) {
            return s.call(access, "lookupOrThrow", nativeKey);
        }
    }

    static String id(Object key) {
        String value = key.toString();
        return value.startsWith("minecraft:") ? value.substring(10) : value;
    }

    Object position(int x, int y, int z) throws Exception {
        return s.make("net.minecraft.core.BlockPos", x, y, z);
    }

    @Override
    String biome(int x, int y, int z) throws Exception {
        return biomeIds.get(s.call(s.call(biomes, "getBiome", position(x, y, z)), "value"));
    }

    @Override
    List<String> possibleBiomes() throws Exception {
        List<String> result = new ArrayList<>();
        for (Object holder :
                (Iterable<?>) s.call(s.call(generator, "getBiomeSource"), "possibleBiomes"))
            result.add(biomeIds.get(s.call(holder, "value")));
        Collections.sort(result);
        return result;
    }

    @Override
    int[] column(int x, int z, Integer slice, boolean terrain) throws Exception {
        if (slice != null) return new int[] {slice, 0, slice};
        if (!terrain) {
            int top =
                    ((Number)
                                            s.call(
                                                    generator,
                                                    "getBaseHeight",
                                                    x,
                                                    z,
                                                    s.enumeration(
                                                            "net.minecraft.world.level.levelgen.Heightmap$Types",
                                                            "WORLD_SURFACE_WG"),
                                                    height,
                                                    random))
                                    .intValue()
                            - 1;
            return new int[] {top, 0, top};
        }
        Object column = s.call(generator, "getBaseColumn", x, z, height, random);
        int top = heightLimit - 1;
        while (top >= minimumY && !opaque.test(s.call(column, "getBlock", top))) top--;
        int fluid = top >= minimumY ? fluid(s.call(column, "getBlock", top)) : 0;
        int ground = top;
        if (terrain && fluid != 0)
            while (ground >= minimumY && fluid(s.call(column, "getBlock", ground)) != 0) ground--;
        return new int[] {ground, fluid, top};
    }

    private int fluid(Object state) throws Exception {
        Object fluid = s.call(state, "getFluidState");
        if (Boolean.TRUE.equals(s.call(fluid, "isEmpty"))) return 0;
        String id =
                s.call(
                                s.field(
                                        s.hasType("net.minecraft.core.registries.BuiltInRegistries")
                                                ? "net.minecraft.core.registries.BuiltInRegistries"
                                                : "net.minecraft.core.Registry",
                                        "FLUID"),
                                "getKey",
                                s.call(fluid, "getType"))
                        .toString();
        return id.equals("minecraft:water") || id.equals("minecraft:flowing_water")
                ? 1
                : id.equals("minecraft:lava") || id.equals("minecraft:flowing_lava") ? 2 : 0;
    }

    private void openServer(boolean initializeSpawn) throws Exception {
        if (server != null) return;
        Object data = worldData();
        if (!initializeSpawn) {
            s.call(data, "setInitialized", true);
            if (s.hasType("net.minecraft.world.level.storage.LevelData$RespawnData"))
                s.call(
                        data,
                        "setSpawn",
                        s.call(
                                s.type("net.minecraft.world.level.storage.LevelData$RespawnData"),
                                "of",
                                s.field("net.minecraft.world.level.Level", "OVERWORLD"),
                                position(0, 80, 0),
                                0f,
                                0f));
        }
        Object services;
        boolean namedResolver = s.hasType("net.minecraft.server.players.UserNameToIdResolver");
        if (namedResolver) {
            Object names =
                    s.function(
                            "net.minecraft.server.players.UserNameToIdResolver",
                            args -> Optional.empty());
            Object profiles =
                    s.function(
                            "net.minecraft.server.players.ProfileResolver",
                            args -> Optional.empty());
            String keys =
                    s.hasType("com.mojang.authlib.services.ServicesKeySet")
                            ? "com.mojang.authlib.services.ServicesKeySet"
                            : "com.mojang.authlib.yggdrasil.ServicesKeySet";
            services =
                    s.make(
                            "net.minecraft.server.Services",
                            null,
                            s.field(keys, "EMPTY"),
                            null,
                            names,
                            profiles);
        } else {
            Object profile =
                    s.make(
                            "net.minecraft.server.players.GameProfileCache",
                            null,
                            new File(directory, "usercache.json"));
            services =
                    s.make(
                            "net.minecraft.server.Services",
                            null,
                            s.hasType("com.mojang.authlib.yggdrasil.ServicesKeySet")
                                    ? s.field(
                                            "com.mojang.authlib.yggdrasil.ServicesKeySet", "EMPTY")
                                    : null,
                            null,
                            profile);
        }
        boolean levelListener = s.hasType("net.minecraft.server.level.progress.LevelLoadListener");
        Object listener =
                levelListener
                        ? s.call(
                                s.type(
                                        "net.minecraft.server.level.progress.LoggingLevelLoadListener"),
                                "forSingleplayer")
                        : s.function(
                                "net.minecraft.server.level.progress.ChunkProgressListener",
                                args -> null);
        Object factory =
                levelListener
                        ? null
                        : s.function(
                                "net.minecraft.server.level.progress.ChunkProgressListenerFactory",
                                args -> listener);
        Constructor<?> constructor =
                NativeSeedSymbols.only(
                        Arrays.asList(
                                s.type("net.minecraft.server.MinecraftServer").getConstructors()),
                        "MAPPED_SERVER_CONSTRUCTOR");
        Object fixer = s.call(s.type("net.minecraft.util.datafix.DataFixers"), "getDataFixer");
        Object[] arguments;
        if (levelListener) {
            List<Object> list =
                    new ArrayList<>(Arrays.asList(Thread.currentThread(), access, packs, stem));
            if (Arrays.asList(constructor.getParameterTypes()).contains(Optional.class))
                list.add(Optional.empty());
            list.addAll(Arrays.asList(java.net.Proxy.NO_PROXY, fixer, services, listener));
            if (Arrays.asList(constructor.getParameterTypes()).contains(boolean.class))
                list.add(false);
            if (constructor.getParameterTypes().length == list.size() + 1)
                list.add(s.make("net.minecraft.server.notifications.NotificationManager"));
            arguments = list.toArray();
        } else
            arguments =
                    new Object[] {
                        Thread.currentThread(),
                        access,
                        packs,
                        stem,
                        java.net.Proxy.NO_PROXY,
                        fixer,
                        services,
                        factory
                    };
        server =
                NativeSeedHostClass.create(constructor).getConstructors()[0].newInstance(arguments);
        Constructor<?> playersConstructor =
                NativeSeedSymbols.only(
                        Arrays.asList(
                                s.type("net.minecraft.server.players.PlayerList")
                                        .getConstructors()),
                        "MAPPED_PLAYERS_CONSTRUCTOR");
        Object players =
                NativeSeedHostClass.create(playersConstructor)
                        .getConstructors()[0]
                        .newInstance(
                                server,
                                playerRegistries(),
                                s.field(server, "playerDataStorage"),
                                playersConstructor.getParameterTypes()[3] != int.class
                                        ? s.make(
                                                "net.minecraft.server.notifications.EmptyNotificationService")
                                        : 0);
        s.call(server, "setPlayerList", players);
        if (levelListener) s.call(server, "createLevels");
        else s.call(server, "createLevels", listener);
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
        Object pos;
        try {
            pos = s.call(level, "getSharedSpawnPos");
        } catch (NoSuchMethodException respawnData) {
            pos = s.call(s.call(s.call(worldData(), "overworldData"), "getRespawnData"), "pos");
        }
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

    Object fullChunk(int x, int z) throws Exception {
        long key = ((long) x << 32) ^ (z & 0xffffffffL);
        Object chunk = fullChunks.get(key);
        if (chunk == null) {
            chunk = s.call(chunks, "getChunk", x, z, chunkStatus("FULL"), true);
            fullChunks.put(key, chunk);
        }
        return chunk;
    }

    Object block(int x, int y, int z) throws Exception {
        return s.call(fullChunk(x >> 4, z >> 4), "getBlockState", position(x, y, z));
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
        s.call(server, "saveEverything", true, true, true);
        s.call(chunks, "save", true);
        return new File(directory, "preview");
    }

    @Override
    Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        return NativeMappedRegion.read(this, query, cancelled);
    }

    @Override
    Map<String, Object> predictLoot(Map<String, Object> query, AtomicBoolean cancelled)
            throws Exception {
        openServer(false);
        return NativeMappedLoot.read(s, server, level, blockRegistry(), query, cancelled);
    }

    static void check(AtomicBoolean cancelled) {
        if (cancelled.get()) throw new CancellationException();
    }

    /** Closes only this helper's native server and resource pack/storage handles. */
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
                else if (packs != null) {
                    try {
                        s.call(packs, "close");
                    } catch (NoSuchMethodException noOwnedClose) {
                    }
                }
            }
        }
    }
}
