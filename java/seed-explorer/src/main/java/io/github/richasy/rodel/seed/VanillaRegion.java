package io.github.richasy.rodel.seed;

import static io.github.richasy.rodel.seed.Protocol.*;

import com.google.gson.JsonElement;
import com.mojang.authlib.GameProfile;
import com.mojang.authlib.services.ServicesKeySet;
import com.mojang.serialization.JsonOps;
import com.mojang.serialization.Lifecycle;

import io.github.richasy.rodel.seed.Protocol.Position;

import net.minecraft.*;
import net.minecraft.commands.Commands;
import net.minecraft.core.*;
import net.minecraft.core.registries.*;
import net.minecraft.gizmos.*;
import net.minecraft.resources.*;
import net.minecraft.server.*;
import net.minecraft.server.level.*;
import net.minecraft.server.level.progress.*;
import net.minecraft.server.notifications.*;
import net.minecraft.server.packs.repository.*;
import net.minecraft.server.permissions.*;
import net.minecraft.server.players.*;
import net.minecraft.util.*;
import net.minecraft.util.datafix.DataFixers;
import net.minecraft.util.debugchart.*;
import net.minecraft.world.Container;
import net.minecraft.world.RandomizableContainer;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.*;
import net.minecraft.world.level.biome.FixedBiomeSource;
import net.minecraft.world.level.block.entity.vault.VaultBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.*;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.dimension.*;
import net.minecraft.world.level.levelgen.*;
import net.minecraft.world.level.levelgen.presets.*;
import net.minecraft.world.level.storage.*;

import java.net.Proxy;
import java.nio.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;
import java.util.function.Function;

/**
 * Bounded original FULL generation. No network listener, game ticking, player identity, mod or
 * player save is opened.
 */
final class VanillaRegion extends MinecraftServer {
    private static final UserNameToIdResolver NO_NAMES =
            new UserNameToIdResolver() {
                public void add(NameAndId value) {}

                public Optional<NameAndId> get(String name) {
                    return Optional.empty();
                }

                public Optional<NameAndId> get(UUID id) {
                    return Optional.empty();
                }

                public void resolveOfflineUsers(boolean value) {}

                public void save() {}
            };
    private static final ProfileResolver NO_PROFILES =
            new ProfileResolver() {
                public Optional<GameProfile> fetchByName(String name) {
                    return Optional.empty();
                }

                public Optional<GameProfile> fetchById(UUID id) {
                    return Optional.empty();
                }
            };
    private static final Services NO_SERVICES =
            new Services(null, ServicesKeySet.EMPTY, null, NO_NAMES, NO_PROFILES);
    private final LocalSampleLogger logger = new LocalSampleLogger(4);

    private VanillaRegion(
            LevelStorageSource.LevelStorageAccess storage, PackRepository packs, WorldStem stem) {
        super(
                Thread.currentThread(),
                storage,
                packs,
                stem,
                Optional.empty(),
                Proxy.NO_PROXY,
                DataFixers.getDataFixer(),
                NO_SERVICES,
                LoggingLevelLoadListener.forSingleplayer(),
                false,
                new NotificationManager());
    }

    protected boolean initServer() {
        setPlayerList(
                new PlayerList(
                        this, registries(), playerDataStorage, new EmptyNotificationService()) {});
        Gizmos.withCollector(GizmoCollector.NOOP);
        createLevels();
        return true;
    }

    protected SampleLogger getTickTimeLogger() {
        return logger;
    }

    public boolean isTickTimeLoggingEnabled() {
        return false;
    }

    public SystemReport fillServerSystemReport(SystemReport report) {
        return report;
    }

    public LevelBasedPermissionSet operatorUserPermissions() {
        return LevelBasedPermissionSet.OWNER;
    }

    public PermissionSet getFunctionCompilationPermissions() {
        return LevelBasedPermissionSet.OWNER;
    }

    public boolean shouldRconBroadcast() {
        return false;
    }

    public boolean isDedicatedServer() {
        return false;
    }

    public int getMaxPlayers() {
        return 0;
    }

