package dev.richasy.worldreroll;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Function;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.registries.Registries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ChunkLevel;
import net.minecraft.server.level.ChunkResult;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.level.Ticket;
import net.minecraft.tags.BlockTags;
import net.minecraft.tags.StructureTags;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.TicketStorage;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.chunk.ChunkAccess;
import net.minecraft.world.level.chunk.ChunkGeneratorStructureState;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.level.levelgen.structure.Structure;
import net.minecraft.world.level.levelgen.structure.placement.ConcentricRingsStructurePlacement;
import net.minecraft.world.level.levelgen.structure.placement.RandomSpreadStructurePlacement;
import net.minecraft.world.level.levelgen.structure.placement.StructurePlacement;
import net.minecraft.world.level.storage.LevelData;

/** Bounded village preparation shared by all loaders; mutable world state stays on its server thread. */
public final class VillageSpawn {
    private static final int SEARCH_RADIUS = 100;
    private static final int MAX_REQUESTS = 2;
    private static final Map<MinecraftServer, Search> ACTIVE = new ConcurrentHashMap<>();
    private static final ExecutorService REQUESTS = Executors.newSingleThreadExecutor(task -> {
        Thread thread = new Thread(task, "WorldReroll-ChunkRequests");
        thread.setDaemon(true);
        return thread;
    });

    private VillageSpawn() {}

    /** Prepares a village neighborhood before moving the local player, world spawn and enabled bonus chest. */
    public static CompletableFuture<BlockPos> placeAsync(MinecraftServer server, UUID playerId) {
        return submit(server, result -> {
            var player = server.getPlayerList().getPlayer(playerId);
            if (server.isDedicatedServer() || server.isPublished() || player == null || player.level() != server.overworld()) {
                throw new IllegalStateException("Village placement requires the local overworld player");
            }
            return new Search(server.overworld(), player.blockPosition(), player, result);
        });
    }

    /** Prepares a safe position and its nine FULL chunks without moving a player or world spawn. */
    public static CompletableFuture<BlockPos> findSpawnAsync(ServerLevel level, BlockPos origin) {
        return submit(level.getServer(), result -> new Search(level, origin.immutable(), null, result));
    }

    /** Cancels this preparation and releases its tickets, including a completed teleport's short handoff lease. */
    public static void cancel(CompletableFuture<BlockPos> result) {
        if (result == null || result.cancel(false)) return;
        ACTIVE.forEach((server, search) -> {
            if (search.result == result) server.execute(search::close);
        });
    }

    /** Protects only the player owned by a live preparation, without changing saved invulnerability flags. */
    public static boolean isPreparing(ServerPlayer player) {
        Search search = ACTIVE.get(player.level().getServer());
        return search != null && search.player == player && !search.result.isCancelled();
    }

    private static CompletableFuture<BlockPos> submit(MinecraftServer server, Function<CompletableFuture<BlockPos>, Search> factory) {
        CompletableFuture<BlockPos> result = new CompletableFuture<>();
        try {
            if (server.isShutdown()) throw new IllegalStateException("Server is shutting down");
            server.execute(() -> {
                try {
                    if (server.isShutdown()) throw new IllegalStateException("Server is shutting down");
                    if (!result.isDone()) start(factory.apply(result));
                } catch (Exception error) {
                    result.completeExceptionally(error);
                }
            });
        } catch (Exception error) {
            result.completeExceptionally(error);
        }
        return result;
    }

    private static void start(Search search) {
        if (ACTIVE.putIfAbsent(search.server, search) != null) {
            search.result.completeExceptionally(new IllegalStateException("A village search is already active"));
        } else {
            search.result.whenComplete((ignored, error) -> {
                // A failure dialog can pause world ticks; cancellation must still release tickets immediately.
                if (search.result.isCancelled()) search.server.execute(search::close);
            });
        }
    }

    /** Advances only this server's bounded request pipeline from its native tick callback. */
    public static void tick(MinecraftServer server) {
        Search search = ACTIVE.get(server);
        if (search == null) return;
        try {
            search.tick();
        } catch (Exception error) {
            search.close();
            search.result.completeExceptionally(error);
        }
    }

