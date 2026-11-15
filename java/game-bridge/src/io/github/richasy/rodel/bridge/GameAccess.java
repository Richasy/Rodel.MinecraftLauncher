package io.github.richasy.rodel.bridge;

import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.LongSupplier;

/** Fixed game operations. Minecraft access is scheduled on its own executors, never the socket thread. */
final class GameAccess {
    record World(Object client, Object level, Object player, Object server, Object connection,
                 UUID playerId, String epoch, String dimension, boolean local, String localEpoch) { }
    record Edit(String operationId, String incarnation, String epoch, String dimension,
                String kind, String dayTime, Float health, Map<String, Object> payload) {
        static Edit read(Map<String, Object> value) {
            Edit edit = new Edit(Json.string(value, "operationId"), Json.string(value, "gameIncarnation"), Json.string(value, "worldEpoch"),
                Json.string(value, "dimension"), Json.string(value, "kind"), value.get("dayTime") instanceof String time ? time : null,
                value.get("health") instanceof Number health ? health.floatValue() : null,
                Json.object("command", value.get("command"), "control", value.get("control"), "click", value.get("click"), "button", value.get("button")));
            if (!edit.operationId.matches("[0-9a-f]{32}") || !edit.incarnation.matches("[0-9a-f]{32}")
                || !edit.epoch.matches("[1-9][0-9]{0,18}") || edit.dimension.isBlank() || edit.dimension.length() > 256)
                throw new IllegalArgumentException("INVALID_PARAMS");
            if (edit.kind.equals("world.setTime")) {
                if (edit.payload.values().stream().anyMatch(Objects::nonNull) || edit.health != null || edit.dayTime == null || !edit.dayTime.matches("0|[1-9][0-9]{0,18}") || Long.parseLong(edit.dayTime) < 0) throw new IllegalArgumentException("INVALID_PARAMS");
            } else if (edit.kind.equals("player.setHealth")) {
                if (edit.payload.values().stream().anyMatch(Objects::nonNull) || edit.dayTime != null || edit.health == null || !Float.isFinite(edit.health) || edit.health < 1 || edit.health > 20) throw new IllegalArgumentException("INVALID_PARAMS");
            } else GamePlay.validateEdit(edit.kind, value);
            return edit;
        }
    }
    static final class Operation {
        final Edit edit;
        String state = "queued", error;
        String time, thread;
        Float health;
        Map<String, Object> result;
        Operation(Edit edit) { this.edit = edit; }
        synchronized Map<String, Object> receipt() {
            return Json.object("operationId", edit.operationId, "state", state, "errorCode", error,
                "dayTime", time, "health", health, "serverThread", thread, "result", result);
        }
        synchronized void reject(String code) { if (state.equals("queued")) { state = "rejected"; error = code; } }
    }

    private final Bindings bindings;
    private final String session, incarnation;
    private final Map<String, Operation> operations = new HashMap<>();
    private final GamePlay gameplay;
    private volatile World world;
    private long generation;
    private Object localServer;
    private long localGeneration;
    private final AtomicLong sequence = new AtomicLong();
    private final AtomicBoolean observationPending = new AtomicBoolean();
    private int idlePoll;
    private final LongSupplier regionClock;

    GameAccess(Bindings bindings, String session, String incarnation) {
        this(bindings, session, incarnation, System::nanoTime);
    }

    GameAccess(Bindings bindings, String session, String incarnation, LongSupplier regionClock) {
        this.bindings = bindings; this.session = session; this.incarnation = incarnation;
        this.regionClock = Objects.requireNonNull(regionClock);
        gameplay = new GamePlay(bindings);
        ScheduledExecutorService observer = Executors.newSingleThreadScheduledExecutor(Agent.threads("rodel-bridge-lifecycle"));
        observer.scheduleWithFixedDelay(this::observe, 0, 50, TimeUnit.MILLISECONDS);
    }