    public int getRateLimitPacketsPerSecond() {
        return 0;
    }

    public int getCommandSpamThresholdSeconds() {
        return 0;
    }

    public int getChatSpamThresholdSeconds() {
        return 0;
    }

    public boolean useNativeTransport() {
        return false;
    }

    public boolean isPublished() {
        return false;
    }

    public boolean shouldInformAdmins() {
        return false;
    }

    public boolean isSingleplayerOwner(NameAndId profile) {
        return false;
    }

    private static void check(AtomicBoolean cancelled) {
        if (cancelled.get() || Thread.currentThread().isInterrupted())
            throw new CancellationException();
    }

    static RegionResult generate(
            Path root,
            RegionQuery query,
            AtomicBoolean cancelled,
            Consumer<RegionProgress> progress)
            throws Exception {
        if (query == null || query.world() == null || query.bounds() == null)
            throw new IllegalArgumentException("A bounded region and world are required.");
        var b = query.bounds();
        if ((long) b.maxX() - b.minX() < 0
                || (long) b.maxX() - b.minX() >= 256
                || (long) b.maxZ() - b.minZ() < 0
                || (long) b.maxZ() - b.minZ() >= 256
                || b.minX() < -30_000_000
                || b.minZ() < -30_000_000
                || b.maxX() >= 30_000_000
                || b.maxZ() >= 30_000_000
                || b.minY() < -64
                || b.maxY() > 319
                || b.maxY() < b.minY()
                || (query.includeBlocks() && b.volume() > 4_194_304))
            throw new IllegalArgumentException("Region bounds exceed the original preview budget.");
        if (!List.of("overworld", "the_nether", "the_end").contains(query.world().dimension())
                || !List.of("normal", "large_biomes", "amplified", "single_biome")
                        .contains(query.world().preset()))
            throw new IllegalArgumentException("Unknown original dimension or preset.");
        long started = System.nanoTime();
        check(cancelled);
        progress.accept(new RegionProgress("loading", 0, 0));
        var result =
                inWorld(
                        root,
                        query.world(),
                        query.seed(),
                        false,
                        cancelled,
                        server -> server.read(query, cancelled, progress));
        return new RegionResult(
                result.seed(),
                result.world(),
                b,
                "fixed_region_v1",
                result.palette(),
                result.blocks(),
                result.materials(),
                result.containers(),
                (System.nanoTime() - started) / 1_000_000.0,
                result.tintPalette(),
                result.tintIndices());
    }

