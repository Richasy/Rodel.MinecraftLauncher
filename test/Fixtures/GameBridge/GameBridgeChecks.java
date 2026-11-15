package io.github.richasy.rodel.bridge;

import java.lang.instrument.Instrumentation;
import java.lang.reflect.Proxy;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.LongSupplier;
import io.github.richasy.rodel.input.KeyHooks;

/** Deterministic scheduling/ownership regressions against the production GameAccess, not Minecraft API evidence. */
public final class GameBridgeChecks {
    public static final class Client {
        static Client instance;
        public volatile Level level = new Level();
        public volatile Player player = new Player(level);
        public final Options options = new Options();
        public Object screen;
        public final Renderer renderer = new Renderer();
        public final GameMode gameMode = new GameMode();
        int uses;
        final Server server = new Server(player);
        final ExecutorService executor = Executors.newSingleThreadExecutor(Agent.threads("fixture-client"));
        volatile Connection connection = new Connection();
        public static Client getInstance() { return instance; }
        public Server getSingleplayerServer() { return server; }
        public boolean isLocalServer() { return true; }
        public Listener getConnection() { return new Listener(connection); }
        public boolean isPaused() { return false; }
        public boolean startAttack() { return true; }
        public void startUseItem() { uses++; }
        public void execute(Runnable action) { executor.execute(action); }
        public Object probe(Bindings bindings) throws Exception { return bindings.client(); }
    }
    public static class KeyReturnBase {
        public boolean modifierDown;
        public int modifierCalls;
        protected boolean modify(boolean original) { modifierCalls++; return original || modifierDown; }
    }
    public static final class KeyInput extends KeyReturnBase {
        public boolean physical;
        public boolean isDown() { return physical; }
        public boolean modified() { return modify(physical); }
        public boolean modifiedTwice() { return modify(modify(physical)); }
        public boolean modifiedBySuper() { return super.modify(physical); }
        public boolean complicated() { return physical && System.nanoTime() != 0; }
    }
    public static final class Options { public final KeyInput keyUp = new KeyInput(), keyUse = new KeyInput(); }
    public static final class Renderer { public void pick(float value) { } }
    public static final class GameMode { int releases; public void releaseUsingItem(Player player) { releases++; } }
    private static Bindings fixtureBindings;
    public static final class Listener {
        final Connection connection;
        Listener(Connection connection) { this.connection = connection; }
        public Connection getConnection() { return connection; }
    }
    public static final class Connection {
        volatile boolean memory = true;
        public boolean isMemoryConnection() { return memory; }
    }
    public static final class Level {
        long ticks;
        boolean loaded;
        int blockReads, chunkReads;
        public Key dimension() { return new Key(); }
        public long getDayTime() { return ticks; }
        public long getGameTime() { return ticks; }
        public void setDayTime(long value) { ticks = value; }
        public boolean hasChunkAt(Position position) { return true; }
        public Level getChunkSource() { return this; }
        public Object getChunkNow(int x, int z) { chunkReads++; return loaded ? this : null; }
        public BlockState getBlockState(Position position) { blockReads++; return new BlockState(); }
        public Iterable<Player> entitiesForRendering() { return List.of(); }
        public Level registryAccess() { return this; }
    }
    public static final class Position { public Position(int x, int y, int z) { } }
    public static final class BlockState { public Object getBlock() { return this; } }
    public static final class Registry { public String getKey(Object item) { return item instanceof Stack stack ? stack.id : "minecraft:stone"; } }
    public static final class Text {
        final String value;
        public Text(String value) { this.value = value; }
        public String getString() { return value; }
    }
    public static final class Stack {
        final String id, name;
        final int count;
        Stack(String id, String name, int count) { this.id = id; this.name = name; this.count = count; }
        public Stack getItem() { return this; }
        public int getCount() { return count; }
        public Text getHoverName() { return new Text(name); }
    }
    public static final class Ingredient {
        public Stack[] getItems() { return new Stack[]{new Stack("fixture:grain", "谷物", 1), new Stack("fixture:rice", "稻米", 1)}; }
    }
    public static class Recipe {
        public boolean unrequested;
        public String getId() { return unrequested ? "fixture:z_unrequested" : "fixture:custom_meal"; }
        public String getType() { return "fixture:cooking"; }
        public List<Ingredient> getIngredients() {
            if (unrequested) throw new AssertionError("Paging must not materialize unrequested ingredient models");
            return List.of(new Ingredient(), new Ingredient());
        }
        public Stack getResultItem(Level registries) { return new Stack("fixture:meal", "整合包餐食", 3); }
        public Stack getToastSymbol() { return new Stack("fixture:cooking_pot", "测试厨锅", 1); }
    }
    public static final class ShapedRecipe extends Recipe {
        public int getWidth() { return 2; }
        public int getHeight() { return 1; }
    }
    public static final class Recipes {
        volatile List<Recipe> loaded = List.of(new Recipe());
        public Collection<Recipe> getRecipes() { return loaded; }
    }
    public static final class Registries { public static final Registry BLOCK = new Registry(), ENTITY_TYPE = new Registry(), ITEM = new Registry(); }
    public static final class Key { public String location() { return "minecraft:overworld"; } }
    public static final class Player {
        final UUID id = UUID.randomUUID();
        final Level level;
        volatile float health = 20;
        volatile int writes;
        volatile boolean failReadback, applied;
        Player(Level level) { this.level = level; }
        public UUID getUUID() { return id; }
        public double getX() { return 1; }
        public double getY() { return 64; }
        public double getZ() { return 3; }
        public Level level() { return level; }
        public Player getInventory() { return this; }
        public int getContainerSize() { return 0; }
        public float getHealth() { if (failReadback && applied) throw new IllegalStateException("readback unavailable"); return health; }
        public boolean isAlive() { return health > 0; }
        public void setHealth(float value) { if (!Thread.currentThread().getName().equals("fixture-server")) throw new AssertionError("wrong authority thread"); health = value; writes++; applied = true; }
    }
    public enum PublicationScope { OFF, LAN }
    public static final class Server {
        public final WorldData data = new WorldData();
        int publishedPort, publications;
        boolean guestCommands = true;
        public WorldData getWorldData() { return data; }
        public boolean isPublished() { return publishedPort != 0; }
        public int getPort() { return publishedPort; }
        public boolean publishServer(Object mode, boolean cheats, int port) {
            check(Thread.currentThread().getName().equals("fixture-client"), "LAN publish must use the native client executor");
            check(mode == null && !cheats && !data.isAllowCommands(), "hosting must preserve mode and disabled cheats");
            publishedPort = port; publications++; return true;
        }
        public boolean publishServer(PublicationScope scope, int port) {
            check(Thread.currentThread().getName().equals("fixture-client"), "LAN publish must use the native client executor");
            check(scope == PublicationScope.LAN && guestCommands && !data.isAllowCommands(), "scoped publication must preserve independent guest and world permissions");
            publishedPort = port; publications++; return true;
        }
        public final Recipes recipes = new Recipes();
        public Recipes getRecipeManager() { return recipes; }
        final Player player;
        final ExecutorService executor = Executors.newSingleThreadExecutor(Agent.threads("fixture-server"));
        Server(Player player) { this.player = player; }
        public Server getPlayerList() { return this; }
        public Player getPlayer(UUID id) { return player.id.equals(id) ? player : null; }
        public void execute(Runnable action) { executor.execute(action); }
    }

