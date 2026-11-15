package io.github.richasy.rodel.bridge;

import io.github.richasy.rodel.input.KeyHooks;
import java.lang.reflect.Proxy;
import java.time.Instant;
import java.util.*;
import java.util.stream.Stream;
import java.util.function.BooleanSupplier;
import java.util.concurrent.atomic.AtomicLong;

/** Native gameplay primitives and semantic observations. No strategy, arbitrary Java evaluation or remote writes. */
final class GamePlay {
    private static final Map<String, String> KEYS = Map.of("forward", "keyForward", "back", "keyBack", "left", "keyLeft", "right", "keyRight",
        "jump", "keyJump", "sneak", "keySneak", "sprint", "keySprint", "attack", "keyAttack", "use", "keyUse");
    private static final Set<String> QUERY_KINDS = Set.of("player", "inventory", "screen", "block", "entity", "world", "registry", "recipes", "advancements", "quests", "books");
    private static final Set<String> CLICK_MODES = Set.of("PICKUP", "QUICK_MOVE", "SWAP", "CLONE", "THROW", "QUICK_CRAFT", "PICKUP_ALL");
    private final Bindings b;
    private volatile InputLease input;
    private volatile boolean disconnected = true;
    private final AtomicLong transportGeneration = new AtomicLong();

    private static final class InputLease {
        final GameAccess.World world;
        final GameAccess.Operation operation;
        final List<String> keys;
        final long startedTick, deadline = System.nanoTime() + 8_000_000_000L;
        final int ticks, delay;
        final long transportGeneration;
        boolean armed;
        InputLease(GameAccess.World world, GameAccess.Operation operation, List<String> keys, long start, int ticks, long transportGeneration) {
            this.world = world; this.operation = operation; this.keys = keys; startedTick = start; this.ticks = ticks;
            delay = (int)integer(object(operation.edit.payload().get("control")), "interactionDelayTicks", 1);
            this.transportGeneration = transportGeneration;
        }
    }

    GamePlay(Bindings bindings) { b = bindings; }

    static boolean isClientOperation(String kind) { return Set.of("player.control", "player.release", "inventory.click", "inventory.button", "screen.close", "player.respawn", "world.publish").contains(kind); }
    static boolean serverQuery(String kind) { return Set.of("entity", "block", "world", "recipes", "advancements", "quests").contains(kind); }