    private void observe() {
        if (!gameplay.hasInput() && ++idlePoll % 4 != 0) return;
        if (!observationPending.compareAndSet(false, true)) return;
        try {
            Object client = bindings.client();
            if (client == null) { observationPending.set(false); return; }
            Bindings.execute(client, () -> {
                try { gameplay.advance(current(client)); }
                catch (Throwable error) { world = null; }
                finally { observationPending.set(false); }
            });
        } catch (Throwable error) { observationPending.set(false); }
    }

    // Called only on the game client executor. A new level object always gets a new generation.
    private World current(Object client) throws Exception {
        Object level = bindings.field(client, "clientLevel"), player = bindings.field(client, "clientPlayer");
        if (level == null || player == null) { world = null; return null; }
        Object server = bindings.call(client, "clientServer");
        if (server != null && server != localServer) { localServer = server; localGeneration++; }
        Object listener = bindings.call(client, "clientConnection");
        Object connection = listener == null ? null : bindings.call(listener, "listenerConnection");
        boolean local = server != null && connection != null && (Boolean)bindings.call(connection, "connectionMemory")
            && (Boolean)bindings.call(client, "clientLocal");
        World previous = world;
        String epoch = previous != null && previous.level == level && previous.player == player && previous.connection == connection
            ? previous.epoch : Long.toString(++generation);
        World selected = new World(client, level, player, server, connection, (UUID)bindings.call(player, "entityUuid"), epoch, bindings.dimension(level), local, local ? Long.toString(localGeneration) : null);
        world = selected;
        return selected;
    }

    Map<String, Object> snapshot() throws Exception { return snapshotForAi(null); }

    Map<String, Object> snapshotForAi(Integer expectedWorld) throws Exception {
        Object client = bindings.client();
        if (client == null) return unavailable("GAME_CLIENT_NOT_LOADED");
        CompletableFuture<Map<String, Object>> completed = new CompletableFuture<>();
        Bindings.execute(client, () -> {
            if (completed.isDone()) return;
            try {
                World observed = current(client);
                if (expectedWorld != null && (observed == null || System.identityHashCode(observed.level) != expectedWorld)) {
                    completed.completeExceptionally(new IllegalArgumentException("GAME_WORLD_STALE")); return;
                }
                if (observed == null) { completed.complete(unavailable("GAME_WORLD_NOT_READY")); return; }
                double x = ((Number)bindings.call(observed.player, "entityX")).doubleValue(), y = ((Number)bindings.call(observed.player, "entityY")).doubleValue(), z = ((Number)bindings.call(observed.player, "entityZ")).doubleValue();
                float health = ((Number)bindings.call(observed.player, "entityHealth")).floatValue();
                if (!Double.isFinite(x) || !Double.isFinite(y) || !Double.isFinite(z) || !Float.isFinite(health)) {
                    completed.complete(unavailable("GAME_DATA_INVALID")); return;
                }
                completed.complete(Json.object("sessionId", session, "gameIncarnation", incarnation, "sequence", Long.toString(sequence.incrementAndGet()),
                    "sampledAt", Instant.now().toString(), "ready", true, "mode", observed.local ? "local_owned" : "remote_read_only",
                    "worldEpoch", observed.epoch, "localWorldEpoch", observed.localEpoch, "dimension", observed.dimension, "playerId", observed.playerId.toString(),
                    "x", x, "y", y, "z", z, "yaw", bindings.has("entityYaw") ? bindings.call(observed.player, "entityYaw") : null,
                    "health", health, "dayTime", Long.toString(bindings.time(observed.level)), "clientThread", Thread.currentThread().getName()));
            } catch (Throwable error) { bindings.report("snapshot", error); completed.complete(unavailable("GAME_BINDING_UNSUPPORTED")); }
        });
        try { return completed.get(10, TimeUnit.SECONDS); }
        finally { completed.cancel(false); }
    }

    private Map<String, Object> unavailable(String error) {
        return Json.object("sessionId", session, "gameIncarnation", incarnation, "sequence", Long.toString(sequence.incrementAndGet()), "sampledAt", Instant.now().toString(),
            "ready", false, "mode", "unavailable", "errorCode", error);
    }