    public static final class WorldData {
        public boolean isAllowCommands() { return false; }
    }

    public static void main(String[] arguments) throws Exception {
        Path root = Path.of(arguments[1]);
        switch (arguments[0]) {
            case "cancel" -> cancel(root);
            case "lan_publish" -> lanPublish(root, false);
            case "lan_stale" -> lanPublish(root, true);
            case "lan_scoped" -> lanPublish(root, false, true);
            case "lan_scoped_stale" -> lanPublish(root, true, true);
            case "lan_unsupported" -> lanUnsupported(root);
            case "stale" -> stale(root);
            case "remote" -> remote(root);
            case "connection_switch" -> connectionSwitch(root);
            case "unknown" -> unknown(root);
            case "bootstrap" -> bootstrap(root);
            case "inherited_mapping" -> inheritedMapping(root);
            case "named_inherited" -> namedInherited(root);
            case "class_initialization" -> classInitialization(root);
            case "unloaded_region" -> unloadedRegion(root);
            case "input_getter" -> inputGetter();
            case "input_modified_getter" -> inputModifiedGetter();
            case "input_authority" -> inputAuthority(root);
            case "input_timing" -> inputTiming(root, false, false);
            case "input_ui" -> inputTiming(root, true, false);
            case "input_physical_use" -> inputTiming(root, false, true);
            case "input_remote_release" -> inputRemoteRelease(root);
            case "respawn_alive" -> respawnAlive(root);
            case "ai_ingress" -> aiIngress(root);
            case "knowledge_recipes" -> knowledgeRecipes(root);
            case "knowledge_progress" -> knowledgeProgress(root);
            default -> throw new AssertionError("Unknown regression.");
        }
        System.out.println("PASS " + arguments[0]);
    }

    private static GameAccess create(Path root) throws Exception {
        return create(root, System::nanoTime);
    }
    private static GameAccess create(Path root, LongSupplier regionClock) throws Exception {
        return create(root, regionClock, false, false);
    }
    private static GameAccess create(Path root, LongSupplier regionClock, boolean scopedLan, boolean unsupportedLan) throws Exception {
        Client.instance = new Client();
        String owner = GameBridgeChecks.class.getName() + "$";
        String[][] members = {
            {"clientOptions", "Client", "options"}, {"clientScreen", "Client", "screen"}, {"clientRenderer", "Client", "renderer"}, {"clientPaused", "Client", "isPaused"},
            {"clientGameMode", "Client", "gameMode"}, {"gameReleaseUse", "GameMode", "releaseUsingItem"},
            {"rendererPick", "Renderer", "pick"}, {"clientAttack", "Client", "startAttack"}, {"clientUse", "Client", "startUseItem"},
            {"keyForward", "Options", "keyUp"}, {"keyUse", "Options", "keyUse"}, {"entityAlive", "Player", "isAlive"}, {"levelGameTime", "Level", "getGameTime"},
            {"keyIsDown", "KeyInput", "isDown"},
            {"clientInstance", "Client", "getInstance"}, {"clientPlayer", "Client", "player"}, {"clientLevel", "Client", "level"},
            {"clientServer", "Client", "getSingleplayerServer"}, {"clientLocal", "Client", "isLocalServer"}, {"clientConnection", "Client", "getConnection"},
            {"listenerConnection", "Listener", "getConnection"}, {"connectionMemory", "Connection", "isMemoryConnection"},
            {"entityUuid", "Player", "getUUID"}, {"entityX", "Player", "getX"}, {"entityY", "Player", "getY"}, {"entityZ", "Player", "getZ"},
            {"entityLevel", "Player", "level"}, {"entityHealth", "Player", "getHealth"}, {"entitySetHealth", "Player", "setHealth"},
            {"serverPlayerList", "Server", "getPlayerList"}, {"listPlayer", "Server", "getPlayer"},
            {"levelDimension", "Level", "dimension"}, {"keyLocation", "Key", "location"}, {"levelTime", "Level", "getDayTime"}, {"levelSetTime", "Level", "setDayTime"},
            {"levelChunks", "Level", "getChunkSource"}, {"chunkNow", "Level", "getChunkNow"}, {"levelLoaded", "Level", "hasChunkAt"},
            {"levelBlock", "Level", "getBlockState"}, {"stateBlock", "BlockState", "getBlock"}, {"registryKey", "Registry", "getKey"},
            {"registryBlock", "Registries", "BLOCK"}, {"registryEntity", "Registries", "ENTITY_TYPE"}, {"registryItem", "Registries", "ITEM"},
            {"levelEntities", "Level", "entitiesForRendering"}, {"playerInventory", "Player", "getInventory"}, {"inventorySize", "Player", "getContainerSize"},
            {"levelRegistryAccess", "Level", "registryAccess"}, {"serverRecipes", "Server", "getRecipeManager"}, {"recipesCollection", "Recipes", "getRecipes"},
            {"recipeId", "Recipe", "getId"}, {"recipeType", "Recipe", "getType"}, {"recipeIngredients", "Recipe", "getIngredients"}, {"recipeResult", "Recipe", "getResultItem"},
            {"recipeStation", "Recipe", "getToastSymbol"},
            {"recipeWidth", "ShapedRecipe", "getWidth"}, {"recipeHeight", "ShapedRecipe", "getHeight"},
            {"ingredientItems", "Ingredient", "getItems"}, {"stackItem", "Stack", "getItem"}, {"stackCount", "Stack", "getCount"}, {"stackName", "Stack", "getHoverName"},
            {"componentText", "Text", "getString"}
            , {"serverWorldData", "Server", "getWorldData"}, {"worldAllowCommands", "WorldData", "isAllowCommands"}
            , {"serverPublished", "Server", "isPublished"}, {"serverPort", "Server", "getPort"}, {"serverPublish", "Server", "publishServer"}
        };
        Properties bindings = new Properties();
        bindings.setProperty("clientClass", owner + "Client"); bindings.setProperty("clockApi", "false");
        bindings.setProperty("regionApi", "true"); bindings.setProperty("blockPosClass", owner + "Position");
        bindings.setProperty("type.net.minecraft.world.item.crafting.ShapedRecipe", owner + "ShapedRecipe");
        for (String[] member : members) {
            bindings.setProperty(member[0], member[2]); bindings.setProperty(member[0] + ".owner", owner + member[1]);
            for (var method : Class.forName(owner + member[1]).getDeclaredMethods()) if (method.getName().equals(member[2]))
                bindings.setProperty(member[0] + ".parameters", String.join(",", Arrays.stream(method.getParameterTypes()).map(Class::getName).toList()));
        }
        bindings.setProperty("serverPublish.parameters", scopedLan ? owner + "PublicationScope,int" : "java.lang.Object,boolean,int");
        if (scopedLan) {
            bindings.setProperty("lanScope", "LAN"); bindings.setProperty("lanScope.owner", owner + "PublicationScope");
        }
        if (unsupportedLan) bindings.setProperty("serverPublish", "missingPublishServer");
        Path file = root.resolve("bindings.properties");
        try (var output = Files.newOutputStream(file)) { bindings.store(output, "synthetic exact API bindings"); }
        Instrumentation instrumentation = (Instrumentation)Proxy.newProxyInstance(GameBridgeChecks.class.getClassLoader(), new Class<?>[]{Instrumentation.class},
            (proxy, method, values) -> { if (method.getName().equals("getAllLoadedClasses")) return new Class<?>[]{Client.class}; throw new UnsupportedOperationException(); });
        Bindings resolved = new Bindings(instrumentation, file.toString());
        fixtureBindings = resolved;
        Client.instance.probe(resolved);
        GameAccess access = new GameAccess(resolved, "a".repeat(32), "b".repeat(32), regionClock);
        access.connected();
        check(Boolean.TRUE.equals(access.snapshot().get("ready")), "initial world must be sampled through the actual client executor");
        return access;
    }