    @SuppressWarnings("unchecked")
    static Map<String, Object> object(Object value) {
        if (!(value instanceof Map<?, ?>)) throw new IllegalArgumentException("INVALID_PARAMS");
        return (Map<String, Object>)value;
    }
    static long integer(Map<String, Object> value, String name) {
        if (!(value.get(name) instanceof Long number)) throw new IllegalArgumentException("INVALID_PARAMS");
        return number;
    }
    static long integer(Map<String, Object> value, String name, long fallback) { return value.containsKey(name) ? integer(value, name) : fallback; }
    private static float number(Map<String, Object> value, String name) {
        if (!(value.get(name) instanceof Number number) || !Float.isFinite(number.floatValue())) throw new IllegalArgumentException("INVALID_PARAMS");
        return number.floatValue();
    }
    static void validateEdit(String kind, Map<String, Object> value) {
        int payloads = 0;
        for (String key : List.of("dayTime", "health", "command", "control", "click", "button")) if (value.get(key) != null) payloads++;
        if (kind.equals("command.execute")) {
            if (!(value.get("command") instanceof String command)) throw new IllegalArgumentException("INVALID_PARAMS");
            if (payloads != 1 || command.isBlank() || command.length() > 32767 || command.indexOf('\n') >= 0 || command.indexOf('\r') >= 0 || command.indexOf('\0') >= 0) throw new IllegalArgumentException("INVALID_PARAMS");
        } else if (kind.equals("player.control")) {
            Map<String, Object> control = object(value.get("control"));
            long ticks = integer(control, "ticks", 1);
            long delay = integer(control, "interactionDelayTicks", 1);
            if (payloads != 1 || ticks < 1 || ticks > 100 || delay < 1 || delay > ticks) throw new IllegalArgumentException("INVALID_PARAMS");
            Object rawKeys = control.getOrDefault("keys", List.of());
            if (!(rawKeys instanceof List<?> keys) || keys.size() > 9 || new HashSet<>(keys).size() != keys.size() || !KEYS.keySet().containsAll(keys)) throw new IllegalArgumentException("INVALID_PARAMS");
            if (control.get("yaw") != null && Math.abs(number(control, "yaw")) > 360 || control.get("pitch") != null && Math.abs(number(control, "pitch")) > 90
                || control.get("hotbar") != null && (integer(control, "hotbar") < 0 || integer(control, "hotbar") > 8)) throw new IllegalArgumentException("INVALID_PARAMS");
        } else if (kind.equals("inventory.click")) {
            Map<String, Object> click = object(value.get("click"));
            long slot = integer(click, "slot"), button = integer(click, "button", 0);
            String mode = Json.string(click, "mode");
            if (payloads != 1 || integer(click, "containerId") < 0 || integer(click, "stateId") < 0 || slot < -999 || slot > 511 || slot < 0 && slot != -999 || !CLICK_MODES.contains(mode)
                || button < 0 || (mode.equals("SWAP") ? button > 8 && button != 40 : mode.equals("CLONE") ? button != 2 : mode.equals("QUICK_CRAFT") ? button > 10 : button > 1)) throw new IllegalArgumentException("INVALID_PARAMS");
        } else if (kind.equals("inventory.button")) {
            Map<String, Object> button = object(value.get("button"));
            if (payloads != 1 || integer(button, "containerId") < 0 || integer(button, "stateId") < 0 || integer(button, "button") < 0 || integer(button, "button") > 255) throw new IllegalArgumentException("INVALID_PARAMS");
        } else if (!Set.of("player.release", "screen.close", "player.respawn", "world.publish").contains(kind)) throw new IllegalArgumentException("GAME_BRIDGE_METHOD_UNKNOWN");
        else if (payloads != 0) throw new IllegalArgumentException("INVALID_PARAMS");
    }
    static void validateQuery(Map<String, Object> value) {
        if (!QUERY_KINDS.contains(Json.string(value, "kind")) || integer(value, "offset", 0) < 0 || integer(value, "limit", 50) < 1 || integer(value, "limit", 50) > 100
            || value.get("nbtPath") instanceof String path && path.length() > 1024 || value.get("resource") instanceof String resource && resource.length() > 256
            || value.get("search") != null && (!(value.get("search") instanceof String search) || search.length() > 256))
            throw new IllegalArgumentException("INVALID_PARAMS");
        if (value.get("entityId") != null) UUID.fromString(Json.string(value, "entityId"));
        if (value.get("kind").equals("block") && (Math.abs(integer(value, "x")) > 30000000 || Math.abs(integer(value, "y")) > 4096 || Math.abs(integer(value, "z")) > 30000000)) throw new IllegalArgumentException("GAME_REGION_RANGE");
    }