    /**
     * Evaluates a recorded table on a detached original block entity in this helper's disposable
     * world.
     */
    static ContainerEntry predictLoot(Path root, LootQuery query, AtomicBoolean cancelled)
            throws Exception {
        if (query == null
                || query.world() == null
                || query.id() == null
                || query.lootTable() == null
                || Math.abs((long) query.x()) > 30_000_000
                || Math.abs((long) query.z()) > 30_000_000)
            throw new IllegalArgumentException(
                    "A recorded container table, seed and origin are required.");
        long lootSeed = Long.parseLong(query.lootSeed());
        return inWorld(
                root,
                query.world(),
                query.seed(),
                false,
                cancelled,
                server -> {
                    var level =
                            server.getLevel(
                                    ResourceKey.create(
                                            Registries.DIMENSION,
                                            Identifier.withDefaultNamespace(
                                                    query.world().dimension())));
                    if (level == null
                            || query.y() < level.getMinY()
                            || query.y() >= level.getMinY() + level.getHeight())
                        throw new IllegalArgumentException(
                                "The saved loot origin is outside the original dimension.");
                    var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(query.id()));
                    if (!(block instanceof net.minecraft.world.level.block.EntityBlock entityBlock))
                        throw new IllegalArgumentException(
                                "The recorded block has no original block entity.");
                    var position = new BlockPos(query.x(), query.y(), query.z());
                    var entity = entityBlock.newBlockEntity(position, block.defaultBlockState());
                    if (!(entity instanceof RandomizableContainer random)
                            || !(entity instanceof Container container))
                        throw new IllegalArgumentException(
                                "The recorded block is not an original randomizable container.");
                    var table =
                            ResourceKey.create(
                                    Registries.LOOT_TABLE, Identifier.parse(query.lootTable()));
                    if (server.reloadableRegistries()
                            .lookup()
                            .lookupOrThrow(Registries.LOOT_TABLE)
                            .get(table)
                            .isEmpty())
                        throw new IllegalArgumentException(
                                "The recorded loot table is not available in this original"
                                        + " runtime.");
                    entity.setLevel(level);
                    random.setLootTable(table, lootSeed);
                    check(cancelled);
                    random.unpackLootTable(null);
                    var items = new ArrayList<LootItem>();
                    var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
                    for (int slot = 0; slot < container.getContainerSize(); slot++) {
                        check(cancelled);
                        var stack = container.getItem(slot);
                        if (stack.isEmpty()) continue;
                        var encoded =
                                ItemStack.CODEC
                                        .encodeStart(ops, stack)
                                        .getOrThrow()
                                        .getAsJsonObject();
                        var components = new TreeMap<String, JsonElement>();
                        if (encoded.has("components"))
                            encoded.getAsJsonObject("components")
                                    .entrySet()
                                    .forEach(
                                            entry ->
                                                    components.put(
                                                            entry.getKey(), entry.getValue()));
                        items.add(
                                new LootItem(
                                        slot,
                                        BuiltInRegistries.ITEM.getKey(stack.getItem()).toString(),
                                        stack.getCount(),
                                        components));
                    }
                    return new ContainerEntry(
                            query.id(),
                            query.x(),
                            query.y(),
                            query.z(),
                            "generated",
                            container.getContainerSize(),
                            query.lootTable(),
                            query.lootSeed(),
                            items);
                });
    }

    /**
     * Runs the unmodified new-world spawn initialization, including PlayerSpawnFinder's actual
     * chunk checks.
     */
    static Position spawn(Path root, SpawnQuery query, AtomicBoolean cancelled) throws Exception {
        return inWorld(
                root,
                query.world(),
                query.seed(),
                true,
                cancelled,
                server -> {
                    var spawn = server.getWorldData().overworldData().getRespawnData().pos();
                    return new Position(
                            "spawn",
                            "spawn",
                            spawn.getX(),
                            spawn.getY(),
                            spawn.getZ(),
                            null,
                            0,
                            null);
                });
    }

    static <T> T inWorld(
            Path root,
            World world,
            String seedText,
            boolean initializeSpawn,
            AtomicBoolean cancelled,
            Function<VanillaRegion, T> read)
            throws Exception {
        long seed = Long.parseLong(seedText);
        check(cancelled);
        root = root.toAbsolutePath().normalize();
        Files.createDirectories(root);
        Path directory = Files.createTempDirectory(root, "request-");
        T result;
        try (var storage = LevelStorageSource.createDefault(directory).createAccess("preview")) {
            var packs = ServerPacksSource.createVanillaTrustedRepository();
            {
                packs.reload();
                var settings =
                        new LevelSettings(
                                "Rodel bounded seed preview",
                                GameType.CREATIVE,
                                LevelSettings.DifficultySettings.DEFAULT,
                                true,
                                WorldDataConfiguration.DEFAULT);
                var init =
                        new WorldLoader.InitConfig(
                                new WorldLoader.PackConfig(
                                        packs, WorldDataConfiguration.DEFAULT, false, true),
                                Commands.CommandSelection.DEDICATED,
                                LevelBasedPermissionSet.OWNER);
                try (var stem =
                        Util.blockUntilDone(
                                        executor ->
                                                WorldLoader.load(
                                                        init,
                                                        context -> {
                                                            Registry<LevelStem> empty =
                                                                    new MappedRegistry<LevelStem>(
                                                                                    Registries
                                                                                            .LEVEL_STEM,
                                                                                    Lifecycle
                                                                                            .stable())
                                                                            .freeze();
                                                            var preset =
                                                                    switch (world.preset()) {
                                                                        case "large_biomes" ->
                                                                                WorldPresets
                                                                                        .LARGE_BIOMES;
                                                                        case "amplified" ->
                                                                                WorldPresets
                                                                                        .AMPLIFIED;
                                                                        default ->
                                                                                WorldPresets.NORMAL;
                                                                    };
                                                            var registries =
                                                                    context
                                                                            .datapackWorldRegistries();
                                                            var dimensions =
                                                                    registries
                                                                            .lookupOrThrow(
                                                                                    Registries
                                                                                            .WORLD_PRESET)
                                                                            .getOrThrow(preset)
                                                                            .value()
                                                                            .createWorldDimensions();
                                                            if ("single_biome"
                                                                    .equals(world.preset())) {
                                                                var biome =
                                                                        registries
                                                                                .lookupOrThrow(
                                                                                        Registries
                                                                                                .BIOME)
                                                                                .getOrThrow(
                                                                                        ResourceKey
                                                                                                .create(
                                                                                                        Registries
                                                                                                                .BIOME,
                                                                                                        Identifier
                                                                                                                .withDefaultNamespace(
                                                                                                                        world
                                                                                                                                .biome())));
                                                                dimensions =
                                                                        dimensions
                                                                                .replaceOverworldGenerator(
                                                                                        registries,
                                                                                        new NoiseBasedChunkGenerator(
                                                                                                new FixedBiomeSource(
                                                                                                        biome),
                                                                                                registries
                                                                                                        .lookupOrThrow(
                                                                                                                Registries
                                                                                                                        .NOISE_SETTINGS)
                                                                                                        .getOrThrow(
                                                                                                                NoiseGeneratorSettings
                                                                                                                        .OVERWORLD)));
                                                            }
                                                            var baked = dimensions.bake(empty);
                                                            var data =
                                                                    new PrimaryLevelData(
                                                                            settings,
                                                                            baked
                                                                                    .specialWorldProperty(),
                                                                            baked.lifecycle());
                                                            data.setInitialized(!initializeSpawn);
                                                            if (!initializeSpawn)
                                                                data.setSpawn(
                                                                        LevelData.RespawnData.of(
                                                                                Level.OVERWORLD,
                                                                                new BlockPos(
                                                                                        0, 80, 0),
                                                                                0,
                                                                                0));
                                                            return new WorldLoader.DataLoadOutput<>(
                                                                    new LevelDataAndDimensions
                                                                            .WorldDataAndGenSettings(
                                                                            data,
                                                                            new WorldGenSettings(
                                                                                    new WorldOptions(
                                                                                            seed,
                                                                                            true,
                                                                                            false),
                                                                                    dimensions)),
                                                                    baked
                                                                            .dimensionsRegistryAccess());
                                                        },
                                                        WorldStem::new,
                                                        Util.backgroundExecutor(),
                                                        executor))
                                .get()) {
                    check(cancelled);
                    var server = new VanillaRegion(storage, packs, stem);
                    try {
                        server.initServer();
                        check(cancelled);
                        result = read.apply(server);
                    } finally {
                        server.stopServer();
                    }
                }
            }
        } finally {
            // Only the freshly allocated, internally owned request directory can be removed.
            if (!directory.normalize().startsWith(root) || directory.equals(root))
                throw new IllegalStateException("Invalid preview cleanup scope.");
            try (var files = Files.walk(directory)) {
                for (var path : files.sorted(Comparator.reverseOrder()).toList())
                    Files.deleteIfExists(path);
            }
        }
        check(cancelled);
        return result;
    }

    private RegionResult read(
            RegionQuery query, AtomicBoolean cancelled, Consumer<RegionProgress> progress) {
        var b = query.bounds();
        var level =
                getLevel(
                        ResourceKey.create(
                                Registries.DIMENSION,
                                Identifier.withDefaultNamespace(query.world().dimension())));
        if (b.minY() < level.getMinY() || b.maxY() >= level.getMinY() + level.getHeight())
            throw new IllegalArgumentException("Region Y is outside this dimension.");
        int minX = Math.floorDiv(b.minX(), 16),
                minZ = Math.floorDiv(b.minZ(), 16),
                maxX = Math.floorDiv(b.maxX(), 16),
                maxZ = Math.floorDiv(b.maxZ(), 16);
        // Fixed request-local order prevents task scheduling from deciding FEATURES overwrite
        // order.
        // Different requested extents may still change decoration; the result records this
        // seed-preview policy.
        for (int stage = 0; stage < 2; stage++) {
            int halo = stage == 0 ? 2 : 1,
                    total = (maxX - minX + 1 + halo * 2) * (maxZ - minZ + 1 + halo * 2),
                    done = 0;
            String phase = stage == 0 ? "terrain" : "features";
            progress.accept(new RegionProgress(phase, 0, total));
            for (int z = minZ - halo; z <= maxZ + halo; z++)
                for (int x = minX - halo; x <= maxX + halo; x++) {
                    check(cancelled);
                    level.getChunkSource()
                            .getChunk(
                                    x,
                                    z,
                                    stage == 0 ? ChunkStatus.TERRAIN : ChunkStatus.FEATURES,
                                    true);
                    progress.accept(new RegionProgress(phase, ++done, total));
                }
        }
        var chunks = new ArrayList<LevelChunk>();
        int total = (maxX - minX + 1) * (maxZ - minZ + 1);
        progress.accept(new RegionProgress("full", 0, total));
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++) {
                check(cancelled);
                chunks.add(
                        (LevelChunk) level.getChunkSource().getChunk(x, z, ChunkStatus.FULL, true));
                progress.accept(new RegionProgress("full", chunks.size(), total));
            }
        var palette = new ArrayList<BlockStateEntry>();
        var indices = new IdentityHashMap<BlockState, Integer>();
        var counts = new TreeMap<String, Long>();
        var containers = new ArrayList<ContainerEntry>();
        ByteBuffer binary =
                query.includeBlocks()
                        ? ByteBuffer.allocate(Math.toIntExact(b.volume() * 2))
                                .order(ByteOrder.LITTLE_ENDIAN)
                        : null;
        int width = b.maxX() - b.minX() + 1, depth = b.maxZ() - b.minZ() + 1, done = 0;
        var pos = new BlockPos.MutableBlockPos();
        var ops = RegistryOps.create(JsonOps.INSTANCE, registryAccess());
        progress.accept(new RegionProgress("scan", 0, total));
        for (var chunk : chunks) {
            check(cancelled);
            int left = Math.max(b.minX(), chunk.getPos().getMinBlockX()),
                    right = Math.min(b.maxX(), chunk.getPos().getMaxBlockX());
            int top = Math.max(b.minZ(), chunk.getPos().getMinBlockZ()),
                    bottom = Math.min(b.maxZ(), chunk.getPos().getMaxBlockZ());
            boolean wholeColumn =
                    binary == null
                            && left == chunk.getPos().getMinBlockX()
                            && right == chunk.getPos().getMaxBlockX()
                            && top == chunk.getPos().getMinBlockZ()
                            && bottom == chunk.getPos().getMaxBlockZ();
            var countedSections = new HashSet<Integer>();
            if (wholeColumn)
                for (int section = 0; section < chunk.getSections().length; section++) {
                    int y = level.getMinY() + section * 16;
                    if (y >= b.minY() && y + 15 <= b.maxY()) {
                        check(cancelled);
                        chunk.getSections()[section]
                                .getStates()
                                .count(
                                        (state, amount) ->
                                                counts.merge(
                                                        BuiltInRegistries.BLOCK
                                                                .getKey(state.getBlock())
                                                                .toString(),
                                                        (long) amount,
                                                        Long::sum));
                        countedSections.add(Math.floorDiv(y, 16));
                    }
                }
            for (int y = b.minY(); y <= b.maxY(); y++) {
                if (countedSections.contains(Math.floorDiv(y, 16))) continue;
                for (int z = top; z <= bottom; z++) {
                    check(cancelled);
                    for (int x = left; x <= right; x++) {
                        var state = chunk.getBlockState(pos.set(x, y, z));
                        String id = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
                        counts.merge(id, 1L, Long::sum);
                        if (binary != null) {
                            int index =
                                    indices.computeIfAbsent(
                                            state,
                                            value -> {
                                                if (palette.size() >= 65_536)
                                                    throw new IllegalStateException(
                                                            "Region state palette is too large.");
                                                var properties = new TreeMap<String, String>();
                                                value.getProperties()
                                                        .forEach(
                                                                property ->
                                                                        properties.put(
                                                                                property.getName(),
                                                                                valueName(
                                                                                        property,
                                                                                        value)));
                                                palette.add(new BlockStateEntry(id, properties));
                                                return palette.size() - 1;
                                            });
                            binary.putShort(
                                    (((y - b.minY()) * depth + z - b.minZ()) * width + x - b.minX())
                                            * 2,
                                    (short) index);
                        }
                    }
                }
            }
            for (var entry : chunk.getBlockEntities().entrySet()) {
                check(cancelled);
                var position = entry.getKey();
                if (!b.contains(position.getX(), position.getY(), position.getZ())) continue;
                var entity = entry.getValue();
                String id =
                        BuiltInRegistries.BLOCK
                                .getKey(entity.getBlockState().getBlock())
                                .toString();
                if (entity instanceof VaultBlockEntity) {
                    containers.add(
                            new ContainerEntry(
                                    id,
                                    position.getX(),
                                    position.getY(),
                                    position.getZ(),
                                    "dynamic_reward",
                                    0,
                                    null,
                                    null,
                                    List.of()));
                } else if (entity instanceof Container container) {
                    String table = null, lootSeed = null, status = "generated";
                    if (entity instanceof RandomizableContainer random
                            && random.getLootTable() != null) {
                        table = random.getLootTable().identifier().toString();
                        lootSeed = Long.toString(random.getLootTableSeed());
                        if (query.includeLoot()) random.unpackLootTable(null);
                        else status = "unevaluated";
                    }
                    var items = new ArrayList<LootItem>();
                    if (!status.equals("unevaluated"))
                        for (int slot = 0; slot < container.getContainerSize(); slot++) {
                            var stack = container.getItem(slot);
                            if (stack.isEmpty()) continue;
                            var encoded =
                                    ItemStack.CODEC
                                            .encodeStart(ops, stack)
                                            .getOrThrow()
                                            .getAsJsonObject();
                            var components = new TreeMap<String, JsonElement>();
                            if (encoded.has("components"))
                                encoded.getAsJsonObject("components")
                                        .entrySet()
                                        .forEach(
                                                component ->
                                                        components.put(
                                                                component.getKey(),
                                                                component.getValue()));
                            items.add(
                                    new LootItem(
                                            slot,
                                            BuiltInRegistries.ITEM
                                                    .getKey(stack.getItem())
                                                    .toString(),
                                            stack.getCount(),
                                            components));
                        }
                    containers.add(
                            new ContainerEntry(
                                    id,
                                    position.getX(),
                                    position.getY(),
                                    position.getZ(),
                                    status,
                                    container.getContainerSize(),
                                    table,
                                    lootSeed,
                                    items));
                }
            }
            progress.accept(new RegionProgress("scan", ++done, total));
        }
        containers.sort(
                Comparator.comparingInt(ContainerEntry::y)
                        .thenComparingInt(ContainerEntry::x)
                        .thenComparingInt(ContainerEntry::z));
        var tints =
                binary == null
                        ? new RegionTints(List.of(), "")
                        : VanillaTint.generated(level, b, cancelled);
        return new RegionResult(
                query.seed(),
                query.world(),
                b,
                "fixed_region_v1",
                palette,
                binary == null ? "" : Base64.getEncoder().encodeToString(binary.array()),
                counts.entrySet().stream()
                        .map(entry -> new BlockCount(entry.getKey(), entry.getValue()))
                        .toList(),
                containers,
                0,
                tints.tintPalette(),
                tints.tintIndices());
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    private static String valueName(
            net.minecraft.world.level.block.state.properties.Property property, BlockState state) {
        return property.getName(state.getValue(property));
    }
}