    private static Map<String, Object> edit(GameAccess game) throws Exception {
        var snapshot = game.snapshot();
        return Json.object("operationId", UUID.randomUUID().toString().replace("-", ""), "gameIncarnation", "b".repeat(32),
            "worldEpoch", snapshot.get("worldEpoch"), "dimension", snapshot.get("dimension"), "kind", "player.setHealth", "health", 13);
    }

    /** Review: LAN is a separate game effect, not a cheat command. Real executor barriers distinguish
     * preserving an existing publication from opening a replacement world with a stale capability. */
    private static void lanPublish(Path root, boolean stale) throws Exception {
        lanPublish(root, stale, false);
    }
    private static void lanPublish(Path root, boolean stale, boolean scopedLan) throws Exception {
        GameAccess game = create(root, System::nanoTime, scopedLan, false);
        var request = edit(game); request.remove("health"); request.put("kind", "world.publish");
        CountDownLatch release = stale ? blockServer() : null;
        try {
            game.edit(request); clientBarrier();
            if (stale) { Client.instance.level = new Level(); release.countDown(); }
            Client.instance.server.executor.submit(() -> { }).get(5, TimeUnit.SECONDS); clientBarrier();
            var operation = game.operation(Json.object("operationId", request.get("operationId")), false);
            if (stale) {
                check("rejected".equals(operation.get("state")), "stale LAN request must be rejected");
                check(Client.instance.server.publications == 0, "replacement world must not be published");
                return;
            }
            check("applied".equals(operation.get("state")), "local world must publish without enabling cheats");
            int port = Client.instance.server.publishedPort;
            check(port > 0 && Client.instance.server.publications == 1, "native listener publication must be observed");
            var second = edit(game); second.remove("health"); second.put("kind", "world.publish");
            game.edit(second); clientBarrier(); Client.instance.server.executor.submit(() -> { }).get(5, TimeUnit.SECONDS); clientBarrier();
            check(Client.instance.server.publishedPort == port && Client.instance.server.publications == 1, "existing native LAN must be reused without changing permissions");
        } finally { if (release != null) release.countDown(); }
    }

    /** Review: a missing fixed API is knowable before native publication. Reject it before applying,
     * preserving zero effects rather than leaving an unknown operation that blocks recovery. */
    private static void lanUnsupported(Path root) throws Exception {
        GameAccess game = create(root, System::nanoTime, true, true);
        var request = edit(game); request.remove("health"); request.put("kind", "world.publish");
        game.edit(request); clientBarrier(); Client.instance.server.executor.submit(() -> { }).get(5, TimeUnit.SECONDS); clientBarrier();
        var operation = game.operation(Json.object("operationId", request.get("operationId")), false);
        check("rejected".equals(operation.get("state")) && "GAME_BINDING_UNSUPPORTED".equals(operation.get("errorCode")), "unsupported publication must reject before effects");
        check(Client.instance.server.publications == 0 && Client.instance.server.publishedPort == 0, "unsupported publication must not open a listener");
    }