    /** Releases an active preparation before its owning server closes its chunk storage. */
    public static void stop(MinecraftServer server) {
        Search search = ACTIVE.get(server);
        if (search != null) {
            search.close();
            search.result.cancel(false);
        }
    }

    private static void place(ServerLevel level, ServerPlayer player, BlockPos spawn) {
        if (level.getServer().getWorldGenSettings().options().generateBonusChest()) {
            moveBonusChest(level, level.getRespawnData().globalPos().pos(), spawn);
        }
        if (!player.teleportTo(level, spawn.getX() + 0.5, spawn.getY(), spawn.getZ() + 0.5,
                Set.of(), player.getYRot(), 0, true)) {
            throw new IllegalStateException("Could not move player to village spawn");
        }
        player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
        player.fallDistance = 0;
        level.setRespawnData(LevelData.RespawnData.of(level.dimension(), spawn, 0, 0));
    }

    private enum Phase { INITIALIZE, STRUCTURES, GROUND, NEARBY, HANDOFF }

    private record Candidate(ChunkPos position, StructurePlacement placement, List<Structure> structures) {}
    private record TicketKey(ChunkPos position, ChunkStatus status) {}

    private static final class Search {
        final ServerLevel level;
        final MinecraftServer server;
        final BlockPos origin;
        final ServerPlayer player;
        final CompletableFuture<BlockPos> result;
        final TicketStorage tickets;
        final Map<TicketKey, Ticket> ownedTickets = new LinkedHashMap<>();
        final Map<StructurePlacement, List<Structure>> placements = new LinkedHashMap<>();
        final Map<ConcentricRingsStructurePlacement, CompletableFuture<List<ChunkPos>>> rings = new LinkedHashMap<>();
        final Map<Candidate, CompletableFuture<ChunkResult<ChunkAccess>>> searching = new LinkedHashMap<>();
        final Map<ChunkPos, CompletableFuture<ChunkResult<ChunkAccess>>> fullChunks = new LinkedHashMap<>();
        final long started = System.nanoTime();
        ChunkGeneratorStructureState state;
        Phase phase = Phase.INITIALIZE;
        List<Candidate> candidates = List.of();
        List<BlockPos> ground = List.of();
        List<ChunkPos> nearby = List.of();
        int candidateIndex;
        int groundIndex;
        int radius = -1;
        int checked;
        int handoffTicks;
        boolean ringsCollected;
        boolean foundInRing;
        BlockPos village;
        BlockPos spawn;

        Search(ServerLevel level, BlockPos origin, ServerPlayer player, CompletableFuture<BlockPos> result) {
            this.level = level;
            server = level.getServer();
            this.origin = origin.immutable();
            this.player = player;
            this.result = result;
            tickets = level.getDataStorage().computeIfAbsent(TicketStorage.TYPE);
        }

        void tick() {
            if (phase == Phase.HANDOFF) {
                if (--handoffTicks <= 0) close();
                return;
            }
            if (result.isDone()) {
                close();
                return;
            }
            if (player != null && (server.isPublished() || player.level() != level
                    || server.getPlayerList().getPlayer(player.getUUID()) != player)) {
                throw new IllegalStateException("Local player or world changed during village preparation");
            }
            switch (phase) {
                case INITIALIZE -> initialize();
                case STRUCTURES -> structures();
                case GROUND -> ground();
                case NEARBY -> nearby();
                case HANDOFF -> throw new IllegalStateException("Unexpected handoff");
            }
        }

        void initialize() {
            if (!server.getWorldGenSettings().options().generateStructures()) {
                throw new IllegalStateException("Village preference requires structure generation");
            }
            state = level.getChunkSource().getGeneratorState();
            var villages = level.registryAccess().lookupOrThrow(Registries.STRUCTURE).get(StructureTags.VILLAGE)
                    .orElseThrow(() -> new IllegalStateException("No village structure tag"));
            for (var structure : villages) {
                for (var placement : state.getPlacementsForStructure(structure)) {
                    if (placement instanceof RandomSpreadStructurePlacement || placement instanceof ConcentricRingsStructurePlacement) {
                        placements.computeIfAbsent(placement, ignored -> new ArrayList<>()).add(structure.value());
                    }
                }
            }
            if (placements.isEmpty()) throw new IllegalStateException("No village placements in this generator");
            for (var placement : placements.keySet()) {
                if (placement instanceof ConcentricRingsStructurePlacement concentric) {
                    // Native placement maps are initialized here; only wait for their cached position futures off-thread.
                    rings.put(concentric, CompletableFuture.supplyAsync(() -> state.getRingPositionsFor(concentric), REQUESTS));
                }
            }
            phase = Phase.STRUCTURES;
        }