    Map<String, Object> capabilities() {
        List<String> methods = new ArrayList<>(List.of("game.snapshot", "world.setTime", "player.setHealth", "game.operation.show", "game.operation.cancel"));
        if (bindings.regionApi()) methods.add("game.region");
        if (bindings.has("serverPublish") && bindings.has("serverPublished") && bindings.has("serverPort")) methods.add("world.publish");
        if (bindings.has("clientOptions")) methods.addAll(List.of("game.query", "player.control", "player.release", "inventory.click", "inventory.button", "screen.close", "player.respawn", "command.execute"));
        return Json.object("capabilities", methods);
    }

    Map<String, Object> edit(Map<String, Object> parameters) throws Exception {
        Edit edit = Edit.read(parameters);
        Operation operation;
        synchronized (operations) {
            Operation existing = operations.get(edit.operationId);
            if (existing != null) {
                if (!existing.edit.equals(edit)) throw new IllegalArgumentException("GAME_OPERATION_CONFLICT");
                return existing.receipt();
            }
            if (operations.size() >= 100000) throw new IllegalArgumentException("GAME_OPERATION_LIMIT");
            int pending = 0;
            for (Operation candidate : operations.values()) synchronized (candidate) {
                if (candidate.state.equals("queued") || candidate.state.equals("applying")) pending++;
            }
            if (pending >= 32) throw new IllegalArgumentException("GAME_BRIDGE_BUSY");
            operation = new Operation(edit); operations.put(edit.operationId, operation);
        }
        try {
            Object client = bindings.client();
            if (client == null) { operation.reject("GAME_WORLD_NOT_READY"); return operation.receipt(); }
            Bindings.execute(client, () -> prepare(client, operation));
        } catch (Throwable error) { operation.reject("GAME_BINDING_UNSUPPORTED"); }
        return operation.receipt();
    }

    private void prepare(Object client, Operation operation) {
        try {
            synchronized (operation) { if (!operation.state.equals("queued")) return; }
            World selected = current(client);
            String rejection = validate(selected, operation.edit);
            if (rejection != null) { operation.reject(rejection); return; }
            Bindings.execute(selected.server, () -> apply(selected, operation));
        } catch (Throwable error) { operation.reject("GAME_BINDING_UNSUPPORTED"); }
    }

    private String validate(World selected, Edit edit) {
        if (selected == null) return "GAME_WORLD_NOT_READY";
        if (!selected.local) return "SERVER_READ_ONLY";
        if (!incarnation.equals(edit.incarnation) || !selected.epoch.equals(edit.epoch) || !selected.dimension.equals(edit.dimension)) return "GAME_WORLD_STALE";
        return null;
    }