    /** Review: real dispatcher queries must reflect replaced loaded recipes, preserve ingredient alternatives and output counts,
     * and never misreport absent optional books as an empty successful result. Recipe API/model and executors are synthetic. */
    @SuppressWarnings("unchecked")
    private static void knowledgeRecipes(Path root) throws Exception {
        GameAccess game = create(root);
        try {
            Map<String, Object> query = new LinkedHashMap<>(edit(game));
            query.remove("operationId"); query.put("kind", "recipes"); query.put("search", "餐食");
            Map<String, Object> first = game.query(query);
            check("server_authoritative".equals(first.get("source")), "recipes must cross the actual owned server executor");
            Map<String, Object> data = (Map<String, Object>) first.get("data");
            List<Map<String, Object>> items = (List<Map<String, Object>>) data.get("items");
            check(items.size() == 1, "localized output search must find the effective recipe");
            Map<String, Object> result = (Map<String, Object>) items.get(0).get("output");
            check("fixture:meal".equals(result.get("itemId")) && Integer.valueOf(3).equals(result.get("count")), "effective output identity and count must survive");
            List<Map<String, Object>> ingredients = (List<Map<String, Object>>) items.get(0).get("ingredients");
            check(ingredients.size() == 2 && ((List<?>)ingredients.get(0).get("alternatives")).size() == 2, "repeated ingredient slots and tag alternatives must remain distinct");
            // A machine/type search must find custom recipe IDs whose output names do not mention that machine.
            for (String search : List.of("测试厨锅", "fixture:cooking")) {
                query.put("search", search);
                check(((List<?>)((Map<?, ?>)game.query(query).get("data")).get("items")).size() == 1, "machine and type search must use actual recipe metadata");
            }
            query.put("search", "餐食");
            // Ingredient names alone cannot distinguish a horizontal recipe from a vertical one.
            Client.instance.server.recipes.loaded = List.of(new ShapedRecipe());
            Map<?, ?> shaped = (Map<?, ?>)((List<?>)((Map<?, ?>)game.query(query).get("data")).get("items")).get(0);
            Map<?, ?> shape = (Map<?, ?>)shaped.get("shape");
            check(Integer.valueOf(2).equals(shape.get("width")) && Integer.valueOf(1).equals(shape.get("height"))
                && List.of(0, 1).equals(shape.get("ingredientIndexes")) && Boolean.TRUE.equals(shape.get("complete")), "native grid dimensions and ordered slots must remain actionable");
            // A large pack's unused native ingredient models must not be expanded for a one-row page.
            Recipe unrequested = new Recipe(); unrequested.unrequested = true;
            Client.instance.server.recipes.loaded = List.of(new Recipe(), unrequested);
            query.put("limit", 1L);
            Map<?, ?> bounded = (Map<?, ?>)game.query(query).get("data");
            check(Integer.valueOf(2).equals(bounded.get("total")) && Boolean.TRUE.equals(bounded.get("hasMore")), "paging must retain the full matching count without materializing later ingredients");
            Client.instance.server.recipes.loaded = List.of();
            check(((List<?>)((Map<?, ?>)game.query(query).get("data")).get("items")).isEmpty(), "a recipe reload must not return removed original recipes");
            query.put("kind", "books"); query.remove("search");
            Map<?, ?> books = (Map<?, ?>)game.query(query).get("data");
            check(Boolean.FALSE.equals(books.get("available")) && "api_not_available".equals(books.get("reason")), "missing Patchouli must remain distinguishable from an empty loaded book");
            query.put("kind", "quests");
            check(Boolean.FALSE.equals(((Map<?, ?>)game.query(query).get("data")).get("available")), "missing FTB must not invent player progress");
            Client.instance.connection = new Connection(); Client.instance.connection.memory = false;
            game.snapshot(); query.put("kind", "recipes"); query.put("worldEpoch", game.snapshot().get("worldEpoch"));
            try { game.query(query); throw new AssertionError("remote recipes cannot be served from the integrated server model"); }
            catch (IllegalArgumentException expected) { check("GAME_QUERY_UNAVAILABLE".equals(expected.getMessage()), "remote unavailability must stay explicit"); }
        } finally { game.disconnected(); Client.instance.executor.shutdownNow(); Client.instance.server.executor.shutdownNow(); }
    }

    /** Review: this optional-API process protects team selection, fresh progress, localization, and unlock boundaries.
     * Optional mod classes are synthetic and only added to this scenario's classpath; dispatcher and bindings stay real. */
    @SuppressWarnings("unchecked")
    private static void knowledgeProgress(Path root) throws Exception {
        GameAccess game = create(root);
        Class<?> teams = Class.forName("dev.ftb.mods.ftbteams.api.FTBTeamsAPI");
        Class<?> quests = Class.forName("dev.ftb.mods.ftbquests.quest.ServerQuestFile");
        Class<?> books = Class.forName("vazkii.patchouli.common.book.BookRegistry");
        teams.getField("player").set(null, Client.instance.player.id);
        try {
            Map<String, Object> query = new LinkedHashMap<>(edit(game)); query.remove("operationId");
            query.put("kind", "quests"); query.put("search", "混合饲料");
            Map<String, Object> data = (Map<String, Object>)game.query(query).get("data");
            Map<String, Object> quest = ((List<Map<String, Object>>)data.get("items")).get(0);
            check("cccccccc-cccc-cccc-cccc-cccccccccccc".equals(quest.get("teamId")), "progress must belong to the resolved player team");
            check(((List<?>)quest.get("dependencies")).contains("0000000000000001"), "real prerequisite IDs must be retained");
            Map<?, ?> task = (Map<?, ?>)((List<?>)quest.get("tasks")).get(0);
            check("2".equals(task.get("progress")) && "5".equals(task.get("maximum")), "actual unfinished progress must not become zero or completed");
            // Review: progress/title alone cannot explain a generic task's target. The native serializer's unknown
            // field and item identifier must survive without a pack-specific item-task schema.
            check("ftbquests:item".equals(task.get("type")) && task.get("definitionSnbt").toString().contains("farmersdelight:animal_feed")
                && task.get("definitionSnbt").toString().contains("custom_field"), "native task target and unknown details must be preserved");
            quests.getField("completed").setBoolean(null, true);
            quest = ((List<Map<String, Object>>)((Map<?, ?>)game.query(query).get("data")).get("items")).get(0);
            check(Boolean.TRUE.equals(quest.get("completed")), "later progress must not reuse a cached completion state");
            quests.getField("hidden").setBoolean(null, true); query.remove("search");
            quest = ((List<Map<String, Object>>)((Map<?, ?>)game.query(query).get("data")).get("items")).get(0);
            check(Boolean.TRUE.equals(quest.get("detailsHidden")) && ((List<?>)quest.get("description")).isEmpty(), "native quest visibility must hide task details");
            quests.getField("missing").setBoolean(null, true);
            check(Boolean.FALSE.equals(((Map<?, ?>)game.query(query).get("data")).get("available")), "missing team data must remain unavailable, never synthesized");
            // Review: remote progression is read-only synchronized data, even when a stale local server object exists.
            // This extends the same scope test; distinct fixture progress detects accidental use of server state.
            Client.instance.connection = new Connection(); Client.instance.connection.memory = false;
            query.put("worldEpoch", game.snapshot().get("worldEpoch"));
            Map<String, Object> synchronizedResult = game.query(query);
            quest = ((List<Map<String, Object>>)((Map<?, ?>)synchronizedResult.get("data")).get("items")).get(0);
            task = (Map<?, ?>)((List<?>)quest.get("tasks")).get(0);
            check("client_synchronized".equals(synchronizedResult.get("source")) && "3".equals(task.get("progress")), "remote queries must use the known player's synchronized team progress");
            query.put("kind", "books"); query.put("search", "混合饲料");
            Map<String, Object> bookResult = game.query(query);
            check("client_synchronized".equals(bookResult.get("source")), "Patchouli content must be queried on the client executor");
            List<?> entries = (List<?>)((Map<?, ?>)bookResult.get("data")).get("items");
            check(entries.size() == 1 && entries.get(0).toString().contains("本包混合饲料说明"), "search must use the loaded localized book text");
            books.getField("pageLocked").setBoolean(null, true);
            check(((List<?>)((Map<?, ?>)game.query(query).get("data")).get("items")).isEmpty(), "locked page text must not leak through search");
            books.getField("pageLocked").setBoolean(null, false); books.getField("locked").setBoolean(null, true);
            check(((List<?>)((Map<?, ?>)game.query(query).get("data")).get("items")).isEmpty(), "locked book entries must not be exposed");
        } finally { game.disconnected(); Client.instance.executor.shutdownNow(); Client.instance.server.executor.shutdownNow(); }
    }