        void structures() {
            if (!ringsCollected) {
                if (rings.values().stream().anyMatch(future -> !future.isDone())) return;
                List<Candidate> first = new ArrayList<>();
                for (var entry : rings.entrySet()) {
                    var positions = entry.getValue().join();
                    if (positions != null) for (var position : positions) addCandidate(first, position, entry.getKey());
                }
                setCandidates(first);
                ringsCollected = true;
            }
            var iterator = searching.entrySet().iterator();
            while (iterator.hasNext()) {
                var entry = iterator.next();
                if (!entry.getValue().isDone()) continue;
                var chunk = completed(entry.getValue());
                Candidate candidate = entry.getKey();
                for (var structure : candidate.structures()) {
                    var start = chunk.getStartForStructure(structure);
                    if (start == null || !start.isValid()) continue;
                    BlockPos position = candidate.placement().getLocatePos(start.getChunkPos());
                    if (village == null || position.distSqr(origin) < village.distSqr(origin)) village = position;
                    foundInRing = true;
                }
                checked++;
                iterator.remove();
            }
            if (foundInRing) candidateIndex = candidates.size();
            if (searching.isEmpty() && candidateIndex == candidates.size()) {
                releaseStructureTickets();
                boolean random = placements.keySet().stream().anyMatch(RandomSpreadStructurePlacement.class::isInstance);
                if ((radius >= 0 && foundInRing) || radius == SEARCH_RADIUS || !random) {
                    if (village == null) throw new IllegalStateException("No village found within the bounded search");
                    List<BlockPos> positions = new ArrayList<>();
                    for (int r = 8; r <= 64; r += 4) {
                        for (int x = -r; x <= r; x += 4) {
                            for (int z = -r; z <= r; z += 4) {
                                if (Math.abs(x) == r || Math.abs(z) == r) positions.add(village.offset(x, 0, z));
                            }
                        }
                    }
                    ground = positions;
                    phase = Phase.GROUND;
                    return;
                }
                radius++;
                foundInRing = false;
                List<Candidate> next = new ArrayList<>();
                ChunkPos center = ChunkPos.containing(origin);
                for (var placement : placements.keySet()) {
                    if (!(placement instanceof RandomSpreadStructurePlacement spread)) continue;
                    for (int x = -radius; x <= radius; x++) {
                        for (int z = -radius; z <= radius; z++) {
                            if (Math.abs(x) != radius && Math.abs(z) != radius) continue;
                            ChunkPos position = spread.getPotentialStructureChunk(state.getLevelSeed(),
                                    center.x() + x * spread.spacing(), center.z() + z * spread.spacing());
                            addCandidate(next, position, spread);
                        }
                    }
                }
                setCandidates(next);
            }
            while (searching.size() < MAX_REQUESTS && candidateIndex < candidates.size()) {
                Candidate candidate = candidates.get(candidateIndex++);
                searching.put(candidate, request(candidate.position(), ChunkStatus.STRUCTURE_STARTS));
            }
        }

        void addCandidate(List<Candidate> values, ChunkPos position, StructurePlacement placement) {
            if (level.getWorldBorder().isWithinBounds(placement.getLocatePos(position))) {
                values.add(new Candidate(position, placement, placements.get(placement)));
            }
        }

        void setCandidates(List<Candidate> values) {
            values.sort(Comparator.comparingDouble(candidate -> candidate.placement().getLocatePos(candidate.position()).distSqr(origin)));
            candidates = values;
            candidateIndex = 0;
        }