    private void apply(World selected, Operation operation) {
        synchronized (operation) {
            if (!operation.state.equals("queued")) return;
            try {
                String rejected = validate(world, operation.edit);
                if (rejected != null) { operation.reject(rejected); return; }
                // Recheck the actual client identities and the authoritative player's world immediately
                // before the linearization point. An old local capability or localhost is insufficient.
                if (bindings.field(selected.client, "clientLevel") != selected.level || bindings.field(selected.client, "clientPlayer") != selected.player
                    || bindings.call(selected.client, "clientServer") != selected.server
                    || !(Boolean)bindings.call(selected.connection, "connectionMemory")) { operation.reject("GAME_WORLD_STALE"); return; }
                Object listener = bindings.call(selected.client, "clientConnection");
                if (listener == null || bindings.call(listener, "listenerConnection") != selected.connection || !(Boolean)bindings.call(selected.client, "clientLocal")) {
                    operation.reject("GAME_WORLD_STALE"); return;
                }
                Object list = bindings.call(selected.server, "serverPlayerList");
                Object player = bindings.call(list, "listPlayer", selected.playerId);
                if (player == null) { operation.reject("GAME_WORLD_STALE"); return; }
                Object level = bindings.call(player, "entityLevel");
                if (!bindings.dimension(level).equals(operation.edit.dimension)) { operation.reject("GAME_WORLD_STALE"); return; }
                if (GamePlay.isClientOperation(operation.edit.kind)) {
                    Bindings.execute(selected.client, () -> applyClient(selected, operation)); return;
                }
                if (operation.edit.kind.equals("command.execute") && !(Boolean)bindings.call(bindings.call(selected.server, "serverWorldData"), "worldAllowCommands")) {
                    operation.reject("GAME_CHEATS_DISABLED"); return;
                }
                operation.state = "applying";
                operation.thread = Thread.currentThread().getName();
                if (operation.edit.kind.equals("world.setTime")) {
                    bindings.time(level, Long.parseLong(operation.edit.dayTime));
                    operation.time = Long.toString(bindings.time(level));
                } else if (operation.edit.kind.equals("player.setHealth")) {
                    bindings.call(player, "entitySetHealth", operation.edit.health);
                    operation.health = ((Number)bindings.call(player, "entityHealth")).floatValue();
                    if (!Float.isFinite(operation.health)) { operation.health = null; throw new IllegalStateException("GAME_DATA_INVALID"); }
                } else operation.result = gameplay.command(selected, player, (String)operation.edit.payload.get("command"));
                operation.state = "applied";
            } catch (Throwable error) {
                if (operation.state.equals("applying")) { operation.state = "unknown"; operation.error = "GAME_OPERATION_OUTCOME_UNKNOWN"; }
                else operation.reject("GAME_BINDING_UNSUPPORTED");
            }
        }
    }

    private void applyClient(World selected, Operation operation) {
        synchronized (operation) {
            if (!operation.state.equals("queued")) return;
            try {
                World actual = current(selected.client);
                String rejection = validate(actual, operation.edit);
                if (rejection != null) { operation.reject(rejection); return; }
                gameplay.checkClient(actual, operation.edit);
                operation.state = "applying"; operation.thread = Thread.currentThread().getName();
                operation.result = gameplay.applyClient(actual, operation);
                if (!operation.edit.kind.equals("player.control")) operation.state = "applied";
            } catch (Throwable error) {
                if (operation.state.equals("applying")) { operation.state = "unknown"; operation.error = "GAME_OPERATION_OUTCOME_UNKNOWN"; }
                else operation.reject(error instanceof IllegalArgumentException ? error.getMessage() : "GAME_BINDING_UNSUPPORTED");
            }
        }
    }

    void disconnected() { gameplay.disconnected(); }
    void connected() { gameplay.connected(); }

    Map<String, Object> query(Map<String, Object> parameters) throws Exception {
        GamePlay.validateQuery(parameters);
        Object client = bindings.client();
        if (client == null) throw new IllegalArgumentException("GAME_WORLD_NOT_READY");
        CompletableFuture<Map<String, Object>> completed = new CompletableFuture<>();
        Bindings.execute(client, () -> {
            if (completed.isDone()) return;
            try {
                World selected = current(client);
                if (selected == null) throw new IllegalArgumentException("GAME_WORLD_NOT_READY");
                if (!incarnation.equals(parameters.get("gameIncarnation")) || !selected.epoch.equals(parameters.get("worldEpoch")) || !selected.dimension.equals(parameters.get("dimension")))
                    throw new IllegalArgumentException("GAME_WORLD_STALE");
                String kind = Json.string(parameters, "kind");
                if (selected.local && GamePlay.serverQuery(kind)) {
                    Bindings.execute(selected.server, () -> {
                        if (completed.isDone()) return;
                        try {
                            if (world == null || !world.epoch.equals(selected.epoch) || bindings.field(selected.client, "clientLevel") != selected.level || bindings.field(selected.client, "clientPlayer") != selected.player || bindings.call(selected.client, "clientServer") != selected.server)
                                throw new IllegalArgumentException("GAME_WORLD_STALE");
                            Object player = bindings.call(bindings.call(selected.server, "serverPlayerList"), "listPlayer", selected.playerId);
                            if (player == null) throw new IllegalArgumentException("GAME_WORLD_NOT_READY");
                            completeQuery(completed, selected, kind, "server_authoritative", gameplay.queryServer(selected, player, parameters));
                        } catch (Throwable error) { bindings.report("queryServer", error); completed.completeExceptionally(error); }
                    });
                } else completeQuery(completed, selected, kind, kind.equals("registry") ? "game_registry" : "client_synchronized", gameplay.queryClient(selected, parameters));
            } catch (Throwable error) { bindings.report("queryClient", error); completed.completeExceptionally(error); }
        });
        try { return completed.get(10, TimeUnit.SECONDS); }
        catch (ExecutionException error) { if (error.getCause() instanceof Exception cause) throw cause; throw error; }
        finally { completed.cancel(false); }
    }