    // Review: exercises the production request codec and game executor with a real pending client task.
    // A world switch before sampling must not send a prompt to the launcher under the old save key;
    // disconnect must complete pending callers without sending their requests again.
    private static void aiIngress(Path root) throws Exception {
        var game = create(root);
        var bytes = new java.io.ByteArrayOutputStream();
        var worker = Executors.newSingleThreadExecutor(Agent.threads("fixture-ai"));
        var link = new AiLink(new Wire(new java.io.ByteArrayInputStream(new byte[0]), bytes), game, worker);
        try {
            int world = System.identityHashCode(Client.instance.level);
            String input = Json.encode(Json.object("action", "open", "worldKey", "local:fixture", "clientWorldId", world));
            var pending = link.request(input);
            worker.submit(() -> {}).get(5, TimeUnit.SECONDS);
            var request = new Wire(new java.io.ByteArrayInputStream(bytes.toByteArray()), new java.io.ByteArrayOutputStream()).read();
            check("launcher.ai".equals(request.get("method")), "the game request must use the authenticated AI ingress");
            @SuppressWarnings("unchecked") var parameters = (Map<String, Object>)request.get("params");
            @SuppressWarnings("unchecked") var observed = (Map<String, Object>)parameters.get("snapshot");
            check(Boolean.TRUE.equals(observed.get("ready")), "the UI must receive a real executor snapshot");
            check(!parameters.containsKey("clientWorldId"), "the process-local caller guard must not become launcher state");
            link.response(Json.object("jsonrpc", "2.0", "id", request.get("id"), "result", Json.object("mode", "safe", "turns", List.of())));
            check(pending.get(5, TimeUnit.SECONDS).contains("safe"), "the exact response must complete its game caller");

            int written = bytes.size();
            CountDownLatch reached = new CountDownLatch(1), release = new CountDownLatch(1);
            Client.instance.executor.execute(() -> { reached.countDown(); try { release.await(); } catch (InterruptedException error) { Thread.currentThread().interrupt(); } });
            check(reached.await(5, TimeUnit.SECONDS), "the client executor must actually be held at the world transition");
            var stale = link.request(input);
            Client.instance.level = new Level();
            Client.instance.player = new Player(Client.instance.level);
            release.countDown();
            try { stale.get(5, TimeUnit.SECONDS); throw new AssertionError("a stale caller must not bind to the replacement world"); }
            catch (ExecutionException expected) { }
            check(bytes.size() == written, "world mismatch must reject before any launcher submission");

            var last = link.request(Json.encode(Json.object("action", "open", "worldKey", "local:next", "clientWorldId", System.identityHashCode(Client.instance.level))));
            worker.submit(() -> {}).get(5, TimeUnit.SECONDS);
            written = bytes.size();
            link.close();
            try { last.get(5, TimeUnit.SECONDS); throw new AssertionError("disconnect must resolve pending callers"); }
            catch (ExecutionException expected) { }
            check(bytes.size() == written, "disconnect must never replay an uncertain game request");
        } finally { link.close(); worker.shutdownNow(); game.disconnected(); }
    }

    // Review: define the rewritten class in a real JVM, then test physical input and live predicate
    // independently. Returning the original bytes or overwriting physical input fails these oracles.
    private static void inputGetter() throws Exception {
        String name = KeyInput.class.getName();
        byte[] original;
        try (var resource = GameBridgeChecks.class.getResourceAsStream("/" + name.replace('.', '/') + ".class")) { original = resource.readAllBytes(); }
        check(KeyInputTransformer.rewrite(original, "complicated") == null, "unsupported control flow must remain unchanged");
        byte[] transformed = KeyInputTransformer.rewrite(original, "isDown");
        check(transformed != null, "the exact getter must be hooked");
        class Loader extends ClassLoader { Class<?> define() { return defineClass(name, transformed, 0, transformed.length); } }
        Class<?> type = new Loader().define(); Object key = type.getConstructor().newInstance();
        var getter = type.getMethod("isDown"); var physical = type.getField("physical");
        java.util.concurrent.atomic.AtomicBoolean allowed = new java.util.concurrent.atomic.AtomicBoolean(true);
        KeyHooks.install(Map.of(key, allowed::get));
        check(Boolean.TRUE.equals(getter.invoke(key)), "native getter must observe the lease");
        allowed.set(false); check(Boolean.FALSE.equals(getter.invoke(key)), "revocation must apply without polling");
        physical.setBoolean(key, true); check(Boolean.TRUE.equals(getter.invoke(key)), "physical input must survive lease revocation");
        KeyHooks.clear(); check(Boolean.TRUE.equals(getter.invoke(key)), "clearing the bridge must not clear physical keys");
    }

    // Review: Xaero's ModifyReturnValue changes the native getter into this branch-free
    // call shape. JVM verification plus independent physical/modifier/lease results catch
    // rejecting decorated getters, discarding a mod, or hooking an unrelated method.
    private static void inputModifiedGetter() throws Exception {
        String name = KeyInput.class.getName();
        byte[] original;
        try (var resource = GameBridgeChecks.class.getResourceAsStream("/" + name.replace('.', '/') + ".class")) { original = resource.readAllBytes(); }
        for (String variant : List.of("modified", "modifiedTwice", "modifiedBySuper", "mixinSwap")) {
            String method = variant.equals("mixinSwap") ? "modified" : variant;
            byte[] source = variant.equals("mixinSwap") ? mixinReturnModifier(original) : original;
            byte[] transformed = KeyInputTransformer.rewrite(source, method);
            check(transformed != null, "native return-value modifiers must remain hookable");
            class Loader extends ClassLoader { Class<?> define() { return defineClass(name, transformed, 0, transformed.length); } }
            Class<?> type = new Loader().define(); Object key = type.getConstructor().newInstance();
            var getter = type.getMethod(method); var physical = type.getField("physical");
            var modifier = type.getField("modifierDown"); var calls = type.getField("modifierCalls");
            java.util.concurrent.atomic.AtomicBoolean allowed = new java.util.concurrent.atomic.AtomicBoolean(true);
            KeyHooks.install(Map.of(key, allowed::get));
            check(Boolean.TRUE.equals(getter.invoke(key)), "the modified getter must observe the live lease");
            check(calls.getInt(key) == (method.equals("modifiedTwice") ? 2 : 1), "every original modifier must still execute");
            check(Boolean.FALSE.equals(type.getMethod("isDown").invoke(key)), "unselected getters must remain untouched");
            allowed.set(false); check(Boolean.FALSE.equals(getter.invoke(key)), "revocation must apply without a tick");
            modifier.setBoolean(key, true); check(Boolean.TRUE.equals(getter.invoke(key)), "mod-provided physical state must survive revocation");
            modifier.setBoolean(key, false); physical.setBoolean(key, true);
            check(Boolean.TRUE.equals(getter.invoke(key)), "native physical input must survive revocation");
            KeyHooks.clear(); check(Boolean.TRUE.equals(getter.invoke(key)), "clearing the bridge must preserve physical input");
        }
    }