    void checkClient(GameAccess.World world, GameAccess.Edit edit) throws Exception {
        if (!world.local()) throw new IllegalArgumentException("SERVER_READ_ONLY");
        String kind = edit.kind();
        if (kind.equals("player.respawn")) {
            if ((Boolean)b.call(world.player(), "entityAlive")) throw new IllegalArgumentException("GAME_PLAYER_ALREADY_ALIVE");
            return;
        }
        if (kind.equals("world.publish")) {
            if (!b.has("serverPublished") || !b.has("serverPort")) throw new IllegalArgumentException("GAME_LAN_UNSUPPORTED");
            b.requireMethod("serverPort");
            if (!(Boolean)b.call(world.server(), "serverPublished")) {
                if (!b.has("serverPublish")) throw new IllegalArgumentException("GAME_LAN_UNSUPPORTED");
                b.requireMethod("serverPublish");
                if (b.has("lanScope")) b.field(null, "lanScope");
                else b.call(b.call(world.server(), "serverWorldData"), "worldAllowCommands");
            }
            return;
        }
        if (kind.equals("player.release") || kind.equals("screen.close")) return;
        if (!(Boolean)b.call(world.player(), "entityAlive")) throw new IllegalArgumentException("GAME_PLAYER_NOT_ALIVE");
        if (kind.equals("player.control")) {
            if (disconnected) throw new IllegalArgumentException("GAME_BRIDGE_DISCONNECTED");
            if (!KeyHooks.available()) throw new IllegalArgumentException("GAME_INPUT_HOOK_UNAVAILABLE");
            if (input != null) throw new IllegalArgumentException("GAME_CONTROL_BUSY");
            if (b.screen(world.client()) != null || (Boolean)b.call(world.client(), "clientPaused")) throw new IllegalArgumentException("GAME_SCREEN_OPEN");
            Object options = b.field(world.client(), "clientOptions");
            for (Object key : (List<?>)object(edit.payload().get("control")).getOrDefault("keys", List.of())) b.field(options, KEYS.get(key));
        } else {
            Map<String, Object> click = object(edit.payload().get(kind.equals("inventory.button") ? "button" : "click"));
            Object menu = b.field(world.player(), "playerMenu");
            if (((Number)b.field(menu, "menuId")).longValue() != integer(click, "containerId") || ((Number)b.call(menu, "menuState")).longValue() != integer(click, "stateId")) throw new IllegalArgumentException("GAME_MENU_CHANGED");
            if (kind.equals("inventory.button")) return;
            long slot = integer(click, "slot");
            if (slot >= ((List<?>)b.field(menu, "menuSlots")).size()) throw new IllegalArgumentException("GAME_SLOT_UNAVAILABLE");
        }
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    Map<String, Object> applyClient(GameAccess.World world, GameAccess.Operation operation) throws Exception {
        String kind = operation.edit.kind();
        if (kind.equals("world.publish")) return publish(world);
        if (kind.equals("player.control")) {
            Map<String, Object> control = object(operation.edit.payload().get("control"));
            if (control.get("yaw") != null) b.call(world.player(), "entitySetYaw", number(control, "yaw"));
            if (control.get("pitch") != null) b.call(world.player(), "entitySetPitch", number(control, "pitch"));
            if (control.get("hotbar") != null) select(world, (int)integer(control, "hotbar"));
            if (!b.namedApi()) b.call(b.field(world.client(), "clientRenderer"), "rendererPick", 1f);
            List<String> keys = new ArrayList<>((List<String>)control.getOrDefault("keys", List.of()));
            InputLease lease = new InputLease(world, operation, keys, ((Number)b.call(world.level(), "levelGameTime")).longValue(), (int)integer(control, "ticks", 1), transportGeneration.get());
            Map<Object, BooleanSupplier> overrides = new IdentityHashMap<>();
            Object options = b.field(world.client(), "clientOptions");
            for (String key : keys) overrides.put(b.field(options, KEYS.get(key)), () -> allowed(lease, key));
            KeyHooks.install(overrides);
            input = lease;
            return null;
        }
        if (kind.equals("player.release")) { finishInput("requested"); return Json.object("released", true); }
        if (kind.equals("screen.close")) {
            Object screen = b.screen(world.client());
            if (screen != null) b.call(screen, "screenClose");
            return Json.object("closeRequested", screen != null);
        }
        if (kind.equals("player.respawn")) {
            if ((Boolean)b.call(world.player(), "entityAlive")) throw new IllegalArgumentException("GAME_PLAYER_ALREADY_ALIVE");
            b.call(world.player(), "playerRespawn"); b.setScreen(world.client(), null);
            return Json.object("respawnRequested", true);
        }
        if (kind.equals("inventory.button")) {
            Map<String, Object> button = object(operation.edit.payload().get("button"));
            b.call(b.field(world.client(), "clientGameMode"), "gameButton", (int)integer(button, "containerId"), (int)integer(button, "button"));
            return Json.object("buttonRequested", true);
        }
        Map<String, Object> click = object(operation.edit.payload().get("click"));
        Object mode = Enum.valueOf((Class)b.officialType("net.minecraft.world.inventory.ClickType"), Json.string(click, "mode"));
        b.call(b.field(world.client(), "clientGameMode"), "gameClick", (int)integer(click, "containerId"), (int)integer(click, "slot"), (int)integer(click, "button", 0), mode, world.player());
        return inventory(world);
    }

    private Map<String, Object> publish(GameAccess.World world) throws Exception {
        boolean existing = (Boolean)b.call(world.server(), "serverPublished");
        if (!existing) {
            int port;
            try (java.net.ServerSocket reservation = new java.net.ServerSocket(0)) { port = reservation.getLocalPort(); }
            boolean opened;
            if (b.has("lanScope")) {
                // The two-argument scoped API preserves both game mode and guest command access.
                opened = (Boolean)b.call(world.server(), "serverPublish", b.field(null, "lanScope"), port);
            } else {
                Object data = b.call(world.server(), "serverWorldData");
                boolean allowCommands = (Boolean)b.call(data, "worldAllowCommands");
                // Native LAN screens invoke this on the client executor. Null retains the world's
                // native default mode; retain the current cheats setting rather than enabling it.
                opened = (Boolean)b.call(world.server(), "serverPublish", null, allowCommands, port);
            }
            if (!opened) throw new IllegalArgumentException("GAME_LAN_OPEN_FAILED");
        }
        int port = ((Number)b.call(world.server(), "serverPort")).intValue();
        if (port < 1 || port > 65535 || !(Boolean)b.call(world.server(), "serverPublished")) throw new IllegalArgumentException("GAME_LAN_OPEN_FAILED");
        return Json.object("port", port, "reused", existing, "localWorldEpoch", world.localEpoch());
    }
    void connected() { disconnected = false; }
    void disconnected() { disconnected = true; transportGeneration.incrementAndGet(); }
    boolean hasInput() { return input != null; }
    void advance(GameAccess.World actual) throws Exception {
        InputLease lease = input;
        if (lease == null) return;
        if (disconnected || lease.transportGeneration != transportGeneration.get() || actual == null || !actual.local() || actual.client() != lease.world.client() || !actual.epoch().equals(lease.world.epoch())) { finishInput(disconnected || lease.transportGeneration != transportGeneration.get() ? "disconnected" : "world_changed"); return; }
        if (b.screen(actual.client()) != null || (Boolean)b.call(actual.client(), "clientPaused")) { finishInput("screen_open"); return; }
        if (!(Boolean)b.call(actual.player(), "entityAlive")) { finishInput("player_not_alive"); return; }
        if (System.nanoTime() >= lease.deadline) { finishInput("watchdog"); return; }
        long elapsed = ((Number)b.call(actual.level(), "levelGameTime")).longValue() - lease.startedTick;
        if (elapsed <= 0) return;
        if (!lease.armed && elapsed >= lease.delay) {
            lease.armed = true;
            if (lease.keys.contains("attack")) b.call(actual.client(), "clientAttack");
            if (lease.keys.contains("use")) b.call(actual.client(), "clientUse");
        }
        if (elapsed >= lease.ticks) finishInput("elapsed");
    }
    private void finishInput(String reason) throws Exception {
        InputLease lease = input;
        if (lease == null) return;
        input = null;
        KeyHooks.clear();
        try {
            if (lease.keys.contains("use") && b.field(lease.world.client(), "clientLevel") == lease.world.level() && b.field(lease.world.client(), "clientPlayer") == lease.world.player()
                && b.call(lease.world.client(), "clientServer") == lease.world.server() && (Boolean)b.call(lease.world.connection(), "connectionMemory")
                && b.call(lease.world.client(), "clientConnection") != null
                && b.call(b.call(lease.world.client(), "clientConnection"), "listenerConnection") == lease.world.connection()
                && (Boolean)b.call(lease.world.client(), "clientLocal")
                && !(Boolean)b.call(b.field(b.field(lease.world.client(), "clientOptions"), "keyUse"), "keyIsDown"))
                b.call(b.field(lease.world.client(), "clientGameMode"), "gameReleaseUse", lease.world.player());
            synchronized (lease.operation) {
                lease.operation.result = Json.object("released", true, "stopReason", reason, "requestedTicks", lease.ticks);
                lease.operation.state = "applied";
            }
        } catch (Throwable error) {
            synchronized (lease.operation) { lease.operation.state = "unknown"; lease.operation.error = "GAME_CONTROL_RELEASE_UNKNOWN"; }
            throw error;
        }
    }
    private boolean allowed(InputLease lease, String key) {
        if (input != lease || disconnected || lease.transportGeneration != transportGeneration.get() || System.nanoTime() >= lease.deadline || (!lease.armed && (key.equals("attack") || key.equals("use")))) return false;
        try {
            return b.field(lease.world.client(), "clientLevel") == lease.world.level() && b.field(lease.world.client(), "clientPlayer") == lease.world.player()
                && b.call(lease.world.client(), "clientServer") == lease.world.server() && b.screen(lease.world.client()) == null
                && b.call(b.call(lease.world.client(), "clientConnection"), "listenerConnection") == lease.world.connection()
                && (Boolean)b.call(lease.world.connection(), "connectionMemory") && (Boolean)b.call(lease.world.client(), "clientLocal");
        } catch (Throwable unavailable) { return false; }
    }
    private int selected(GameAccess.World world) throws Exception {
        Object inventory = b.call(world.player(), "playerInventory");
        return ((Number)(b.namedApi() ? b.call(inventory, "inventoryGetSelected") : b.field(inventory, "inventorySelected"))).intValue();
    }
    private void select(GameAccess.World world, int slot) throws Exception {
        Object inventory = b.call(world.player(), "playerInventory");
        if (b.namedApi()) b.call(inventory, "inventorySetSelected", slot); else b.setField(inventory, "inventorySelected", slot);
    }

    Map<String, Object> command(GameAccess.World world, Object player, String command) throws Exception {
        if (!(Boolean)b.call(b.call(world.server(), "serverWorldData"), "worldAllowCommands")) throw new IllegalArgumentException("GAME_CHEATS_DISABLED");
        List<Object> messages = new ArrayList<>(), callbacks = new ArrayList<>();
        String send = b.nativeName("sourceMessage"), success = b.nativeName("sourceSuccess"), failure = b.nativeName("sourceFailure");
        Class<?> sourceType = b.officialType("net.minecraft.commands.CommandSource");
        Object receiver = Proxy.newProxyInstance(sourceType.getClassLoader(), new Class<?>[]{sourceType}, (proxy, method, arguments) -> {
            if (method.getName().equals(send)) { if (messages.size() < 64) { String text = (String)b.call(arguments[0], "componentText"); messages.add(text.length() > 4096 ? text.substring(0, 4096) : text); } return null; }
            if (method.getName().equals(success) || method.getName().equals(failure)) return true;
            if (method.getReturnType() == boolean.class) return false;
            if (method.getName().equals("toString")) return "Rodel command feedback";
            if (method.getName().equals("hashCode")) return System.identityHashCode(proxy);
            if (method.getName().equals("equals")) return proxy == arguments[0];
            return null;
        });
        Object source = b.call(b.call(player, "playerCommandSource"), "commandWithSource", receiver);
        if (b.has("commandWithCallback")) {
            Class<?> callbackType = b.officialType("net.minecraft.commands.CommandResultCallback");
            Object callback = Proxy.newProxyInstance(callbackType.getClassLoader(), new Class<?>[]{callbackType}, (proxy, method, args) -> {
                if (method.getName().equals("onResult") && callbacks.size() < 64) callbacks.add(Json.object("success", args[0], "result", args[1]));
                return null;
            });
            source = b.call(source, "commandWithCallback", callback);
        }
        b.call(b.call(world.server(), "serverCommands"), "commandPerform", source, command);
        return Json.object("nativeDispatchCompleted", true, "callbacks", callbacks, "messages", messages, "feedbackTruncated", messages.size() == 64 || callbacks.size() == 64);
    }

    Object queryClient(GameAccess.World world, Map<String, Object> request) throws Exception {
        return switch (Json.string(request, "kind")) {
            case "player" -> player(world);
            case "inventory" -> inventory(world);
            case "screen" -> screen(world);
            case "registry" -> registry(request);
            case "books" -> new GameKnowledge(b).books(request);
            case "quests" -> new GameKnowledge(b).quests(world, request);
            case "world" -> Json.object("dayTime", Long.toString(b.time(world.level())), "serverDataAvailable", false);
            case "block" -> block(world, world.level(), request);
            case "entity" -> nbt(findClientEntity(world, request), world.level(), request);
            default -> throw new IllegalArgumentException("GAME_QUERY_UNAVAILABLE");
        };
    }
    Object queryServer(GameAccess.World world, Object player, Map<String, Object> request) throws Exception {
        Object level = b.call(player, "entityLevel");
        if (!b.dimension(level).equals(world.dimension())) throw new IllegalArgumentException("GAME_WORLD_STALE");
        return switch (Json.string(request, "kind")) {
            case "world" -> {
                Object data = b.call(world.server(), "serverWorldData");
                yield Json.object("name", b.call(data, "worldName"), "allowCommands", b.call(data, "worldAllowCommands"), "hardcore", b.call(data, "worldHardcore"),
                    "seed", b.call(level, "levelSeed").toString(), "dayTime", Long.toString(b.time(level)), "localWorldEpoch", world.localEpoch(),
                    "directory", worldDirectory(world));
            }
            case "block" -> block(world, level, request);
            case "entity" -> {
                Object entity = request.get("entityId") == null ? player : b.call(level, "levelEntity", UUID.fromString(Json.string(request, "entityId")));
                if (entity == null || !b.dimension(b.call(entity, "entityLevel")).equals(world.dimension())) throw new IllegalArgumentException("GAME_ENTITY_NOT_LOADED");
                yield nbt(entity, level, request);
            }
            case "recipes" -> recipes(world, request);
            case "quests" -> new GameKnowledge(b).quests(world, request);
            case "advancements" -> advancements(world, player, request);
            default -> throw new IllegalArgumentException("GAME_QUERY_UNAVAILABLE");
        };
    }
    private String worldDirectory(GameAccess.World world) {
        if (!b.has("serverWorldDirectory") || !b.has("worldRootResource")) return null;
        try {
            return ((java.nio.file.Path)b.call(world.server(), "serverWorldDirectory", b.field(null, "worldRootResource")))
                    .toAbsolutePath().normalize().toString();
        } catch (Exception error) { return null; }
    }
    private Map<String, Object> player(GameAccess.World world) throws Exception {
        Object food = b.call(world.player(), "playerFood");
        Object hit = b.field(world.client(), "clientHit");
        Map<String, Object> target = null;
        if (hit != null) {
            String kind = b.call(hit, "hitType").toString();
            target = Json.object("kind", kind, "location", b.call(hit, "hitLocation").toString());
            if (kind.equals("BLOCK")) {
                Object position = b.call(hit, "blockHitPos");
                target.put("x", b.call(position, "posX")); target.put("y", b.call(position, "posY")); target.put("z", b.call(position, "posZ")); target.put("face", b.call(hit, "blockHitFace").toString());
            } else if (kind.equals("ENTITY")) target.put("entityId", b.call(b.call(hit, "entityHitTarget"), "entityUuid").toString());
        }
        return Json.object("x", b.call(world.player(), "entityX"), "y", b.call(world.player(), "entityY"), "z", b.call(world.player(), "entityZ"),
            "yaw", b.call(world.player(), "entityYaw"), "pitch", b.call(world.player(), "entityPitch"), "health", b.call(world.player(), "entityHealth"),
            "alive", b.call(world.player(), "entityAlive"), "onGround", b.call(world.player(), "entityOnGround"), "food", b.call(food, "foodLevel"), "saturation", b.call(food, "foodSaturation"),
            "blocking", b.call(world.player(), "entityBlocking"), "usingItem", b.call(world.player(), "entityUsingItem"),
            "hotbar", selected(world), "gameMode", b.call(b.call(b.field(world.client(), "clientGameMode"), "gamePlayerMode"), "gameTypeName"), "target", target);
    }
    private Map<String, Object> inventory(GameAccess.World world) throws Exception {
        Object menu = b.field(world.player(), "playerMenu");
        List<?> slots = (List<?>)b.field(menu, "menuSlots");
        if (slots.size() > 512) throw new IllegalArgumentException("GAME_MENU_TOO_LARGE");
        List<Object> items = new ArrayList<>();
        for (int index = 0; index < slots.size(); index++) { Map<String, Object> item = stack(b.call(slots.get(index), "slotItem")); item.put("slot", index); items.add(item); }
        List<Object> data = new ArrayList<>();
        for (Object slot : (List<?>)b.field(menu, "menuDataSlots")) { if (data.size() >= 128) throw new IllegalArgumentException("GAME_MENU_TOO_LARGE"); data.add(b.call(slot, "dataSlotValue")); }
        return Json.object("containerId", b.field(menu, "menuId"), "stateId", b.call(menu, "menuState"), "menuType", menu.getClass().getName(), "hotbar", selected(world), "dataSlots", data,
            "slots", items, "carried", stack(b.call(menu, "menuCarried")));
    }
    private Map<String, Object> stack(Object stack) throws Exception {
        int count = ((Number)b.call(stack, "stackCount")).intValue();
        return Json.object("itemId", b.call(b.field(null, "registryItem"), "registryKey", b.call(stack, "stackItem")).toString(), "count", count);
    }
    private Map<String, Object> screen(GameAccess.World world) throws Exception {
        Object screen = b.screen(world.client());
        return Json.object("open", screen != null, "type", screen == null ? null : screen.getClass().getName(), "title", screen == null ? null : b.call(b.call(screen, "screenTitle"), "componentText"), "paused", b.call(world.client(), "clientPaused"));
    }
    private Object registry(Map<String, Object> request) throws Exception {
        String kind = request.getOrDefault("resource", "item").toString();
        String field = switch (kind) { case "item" -> "registryItem"; case "block" -> "registryBlock"; case "entity" -> "registryEntity"; default -> throw new IllegalArgumentException("GAME_QUERY_UNAVAILABLE"); };
        List<String> keys = ((Set<?>)b.call(b.field(null, field), "registryKeys")).stream().map(Object::toString).sorted().toList();
        return page(keys, request);
    }
    private Object recipes(GameAccess.World world, Map<String, Object> request) throws Exception {
        if (b.namedApi() || b.has("recipeIngredients") && b.has("recipeResult"))
            return new GameKnowledge(b).recipes(world, request);
        if (b.namedApi()) {
            List<String> ids = new ArrayList<>();
            for (Object holder : (Collection<?>)b.call(b.call(world.server(), "serverRecipes"), "recipesCollection"))
                ids.add(b.call(b.call(holder, "recipeHolderId"), "keyLocation").toString());
            return page(ids.stream().filter(id -> request.get("resource") == null || id.startsWith(request.get("resource").toString())).sorted().toList(), request);
        }
        try (Stream<?> ids = (Stream<?>)b.call(b.call(world.server(), "serverRecipes"), "recipeIds")) {
            return page(ids.map(Object::toString).filter(id -> request.get("resource") == null || id.startsWith(request.get("resource").toString())).sorted().toList(), request);
        }
    }
    private Object advancements(GameAccess.World world, Object player, Map<String, Object> request) throws Exception {
        Map<?, ?> progresses = (Map<?, ?>)b.field(b.call(player, "playerAdvancements"), "advancementProgress");
        List<Map<String, Object>> values = new ArrayList<>();
        for (Object holder : (Collection<?>)b.call(b.call(world.server(), "serverAdvancements"), "allAdvancements")) {
            String id = b.call(holder, b.has("advancementId") ? "advancementId" : "advancementIdLegacy").toString();
            if (request.get("resource") != null && !id.startsWith(request.get("resource").toString())) continue;
            Object progress = progresses.get(holder);
            values.add(Json.object("id", id, "done", progress != null && (Boolean)b.call(progress, "advancementDone"), "progressPresent", progress != null,
                "completed", progress == null ? List.of() : iterable(b.call(progress, "advancementCompleted")), "remaining", progress == null ? null : iterable(b.call(progress, "advancementRemaining"))));
        }
        values.sort(Comparator.comparing(value -> value.get("id").toString())); return page(values, request);
    }
    private static List<Object> iterable(Object value) { List<Object> result = new ArrayList<>(); for (Object item : (Iterable<?>)value) result.add(item.toString()); return result; }
    private static Object page(List<?> values, Map<String, Object> request) {
        int start = (int)Math.min(integer(request, "offset", 0), values.size()), end = (int)Math.min((long)start + integer(request, "limit", 50), values.size());
        return Json.object("items", values.subList(start, end), "total", values.size(), "offset", start, "hasMore", end < values.size(), "nextOffset", end < values.size() ? end : null);
    }
    private Object findClientEntity(GameAccess.World world, Map<String, Object> request) throws Exception {
        if (request.get("entityId") == null) return world.player();
        UUID target = UUID.fromString(Json.string(request, "entityId")); int scanned = 0;
        if (target.equals(world.playerId())) return world.player();
        for (Object entity : (Iterable<?>)b.call(world.level(), "levelEntities")) {
            if (scanned++ >= 256) break;
            if (target.equals(b.call(entity, "entityUuid"))) return entity;
        }
        throw new IllegalArgumentException("GAME_ENTITY_NOT_LOADED");
    }
    private Object block(GameAccess.World world, Object level, Map<String, Object> request) throws Exception {
        int x = (int)integer(request, "x"), y = (int)integer(request, "y"), z = (int)integer(request, "z");
        if (Math.abs(x - ((Number)b.call(world.player(), "entityX")).doubleValue()) > 64 || Math.abs(y - ((Number)b.call(world.player(), "entityY")).doubleValue()) > 64 || Math.abs(z - ((Number)b.call(world.player(), "entityZ")).doubleValue()) > 64) throw new IllegalArgumentException("GAME_REGION_RANGE");
        Object chunks = b.call(level, "levelChunks");
        if (b.call(chunks, "chunkNow", x >> 4, z >> 4) == null) return Json.object("state", "not_loaded");
        Object pos = b.blockPos(x, y, z), state = b.call(level, "levelBlock", pos), entity = b.call(level, "levelBlockEntity", pos);
        Object tag = entity == null ? null : b.has("blockEntityNbt") ? b.call(entity, "blockEntityNbt", b.call(level, "levelRegistryAccess")) : b.call(entity, "blockEntityNbtLegacy");
        return Json.object("state", "loaded", "blockId", b.call(b.field(null, "registryBlock"), "registryKey", b.call(state, "stateBlock")).toString(), "properties", state.toString(), "nbt", tag == null ? null : tag(tag, request));
    }
    private Object nbt(Object entity, Object level, Map<String, Object> request) throws Exception {
        Object tag;
        if (b.namedApi()) {
            Class<?> output = b.officialType("net.minecraft.world.level.storage.TagValueOutput"), reporter = b.officialType("net.minecraft.util.ProblemReporter"), lookup = b.officialType("net.minecraft.core.HolderLookup$Provider");
            Object writer = output.getMethod("createWithContext", reporter, lookup).invoke(null, reporter.getField("DISCARDING").get(null), b.call(level, "levelRegistryAccess"));
            b.type("net.minecraft.world.entity.Entity").getMethod("saveWithoutId", b.officialType("net.minecraft.world.level.storage.ValueOutput")).invoke(entity, writer);
            tag = output.getMethod("buildResult").invoke(writer);
        } else tag = b.call(entity, "entityNbt", b.officialType("net.minecraft.nbt.CompoundTag").getConstructor().newInstance());
        return tag(tag, request);
    }
    private Object tag(Object tag, Map<String, Object> request) throws Exception {
        List<String> values = new ArrayList<>();
        if (request.get("nbtPath") instanceof String path && !path.isEmpty()) {
            Object reader = b.type("com.mojang.brigadier.StringReader").getConstructor(String.class).newInstance(path);
            Object selector = b.call(b.call(null, "nbtPathFactory"), "nbtPathParse", reader);
            for (Object item : (List<?>)b.call(selector, "nbtPathGet", tag)) values.add(item.toString());
        } else values.add(tag.toString());
        int size = values.stream().mapToInt(String::length).sum();
        if (values.size() > 100 || size > 262144) throw new IllegalArgumentException("GAME_QUERY_TOO_LARGE");
        return Json.object("format", "snbt", "values", values);
    }
}