    private void completeQuery(CompletableFuture<Map<String, Object>> target, World selected, String kind, String source, Object data) {
        target.complete(Json.object("sessionId", session, "gameIncarnation", incarnation, "worldEpoch", selected.epoch, "dimension", selected.dimension,
            "sampledAt", Instant.now().toString(), "kind", kind, "source", source, "data", data));
    }

    Map<String, Object> operation(Map<String, Object> parameters, boolean cancel) {
        String id = Json.string(parameters, "operationId");
        Operation operation;
        synchronized (operations) { operation = operations.get(id); }
        if (operation == null) throw new IllegalArgumentException("GAME_OPERATION_NOT_FOUND");
        synchronized (operation) {
            if (cancel && operation.state.equals("queued")) operation.state = "canceled";
            return operation.receipt();
        }
    }

    Map<String, Object> region(Map<String, Object> parameters) throws Exception {
        if (!bindings.regionApi()) throw new IllegalArgumentException("GAME_REGION_UNSUPPORTED");
        String expectedIncarnation = Json.string(parameters, "gameIncarnation"), epoch = Json.string(parameters, "worldEpoch"), dimension = Json.string(parameters, "dimension");
        int x = integer(parameters, "x"), y = integer(parameters, "y"), z = integer(parameters, "z");
        int sx = integer(parameters, "sizeX"), sy = integer(parameters, "sizeY"), sz = integer(parameters, "sizeZ");
        if (sx < 1 || sy < 1 || sz < 1 || sx > 16 || sy > 16 || sz > 16 || (long)sx * sy * sz > 4096
            || Math.abs((long)x) > 30000000 || Math.abs((long)z) > 30000000 || Math.abs((long)y) > 4096) throw new IllegalArgumentException("GAME_REGION_RANGE");
        Object client = bindings.client();
        if (client == null) throw new IllegalArgumentException("GAME_WORLD_NOT_READY");
        CompletableFuture<Map<String, Object>> completed = new CompletableFuture<>();
        Bindings.execute(client, () -> {
            if (completed.isDone()) return;
            try {
                World selected = current(client);
                if (selected == null) throw new IllegalArgumentException("GAME_WORLD_NOT_READY");
                if (!incarnation.equals(expectedIncarnation) || !selected.epoch.equals(epoch) || !selected.dimension.equals(dimension)) throw new IllegalArgumentException("GAME_WORLD_STALE");
                double px = ((Number)bindings.call(selected.player, "entityX")).doubleValue(), py = ((Number)bindings.call(selected.player, "entityY")).doubleValue(), pz = ((Number)bindings.call(selected.player, "entityZ")).doubleValue();
                if (Math.abs(x - px) > 64 || Math.abs(y - py) > 64 || Math.abs(z - pz) > 64) throw new IllegalArgumentException("GAME_REGION_RANGE");
                Object blockRegistry = bindings.field(null, "registryBlock"), entityRegistry = bindings.field(null, "registryEntity"), itemRegistry = bindings.field(null, "registryItem");
                Object chunks = bindings.call(selected.level, "levelChunks");
                Map<Long, Boolean> loadedChunks = new HashMap<>();
                List<Object> blocks = new ArrayList<>(), entities = new ArrayList<>(), inventory = new ArrayList<>();
                // A partial response is explicit. The budget prevents a large read monopolizing the render thread.
                long deadline = regionClock.getAsLong() + TimeUnit.MILLISECONDS.toNanos(4);
                boolean truncated = false;
                outer: for (int dx = 0; dx < sx; dx++) for (int dy = 0; dy < sy; dy++) for (int dz = 0; dz < sz; dz++) {
                    if (regionClock.getAsLong() > deadline) { truncated = true; break outer; }
                    Object pos = bindings.blockPos(x + dx, y + dy, z + dz);
                    // ClientLevel.hasChunk always returns true. Query the actual client cache with
                    // ChunkSource.getChunkNow, which uses create=false and returns null when absent.
                    int cx = (x + dx) >> 4, cz = (z + dz) >> 4;
                    long chunkKey = ((long)cx << 32) ^ (cz & 0xffffffffL);
                    Boolean loaded = loadedChunks.get(chunkKey);
                    if (loaded == null) { loaded = bindings.call(chunks, "chunkNow", cx, cz) != null; loadedChunks.put(chunkKey, loaded); }
                    Object block = loaded ? bindings.call(bindings.call(selected.level, "levelBlock", pos), "stateBlock") : null;
                    blocks.add(Json.object("x", x + dx, "y", y + dy, "z", z + dz, "state", loaded ? "loaded" : "not_loaded", "blockId", block == null ? null : bindings.call(blockRegistry, "registryKey", block).toString()));
                }
                int scanned = 0;
                for (Object entity : (Iterable<?>)bindings.call(selected.level, "levelEntities")) {
                    if (scanned++ >= 256 || entities.size() >= 128 || regionClock.getAsLong() > deadline) { truncated = true; break; }
                    double ex = ((Number)bindings.call(entity, "entityX")).doubleValue(), ey = ((Number)bindings.call(entity, "entityY")).doubleValue(), ez = ((Number)bindings.call(entity, "entityZ")).doubleValue();
                    if (Math.abs(ex - px) > 32 || Math.abs(ey - py) > 32 || Math.abs(ez - pz) > 32) continue;
                    entities.add(Json.object("id", bindings.call(entity, "entityUuid").toString(), "typeId", bindings.call(entityRegistry, "registryKey", bindings.call(entity, "entityType")).toString(), "x", ex, "y", ey, "z", ez));
                }
                Object container = bindings.call(selected.player, "playerInventory");
                int count = ((Number)bindings.call(container, "inventorySize")).intValue();
                for (int index = 0; index < Math.min(count, 128); index++) {
                    if (regionClock.getAsLong() > deadline) { truncated = true; break; }
                    Object stack = bindings.call(container, "inventoryItem", index);
                    int amount = ((Number)bindings.call(stack, "stackCount")).intValue();
                    if (amount > 0) inventory.add(Json.object("slot", index, "itemId", bindings.call(itemRegistry, "registryKey", bindings.call(stack, "stackItem")).toString(), "count", amount));
                }
                completed.complete(Json.object("sessionId", session, "gameIncarnation", incarnation, "worldEpoch", epoch, "dimension", dimension,
                    "sampledAt", Instant.now().toString(), "source", "client_synchronized", "truncated", truncated, "blocks", blocks, "entities", entities, "inventory", inventory));
            } catch (Throwable error) { completed.completeExceptionally(error); }
        });
        try { return completed.get(10, TimeUnit.SECONDS); }
        finally { completed.cancel(false); }
    }

    private static int integer(Map<String, Object> parameters, String key) {
        Object value = parameters.get(key);
        if (!(value instanceof Long number) || number < Integer.MIN_VALUE || number > Integer.MAX_VALUE) throw new IllegalArgumentException("INVALID_PARAMS");
        return number.intValue();
    }
}