    private static byte[] mixinReturnModifier(byte[] original) {
        // Actual installed MixinExtras0.5.5 inserts ALOAD_0/SWAP at IRETURN,
        // unlike javac's receiver-first expression. Keep the fixture CP and method
        // intact, changing only this verified9-byte getter into the real10-byte shape.
        int start = -1;
        for (int i = 12; i + 9 <= original.length; i++) {
            if (original[i] == 0x2a && original[i + 1] == 0x2a && (original[i + 2] & 255) == 0xb4
                && (original[i + 5] & 255) == 0xb6 && (original[i + 8] & 255) == 0xac
                && java.nio.ByteBuffer.wrap(original, i - 4, 4).getInt() == 9) {
                check(start == -1, "the return-modifier fixture must be unambiguous"); start = i;
            }
        }
        check(start != -1, "the compiler must retain the expected native modifier fixture");
        byte[] result = new byte[original.length + 1];
        System.arraycopy(original, 0, result, 0, start);
        result[start] = 0x2a;
        System.arraycopy(original, start + 2, result, start + 1, 3);
        result[start + 4] = 0x2a; result[start + 5] = 0x5f;
        System.arraycopy(original, start + 5, result, start + 6, 4);
        System.arraycopy(original, start + 9, result, start + 10, original.length - start - 9);
        java.nio.ByteBuffer.wrap(result, start - 4, 4).putInt(10);
        int length = java.nio.ByteBuffer.wrap(original, start - 12, 4).getInt();
        java.nio.ByteBuffer.wrap(result, start - 12, 4).putInt(length + 1);
        return result;
    }

    // Review: hold a production input lease at a frozen game tick, then replace connection authority
    // before any lifecycle poll. This specifically detects a remote write leak between polls.
    private static void inputAuthority(Path root) throws Exception {
        GameAccess access = create(root); KeyHooks.transformed();
        var request = edit(access); request.remove("health"); request.put("kind", "player.control");
        request.put("control", Json.object("ticks", 100L, "keys", List.of("forward")));
        access.edit(Json.parse(Json.encode(request))); clientBarrier(); serverBarrier(); clientBarrier();
        Client.instance.executor.submit(() -> {
            // No lifecycle task can interleave this authority change and the immediate getter read.
            Object key = Client.instance.options.keyUp;
            check(KeyHooks.resolve(key, false), "the local lease must first be active");
            Client.instance.connection.memory = false;
            check(!KeyHooks.resolve(key, false), "remote connection must revoke at the native read boundary");
            check(KeyHooks.resolve(key, true), "remote physical input still belongs to the player");
            Client.instance.connection.memory = true; access.disconnected();
            check(!KeyHooks.resolve(key, false), "transport loss must revoke without a game tick");
            access.connected();
            check(!KeyHooks.resolve(key, false), "reauthentication cannot revive an old input lease");
        }).get(5, TimeUnit.SECONDS);
        KeyHooks.clear();
    }

    // Review: hold the client executor so the lifecycle observer cannot refresh its cached world.
    // The old connection remains a memory connection; only a fresh pointer check prevents the write.
    private static void connectionSwitch(Path root) throws Exception {
        GameAccess access = create(root); CountDownLatch release = blockServer();
        var request = edit(access); access.edit(request); clientBarrier();
        Client client = Client.instance;
        client.executor.submit(() -> {
            try {
                Connection replacement = new Connection(); replacement.memory = false; client.connection = replacement;
                release.countDown(); client.server.executor.submit(() -> { }).get(5, TimeUnit.SECONDS);
                var result = access.operation(Json.object("operationId", request.get("operationId")), false);
                check(result.get("state").equals("rejected") && result.get("errorCode").equals("GAME_WORLD_STALE"), "an uncached connection switch must reject before mutation");
                check(client.player.writes == 0, "the old local player must remain unchanged");
            } catch (Exception error) { throw new RuntimeException(error); }
        }).get(5, TimeUnit.SECONDS);
    }

    // Review: advance the real lease at exact game ticks on its client executor. Delaying movement
    // together with use, firing use early, or leaving the key held after completion each fails independently.
    private static void inputTiming(Path root, boolean interruptWithScreen, boolean physicalUse) throws Exception {
        GameAccess access = create(root); KeyHooks.transformed();
        var request = edit(access); request.remove("health"); request.put("kind", "player.control");
        request.put("control", Json.object("ticks", 6L, "interactionDelayTicks", 5L, "keys", List.of("forward", "use")));
        GameAccess.Edit edit = GameAccess.Edit.read(Json.parse(Json.encode(request)));
        GameAccess.Operation operation = new GameAccess.Operation(edit); operation.state = "applying";
        Client client = Client.instance;
        var world = new GameAccess.World(client, client.level, client.player, client.server, client.connection, client.player.id, "1", "minecraft:overworld", true, "1");
        GamePlay play = new GamePlay(fixtureBindings);
        play.connected();
        client.executor.submit(() -> {
            try {
                play.applyClient(world, operation);
                if (interruptWithScreen) {
                    // The screen opens before the delayed action; an isDown-only guard would still
                    // let advance invoke the initial native use behind the new UI.
                    client.screen = new Object(); client.level.ticks = 5; play.advance(world);
                    check(client.uses == 0 && !KeyHooks.resolve(client.options.keyUse, false), "opening a screen must suppress delayed native use");
                    check(operation.result.get("stopReason").equals("screen_open"), "interruption must be observable in the exact receipt");
                    return;
                }
                client.level.ticks = 4; play.advance(world);
                check(KeyHooks.resolve(client.options.keyUp, false), "movement starts before the delayed interaction");
                check(!KeyHooks.resolve(client.options.keyUse, false) && client.uses == 0, "use must wait for its exact tick");
                client.level.ticks = 5; play.advance(world);
                check(KeyHooks.resolve(client.options.keyUse, false) && client.uses == 1, "the native initial use occurs once at the requested tick");
                // A real held key still owns the native item use when the bridge lease ends.
                client.options.keyUse.physical = physicalUse;
                client.level.ticks = 6; play.advance(world);
                check(!KeyHooks.resolve(client.options.keyUse, false) && client.gameMode.releases == (physicalUse ? 0 : 1), "completion must release only the bridge-owned native use action");
                check(client.options.keyUse.isDown() == physicalUse, "physical input must retain its independent ownership");
                check(operation.state.equals("applied"), "the retained lease receipt must finish");
            } catch (Exception error) { throw new RuntimeException(error); }
        }).get(5, TimeUnit.SECONDS);
    }