        void ground() {
            for (int i = groundIndex; i < Math.min(groundIndex + 32, ground.size()); i++) ensureFull(ChunkPos.containing(ground.get(i)));
            for (int budget = 0; budget < 32 && groundIndex < ground.size(); budget++) {
                BlockPos position = ground.get(groundIndex);
                var future = ensureFull(ChunkPos.containing(position));
                if (future == null || !future.isDone()) return;
                var chunk = completed(future);
                BlockPos feet = safeFeet(level, chunk, position.getX(), position.getZ());
                groundIndex++;
                if (feet == null) continue;
                spawn = feet;
                ChunkPos center = ChunkPos.containing(spawn);
                List<ChunkPos> positions = new ArrayList<>();
                for (int x = -1; x <= 1; x++) {
                    for (int z = -1; z <= 1; z++) positions.add(new ChunkPos(center.x() + x, center.z() + z));
                }
                if (player != null && server.getWorldGenSettings().options().generateBonusChest()) {
                    ChunkPos originalSpawn = ChunkPos.containing(level.getRespawnData().globalPos().pos());
                    if (!positions.contains(originalSpawn)) positions.add(originalSpawn);
                }
                nearby = positions;
                phase = Phase.NEARBY;
                return;
            }
            if (groundIndex == ground.size()) throw new IllegalStateException("No safe ground found near village at " + village);
        }

        void nearby() {
            boolean ready = true;
            for (var position : nearby) {
                var future = ensureFull(position);
                if (future == null || !future.isDone()) ready = false;
                else completed(future);
            }
            if (!ready) return;
            if (!safeGround(level, spawn)) {
                phase = Phase.GROUND;
                return;
            }
            if (player != null) place(level, player, spawn);
            // Let vanilla observe the teleport and install player-view tickets before releasing our lease.
            phase = Phase.HANDOFF;
            handoffTicks = 2;
            org.slf4j.LoggerFactory.getLogger("worldreroll").info(
                    "Village preparation finished: candidates={}, fullChunks={}, elapsedMs={}",
                    checked, fullChunks.size(), (System.nanoTime() - started) / 1_000_000);
            result.complete(spawn);
        }

        CompletableFuture<ChunkResult<ChunkAccess>> ensureFull(ChunkPos position) {
            var existing = fullChunks.get(position);
            if (existing != null) return existing;
            if (fullChunks.values().stream().filter(future -> !future.isDone()).count() >= MAX_REQUESTS) return null;
            var future = request(position, ChunkStatus.FULL);
            fullChunks.put(position, future);
            return future;
        }

        CompletableFuture<ChunkResult<ChunkAccess>> request(ChunkPos position, ChunkStatus status) {
            TicketKey key = new TicketKey(position, status);
            if (!ownedTickets.containsKey(key)) {
                Ticket ticket = new Ticket(VillageTickets.PREPARATION, ChunkLevel.byStatus(status));
                tickets.addTicket(ticket, position);
                ownedTickets.put(key, ticket);
            }
            var chunks = level.getChunkSource();
            // The server-thread branch of this public API waits; its off-thread branch only queues the request.
            return CompletableFuture.supplyAsync(() -> chunks.getChunkFuture(position.x(), position.z(), status, true), REQUESTS)
                    .thenCompose(future -> future);
        }

        ChunkAccess completed(CompletableFuture<ChunkResult<ChunkAccess>> future) {
            var chunk = future.join();
            return chunk.orElseThrow(() -> new IllegalStateException("Chunk preparation failed: " + chunk.getError()));
        }

        void releaseStructureTickets() {
            var iterator = ownedTickets.entrySet().iterator();
            while (iterator.hasNext()) {
                var entry = iterator.next();
                if (entry.getKey().status() == ChunkStatus.STRUCTURE_STARTS) {
                    tickets.removeTicket(entry.getValue(), entry.getKey().position());
                    iterator.remove();
                }
            }
        }

        void close() {
            ownedTickets.forEach((key, ticket) -> tickets.removeTicket(ticket, key.position()));
            ownedTickets.clear();
            ACTIVE.remove(server, this);
        }
    }