    // Review: an already-alive player is a known no-op, not an uncertain native write. Rejecting
    // before applying avoids permanently blocking later AI actions behind a false unknown effect.
    private static void respawnAlive(Path root) throws Exception {
        GameAccess access = create(root); var request = edit(access); request.remove("health"); request.put("kind", "player.respawn");
        access.edit(request); clientBarrier(); serverBarrier(); clientBarrier();
        var result = access.operation(Json.object("operationId", request.get("operationId")), false);
        check(result.get("state").equals("rejected") && result.get("errorCode").equals("GAME_PLAYER_ALREADY_ALIVE"), "alive precondition must remain a known rejection");
    }

    // Review: use starts on a local world, then only the live connection changes. Clearing the key
    // is safe, but sending releaseUsingItem through the newly active remote game mode is a write.
    private static void inputRemoteRelease(Path root) throws Exception {
        GameAccess access = create(root); KeyHooks.transformed();
        var request = edit(access); request.remove("health"); request.put("kind", "player.control");
        request.put("control", Json.object("ticks", 6L, "keys", List.of("use")));
        var operation = new GameAccess.Operation(GameAccess.Edit.read(Json.parse(Json.encode(request)))); operation.state = "applying";
        Client client = Client.instance;
        var world = new GameAccess.World(client, client.level, client.player, client.server, client.connection, client.player.id, "1", "minecraft:overworld", true, "1");
        GamePlay play = new GamePlay(fixtureBindings); play.connected();
        client.executor.submit(() -> {
            try {
                play.applyClient(world, operation); client.level.ticks = 1; play.advance(world);
                check(client.uses == 1, "native local use must first be active");
                Connection remote = new Connection(); remote.memory = false; client.connection = remote;
                var changed = new GameAccess.World(client, client.level, client.player, client.server, remote, client.player.id, "2", "minecraft:overworld", false, null);
                play.advance(changed);
                check(client.gameMode.releases == 0, "world revocation must not send a release packet to the new server");
                check(!KeyHooks.resolve(client.options.keyUse, false), "the bridge key must still be cleared");
            } catch (Exception error) { throw new RuntimeException(error); }
        }).get(5, TimeUnit.SECONDS);
    }
    private static void clientBarrier() throws Exception { Client.instance.executor.submit(() -> { }).get(5, TimeUnit.SECONDS); }
    private static void serverBarrier() throws Exception { Client.instance.server.executor.submit(() -> { }).get(5, TimeUnit.SECONDS); }
    private static CountDownLatch blockServer() throws Exception {
        CountDownLatch entered = new CountDownLatch(1), release = new CountDownLatch(1);
        Client.instance.server.execute(() -> { entered.countDown(); try { release.await(5, TimeUnit.SECONDS); } catch (InterruptedException error) { throw new RuntimeException(error); } });
        check(entered.await(5, TimeUnit.SECONDS), "server barrier did not enter"); return release;
    }
    private static void cancel(Path root) throws Exception {
        var game = create(root); var request = edit(game); var gate = blockServer();
        try {
            game.edit(request); clientBarrier();
            check(game.operation(request, true).get("state").equals("canceled"), "queued operation must cancel");
        } finally { gate.countDown(); }
        serverBarrier();
        check(Client.instance.player.health == 20 && Client.instance.player.writes == 0, "cancel must preserve actual player data");
        check(game.edit(request).get("state").equals("canceled"), "duplicate canceled operation must not reschedule");
    }
    private static void stale(Path root) throws Exception {
        var game = create(root); var request = edit(game); var old = Client.instance.player; var gate = blockServer();
        try {
            game.edit(request); clientBarrier();
            Client.instance.executor.submit(() -> { Client.instance.level = new Level(); Client.instance.player = new Player(Client.instance.level); }).get(5, TimeUnit.SECONDS);
        } finally { gate.countDown(); }
        serverBarrier();
        check(game.operation(request, false).get("state").equals("rejected"), "world replacement after queueing must reject");
        check(old.writes == 0 && old.health == 20 && Client.instance.player.health == 20, "neither old nor new world may be modified");
        check(!request.get("worldEpoch").equals(game.snapshot().get("worldEpoch")), "world generation must advance");
    }
    private static void remote(Path root) throws Exception {
        var game = create(root); var request = edit(game);
        Client.instance.connection.memory = false;
        game.edit(request); clientBarrier(); serverBarrier();
        check(game.operation(request, false).get("errorCode").equals("SERVER_READ_ONLY"), "a server object and local flag do not prove integrated ownership");
        check(Client.instance.player.writes == 0 && Client.instance.player.health == 20, "remote refusal must preserve player data");
    }
    private static void unknown(Path root) throws Exception {
        var game = create(root); var request = edit(game);
        Client.instance.player.failReadback = true;
        game.edit(request); clientBarrier(); serverBarrier();
        check(game.operation(request, false).get("state").equals("unknown"), "failed post-write readback must retain uncertainty");
        game.edit(request); clientBarrier(); serverBarrier();
        check(Client.instance.player.writes == 1 && Client.instance.player.health == 13, "unknown outcome must never replay the actual mutation");
    }
    private static void unloadedRegion(Path root) throws Exception {
        // Chunk classification must not race the production 4 ms budget under parallel test load.
        var ticks = new AtomicLong(); var step = new AtomicLong();
        var game = create(root, () -> ticks.getAndAdd(step.get())); var observed = game.snapshot();
        Map<String, Object> query = Json.object("gameIncarnation", "b".repeat(32), "worldEpoch", observed.get("worldEpoch"), "dimension", observed.get("dimension"),
            "x", 1L, "y", 64L, "z", 3L, "sizeX", 2L, "sizeY", 1L, "sizeZ", 1L);
        Map<String, Object> result = game.region(query);
        List<?> cells = (List<?>)result.get("blocks");
        check(cells.size() == 2, "both requested cells must be accounted for");
        for (Object cell : cells) check(((Map<?, ?>)cell).get("state").equals("not_loaded") && ((Map<?, ?>)cell).get("blockId") == null, "absent chunks must not be reported as air");
        check(Client.instance.level.blockReads == 0 && Client.instance.level.chunkReads == 1, "never inspect an absent chunk; cache the native no-create lookup per request");
        Client.instance.level.loaded = true;
        result = game.region(query);
        check(((Map<?, ?>)((List<?>)result.get("blocks")).get(0)).get("blockId").equals("minecraft:stone"), "a subsequently synchronized chunk must become readable");
        check(Client.instance.level.blockReads == 2 && Client.instance.level.chunkReads == 2, "loaded state must not be cached between requests");
        step.set(TimeUnit.MILLISECONDS.toNanos(5));
        result = game.region(query);
        check(Boolean.TRUE.equals(result.get("truncated")) && ((List<?>)result.get("blocks")).isEmpty(), "expiry must return explicit partial evidence without reading a cell");
        check(Client.instance.level.blockReads == 2, "expired reads must not touch the game cache");
    }
    private static void bootstrap(Path root) throws Exception {
        Properties names = new Properties();
        names.setProperty("clockApi", "false");
        names.setProperty("clientClass", Client.class.getName());
        Path file = root.resolve("bootstrap.properties");
        try (var output = Files.newOutputStream(file)) { names.store(output, "bootstrap regression"); }
        Instrumentation instrumentation = (Instrumentation)Proxy.newProxyInstance(GameBridgeChecks.class.getClassLoader(), new Class<?>[]{Instrumentation.class},
            (proxy, method, values) -> { if (method.getName().equals("getAllLoadedClasses")) return new Class<?>[]{net.fabricmc.loader.api.FabricLoader.class, Client.class}; throw new UnsupportedOperationException(); });
        check(new Bindings(instrumentation, file.toString()).client() == null, "bridge must wait without calling Fabric or defining the game entrypoint");
        check(net.fabricmc.loader.api.FabricLoader.calls == 0, "Fabric initialization was triggered before the entrypoint barrier");
    }
    public interface UuidSource { UUID actualUuid(); }
    public interface TickSource { default long gameTime() { return 73; } }
    public static final class TickOwner implements TickSource { }

    // Review: named versions may move the exact API to a default interface without changing its
    // signature. Resolve that inherited method, but do not guess another zero-argument long getter.
    private static void namedInherited(Path root) throws Exception {
        create(root);
        Properties names = new Properties(); names.setProperty("clockApi", "false");
        names.setProperty("clientClass", Client.class.getName()); names.setProperty("clientInstance.owner", Client.class.getName()); names.setProperty("clientInstance", "getInstance");
        names.setProperty("levelGameTime.owner", TickOwner.class.getName()); names.setProperty("levelGameTime", "gameTime");
        names.setProperty("absent.owner", TickOwner.class.getName()); names.setProperty("absent", "missingTime");
        Path file = root.resolve("named-inherited.properties");
        try (var output = Files.newOutputStream(file)) { names.store(output, "exact inherited named API"); }
        Instrumentation instrumentation = (Instrumentation)Proxy.newProxyInstance(GameBridgeChecks.class.getClassLoader(), new Class<?>[]{Instrumentation.class},
            (proxy, method, values) -> { if (method.getName().equals("getAllLoadedClasses")) return new Class<?>[]{Client.class}; throw new UnsupportedOperationException(); });
        Bindings bindings = new Bindings(instrumentation, file.toString()); Client.instance.probe(bindings);
        check(bindings.call(new TickOwner(), "levelGameTime").equals(73L), "named inherited default method must resolve exactly");
        try { bindings.call(new TickOwner(), "absent"); throw new AssertionError("missing method was guessed"); }
        catch (NoSuchMethodException expected) { }
    }
    public static final class UuidOwner implements UuidSource {
        final UUID uuid = UUID.randomUUID();
        public UUID actualUuid() { return uuid; }
    }
    private static void inheritedMapping(Path root) throws Exception {
        Client.instance = new Client();
        Properties names = new Properties(); names.setProperty("clockApi", "false");
        names.setProperty("clientClass", "official.Client"); names.setProperty("clientInstance.owner", "official.Client");
        names.setProperty("clientInstance", "getInstance"); names.setProperty("clientInstance.descriptor", "()Lofficial/Client;");
        names.setProperty("entityUuid.owner", "official.Owner"); names.setProperty("entityUuid", "obfUuid"); names.setProperty("entityUuid.descriptor", "()Ljava/util/UUID;");
        Path file = root.resolve("inherited.properties");
        try (var output = Files.newOutputStream(file)) { names.store(output, "virtual mapping regression"); }
        net.fabricmc.loader.api.FabricLoader.initialized = true;
        net.fabricmc.loader.api.FabricLoader.resolver = new net.fabricmc.loader.api.MappingResolver() {
            public String mapClassName(String namespace, String name) { return name.equals("official.Client") ? Client.class.getName() : name.equals("official.Owner") ? UuidOwner.class.getName() : name; }
            public String unmapClassName(String namespace, String name) { return name.equals(UuidSource.class.getName()) ? "official.Interface" : name; }
            public String mapMethodName(String namespace, String owner, String name, String descriptor) { return owner.equals("official.Interface") && name.equals("obfUuid") && descriptor.equals("()Ljava/util/UUID;") ? "actualUuid" : name; }
            public String mapFieldName(String namespace, String owner, String name, String descriptor) { return name; }
        };
        Instrumentation instrumentation = (Instrumentation)Proxy.newProxyInstance(GameBridgeChecks.class.getClassLoader(), new Class<?>[]{Instrumentation.class},
            (proxy, method, values) -> { if (method.getName().equals("getAllLoadedClasses")) return new Class<?>[]{net.fabricmc.loader.api.FabricLoader.class, net.minecraft.client.main.Main.class, Client.class}; throw new UnsupportedOperationException(); });
        Bindings bindings = new Bindings(instrumentation, file.toString());
        check(Client.instance.probe(bindings) == Client.instance, "runtime class mapping did not resolve");
        UuidOwner target = new UuidOwner();
        check(bindings.call(target, "entityUuid").equals(target.uuid), "interface-owned exact mapping was not applied to its actual implementation");
    }
    public static final class InitializationWitness { static boolean initialized; }
    public static final class UninitializedClient {
        static { InitializationWitness.initialized = true; }
        public static UninitializedClient getInstance() { return null; }
    }
    private static void classInitialization(Path root) throws Exception {
        Properties names = new Properties(); names.setProperty("clockApi", "false");
        names.setProperty("clientClass", UninitializedClient.class.getName()); names.setProperty("clientInstance.owner", UninitializedClient.class.getName()); names.setProperty("clientInstance", "getInstance");
        Path file = root.resolve("not-initialized.properties");
        try (var output = Files.newOutputStream(file)) { names.store(output, "no class initialization regression"); }
        Instrumentation instrumentation = (Instrumentation)Proxy.newProxyInstance(GameBridgeChecks.class.getClassLoader(), new Class<?>[]{Instrumentation.class},
            (proxy, method, values) -> { if (method.getName().equals("getAllLoadedClasses")) return new Class<?>[]{UninitializedClient.class}; throw new UnsupportedOperationException(); });
        check(new Bindings(instrumentation, file.toString()).client() == null, "uninitialized game must remain unavailable");
        check(!InitializationWitness.initialized, "class enumeration caused premature game initialization");
    }
    private static void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
}