    private static void moveBonusChest(ServerLevel level, BlockPos previousSpawn, BlockPos spawn) {
        var loot = net.minecraft.world.level.storage.loot.BuiltInLootTables.SPAWN_BONUS_CHEST;
        // Vanilla places its bonus chest in the original spawn chunk. Identify it
        // by its unopened loot table, never by merely being a nearby chest.
        var source = level.getChunkAt(previousSpawn).getBlockEntities().values().stream()
                .filter(entity -> entity instanceof net.minecraft.world.level.block.entity.ChestBlockEntity chest
                        && loot.equals(chest.getLootTable()))
                .map(entity -> (net.minecraft.world.level.block.entity.ChestBlockEntity) entity)
                .findFirst().orElse(null);
        BlockPos destination = null;
        for (int radius = 2; radius <= 8 && destination == null; radius++) {
            for (int x = -radius; x <= radius && destination == null; x++) {
                for (int z = -radius; z <= radius; z++) {
                    if (Math.abs(x) != radius && Math.abs(z) != radius) continue;
                    int px = spawn.getX() + x, pz = spawn.getZ() + z;
                    var chunk = level.getChunk(Math.floorDiv(px, 16), Math.floorDiv(pz, 16));
                    BlockPos pos = new BlockPos(px, chunk.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES,
                            px & 15, pz & 15) + 1, pz);
                    // A chest replaces the snow layer, as native placement does, rather than floating above it.
                    if (level.getBlockState(pos.below()).is(Blocks.SNOW)) pos = pos.below();
                    if (Math.abs(pos.getY() - spawn.getY()) <= 2 && level.getWorldBorder().isWithinBounds(pos)
                            && (level.getBlockState(pos).isAir() || level.getBlockState(pos).is(Blocks.SNOW))
                            && level.getBlockState(pos.above()).isAir()
                            && supportsStanding(level, pos.below())) {
                        destination = pos;
                        break;
                    }
                }
            }
        }
        if (destination == null) throw new IllegalStateException("No safe bonus chest position near " + spawn);
        if (!level.setBlock(destination, Blocks.CHEST.defaultBlockState(), 3)
                || !(level.getBlockEntity(destination) instanceof net.minecraft.world.level.block.entity.ChestBlockEntity chest)) {
            throw new IllegalStateException("Could not place bonus chest at " + destination);
        }
        chest.setLootTable(loot);
        chest.setLootTableSeed(source == null ? level.getRandom().nextLong() : source.getLootTableSeed());
        chest.setChanged();
        if (source != null) {
            level.removeBlock(source.getBlockPos(), false);
        } else {
            org.slf4j.LoggerFactory.getLogger("worldreroll").warn(
                    "Vanilla bonus chest absent from original spawn chunk {}; generated one at final spawn {}",
                    previousSpawn, destination);
        }
        for (Direction direction : Direction.Plane.HORIZONTAL) {
            BlockPos torch = destination.relative(direction);
            var state = Blocks.TORCH.defaultBlockState();
            if (level.getBlockState(torch).isAir() && state.canSurvive(level, torch)) level.setBlock(torch, state, 3);
        }
    }

    static BlockPos safeFeet(ServerLevel level, ChunkAccess chunk, int x, int z) {
        BlockPos feet = new BlockPos(x, chunk.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, x & 15, z & 15) + 1, z);
        if (level.getBlockState(feet).is(Blocks.SNOW)) feet = feet.above();
        return safeGround(level, feet) ? feet : null;
    }

    private static boolean safeGround(ServerLevel level, BlockPos feet) {
        if (feet.getY() <= level.getMinY() || feet.getY() >= level.getMaxY()) return false;
        BlockPos support = feet.below();
        // Retain snow layers and check their underlying terrain; the player may settle by less than one block.
        if (level.getBlockState(support).is(Blocks.SNOW)) support = support.below();
        var ground = level.getBlockState(support);
        boolean terrain = ground.is(BlockTags.DIRT) || ground.is(BlockTags.SAND)
                || ground.is(BlockTags.BASE_STONE_OVERWORLD) || ground.is(Blocks.GRAVEL)
                || ground.is(Blocks.SNOW_BLOCK) || ground.is(Blocks.DIRT_PATH);
        return terrain && level.getWorldBorder().isWithinBounds(feet)
                && supportsStanding(level, support) && level.getBlockState(feet).isAir()
                && level.getBlockState(feet.above()).isAir();
    }

    private static boolean supportsStanding(ServerLevel level, BlockPos position) {
        var state = level.getBlockState(position);
        // A path supports a player despite lacking a full-block UP face.
        return state.getFluidState().isEmpty()
                && (state.is(Blocks.DIRT_PATH) || state.isFaceSturdy(level, position, Direction.UP));
    }
}
