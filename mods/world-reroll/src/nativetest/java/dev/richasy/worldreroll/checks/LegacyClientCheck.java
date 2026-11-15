package dev.richasy.worldreroll.checks;

import dev.richasy.worldreroll.RerollController;
import dev.richasy.worldreroll.checks.mixin.WorldCreateAccess;
import dev.richasy.worldreroll.checks.mixin.InventoryAccess;
import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;
import io.github.richasy.bettertools.CubeState;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.CompletableFuture;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.MultiLineEditBox;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import net.minecraft.client.gui.screens.worldselection.WorldCreationUiState;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.GameRules;
import net.minecraft.world.level.storage.LevelResource;

/**
 * Review: compilation and synthetic Core packages cannot detect missing Mixin targets, old native editor events,
 * durability recovery erasing the offhand, or the old world-data/ticket lifecycle. This runs normal remapped product
 * JARs, the real client/server and native callbacks. It creates and deletes only its own guarded temporary worlds.
 * Public skin service availability is reported separately and is never inferred from a Microsoft login success.
 */
public final class LegacyClientCheck implements ClientModInitializer {
    private Path root, source;
    private int stage, ticks;
    private long deadline, seed;
    private Object previousServer;
    private CompletableFuture<Void> tools;
    private CompletableFuture<net.minecraft.client.resources.PlayerSkin> skin;
    private boolean customSkin;
    private boolean tooltipDrawn;
    private CompletableFuture<Boolean> selection;
    private CompletableFuture<net.minecraft.core.BlockPos> village;

    @Override public void onInitializeClient() {
        String requested = System.getProperty("rodel.validation.root");
        if (requested == null) throw new IllegalStateException("Native validation requires an explicit isolated root");
        root = Path.of(requested).toAbsolutePath().normalize();
        var game = Minecraft.getInstance().gameDirectory.toPath().toAbsolutePath().normalize();
        if (!root.equals(game) || !Files.exists(root.resolve(".native-validation")))
            throw new IllegalStateException("Refusing validation outside its marked isolated game directory");
        deadline = System.nanoTime() + java.time.Duration.ofMinutes(8).toNanos();
        ClientTickEvents.END_CLIENT_TICK.register(this::tick);
    }

    private void tick(Minecraft client) {
        if (stage == 99) return;
        try {
            require(System.nanoTime() < deadline, "Native client validation timed out at stage " + stage);
            ticks++;
            switch (stage) {
                case 0 -> title(client);
                case 1 -> create(client);
                case 2 -> entered(client);
                case 3 -> toolsCompleted(client);
                case 4 -> ai(client);
                case 5 -> reroll(client);
                case 6 -> replacement(client);
                case 7 -> inventory(client);
                case 8 -> selectionCompleted(client);
                case 9 -> villageCompleted(client);
                default -> throw new IllegalStateException("Unknown check stage");
            }
        } catch (Throwable error) {
            error.printStackTrace();
            finish(client, false, error.toString());
        }
    }

    private void title(Minecraft client) {
        if (!(client.screen instanceof TitleScreen) || client.getOverlay() != null || ticks < 20) return;
        require(!client.getWindow().isFullscreen(), "Saved fullscreen must be overridden by the windowed launch choice");
        if (skin == null) skin = client.getSkinManager().getOrLoad(client.getGameProfile());
        if (!skin.isDone()) return;
        customSkin = skin.join().textureUrl() != null;
        if (Boolean.getBoolean("rodel.validation.expectSkin")) require(customSkin, "The actual game still resolved a default skin");
        CreateWorldScreen.openFresh(client, client.screen);
        next(1);
    }

    private void create(Minecraft client) {
        if (!(client.screen instanceof CreateWorldScreen screen)) return;
        var state = screen.getUiState();
        state.setName("Native 1.21.1 Check");
        state.setGameMode(WorldCreationUiState.SelectedGameMode.CREATIVE);
        state.setAllowCommands(true);
        state.setGenerateStructures(true);
        state.setSeed("42");
        state.getGameRules().getRule(GameRules.RULE_KEEPINVENTORY).set(true, null);
        ((WorldCreateAccess) screen).check$create();
        next(2);
    }

    private void entered(Minecraft client) throws Exception {
        var server = client.getSingleplayerServer();
        if (client.player == null || server == null || client.screen != null) { ticks = 0; return; }
        if (ticks < 40) return;
        if (Boolean.getBoolean("rodel.validation.expectSkin"))
            require(client.player.getSkin().textureUrl() != null, "The actual in-world player still uses a default skin");
        source = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
        require(source.getParent().equals(root.resolve("saves")) && source.getFileName().toString().startsWith("Native"), "World ownership mismatch");
        previousServer = server;
        seed = server.getWorldData().worldGenOptions().seed();
        Files.createDirectories(source.resolve("datapacks"));
        Files.writeString(source.resolve("datapacks/native-check.txt"), "retained alongside native data packs");
        tools = server.submit(() -> checkOriginalTools(server.getPlayerList().getPlayers().getFirst()));
        next(3);
    }

    private static void checkOriginalTools(ServerPlayer player) {
        require(player.serverLevel().getRecipeManager().byKey(net.minecraft.resources.ResourceLocation.fromNamespaceAndPath("bettertools", "tool_cube")).isPresent(), "The normal product JAR did not load its crafting recipe");
        var cube = new ItemStack(BetterTools.CUBE);
        var pick = named(Items.DIAMOND_PICKAXE, "Original pick");
        pick.setDamageValue(42);
        var sword = named(Items.WOODEN_SWORD, "Primary sword");
        sword.setDamageValue(sword.getMaxDamage() - 1);
        var backup = named(Items.IRON_SWORD, "Reserve sword");
        require(CubeInventory.insert(cube, pick) && CubeInventory.insert(cube, sword) && CubeInventory.insert(cube, backup), "Stored equipment rejected");
        player.getInventory().selected = 0;
        player.getInventory().setItem(0, cube);
        CubeSelection.equip(player, 0);
        require(player.getMainHandItem().is(Items.DIAMOND_PICKAXE) && player.getMainHandItem().getDamageValue() == 42
            && player.getMainHandItem().getHoverName().getString().equals("Original pick"), "Morph lost the real original's components or durability");
        var offhand = new ItemStack(Items.TORCH, 37);
        player.setItemSlot(EquipmentSlot.OFFHAND, offhand);
        CubeSelection.equip(player, 3);
        var target = EntityType.COW.create(player.serverLevel());
        require(target != null, "Native target creation failed");
        target.setPos(player.getX() + 1, player.getY(), player.getZ());
        player.serverLevel().addFreshEntity(target);
        player.setGameMode(net.minecraft.world.level.GameType.SURVIVAL); // Creative mode deliberately skips native durability.
        player.attack(target);
        require(player.getMainHandItem().is(Items.IRON_SWORD), "Broken sword did not recover the original reserve");
        require(ItemStack.isSameItemSameComponents(offhand, player.getOffhandItem()) && player.getOffhandItem().getCount() == 37, "Native attack cleanup erased the independent offhand");
        var state = player.getMainHandItem().get(BetterTools.STATE);
        require(state != null && state.unpack().get(0).getDamageValue() == 42, "Recovery lost the stored pick");
        require(state.unpack().size() == CubeState.SHELL_SLOT + 1, "Ten-slot storage lost its shell");
        target.discard();
    }

    private static ItemStack named(net.minecraft.world.item.Item item, String name) {
        var stack = new ItemStack(item);
        stack.set(DataComponents.CUSTOM_NAME, Component.literal(name));
        return stack;
    }

    private void toolsCompleted(Minecraft client) {
        if (!tools.isDone()) return;
        tools.join();
        var inventory = new net.minecraft.client.gui.screens.inventory.InventoryScreen(client.player);
        client.setScreen(inventory);
        net.fabricmc.fabric.api.client.screen.v1.ScreenEvents.afterRender(inventory).register((screen, graphics, mouseX, mouseY, tick) -> {
            if (!CubeInventory.isCube(client.player.getInventory().getItem(0))) return;
            var access = (InventoryAccess) screen;
            access.check$hover(client.player.inventoryMenu.slots.get(heldSlot(client.player)));
            access.check$tooltip(graphics, screen.width / 3, screen.height / 3);
            tooltipDrawn = true;
        });
        next(7);
    }

    private void inventory(Minecraft client) {
        require(ticks < 300, "The native inventory did not render its cube tooltip");
        if (!tooltipDrawn || ticks < 10) return;
        require(client.screen.mouseScrolled(0, 0, 0, -1), "Native inventory wheel did not select stored equipment");
        var cube = client.player.getInventory().getItem(0);
        require(cube.get(BetterTools.STATE).picked() == 0, "Wheel selection did not choose the first occupied tool");
        require(client.screen.keyPressed(258, 0, 0), "Native Tab did not exchange primary and reserve");
        require(cube.get(BetterTools.STATE).picked() == 5, "Selection did not follow the swapped original");
        next(8);
    }

    private void selectionCompleted(Minecraft client) {
        require(ticks < 300, "Native selection/swap packets were not accepted by the actual server");
        if (selection == null) selection = client.getSingleplayerServer().submit(() -> {
            var player = client.getSingleplayerServer().getPlayerList().getPlayers().getFirst();
            var cube = player.getInventory().getItem(0);
            var state = cube.get(BetterTools.STATE);
            if (state == null || state.picked() != 5) return false;
            // Only after observing the real packet result may a native click remove the selected original.
            int slot = heldSlot(player);
            player.inventoryMenu.clicked(slot, 1, net.minecraft.world.inventory.ClickType.PICKUP, player);
            var carried = player.inventoryMenu.getCarried();
            require(carried.is(Items.DIAMOND_PICKAXE) && carried.getDamageValue() == 42, "Native right-click lost the selected original");
            player.inventoryMenu.clicked(slot, 1, net.minecraft.world.inventory.ClickType.PICKUP, player);
            require(player.inventoryMenu.getCarried().isEmpty(), "Native insertion did not return the original to its cube");
            return true;
        });
        if (!selection.isDone()) return;
        if (!selection.join()) { selection = null; return; }
        client.setScreen(new PauseScreen(true));
        next(4);
    }

    private void ai(Minecraft client) throws Exception {
        if (client.screen instanceof PauseScreen) {
            var button = client.screen.children().stream().filter(Button.class::isInstance).map(Button.class::cast)
                .filter(value -> value.getMessage().getString().equals(Component.translatable("rai.title").getString())).findFirst().orElseThrow();
            button.onPress();
            ticks = 0;
            return;
        }
        require(client.screen != null && client.screen.getClass().getSimpleName().equals("AiChatScreen"), "Pause entry did not open the AI panel");
        if (ticks < 10) return; // A real render must exercise native widget/Mixin targets before input is checked.
        var input = client.screen.children().stream().filter(value -> value.getClass().getSimpleName().equals("NativeComposer")).findFirst().orElseThrow();
        var field = input.getClass().getDeclaredField("editor");
        field.setAccessible(true);
        var editor = (MultiLineEditBox) field.get(input);
        input.setFocused(true);
        input.charTyped('中', 0);
        input.keyPressed(257, 0, 0);
        input.charTyped('x', 0);
        require(editor.getValue().equals("中\nx"), "The native multiline editor lost Unicode or newline input");
        client.screen.onClose();
        require(client.screen instanceof PauseScreen, "AI close lost its actual parent screen");
        var chat = new net.minecraft.client.gui.screens.ChatScreen("/rai_new");
        client.setScreen(chat);
        chat.keyPressed(257, 0, 0);
        require(client.screen == chat, "AI command submission closed the native chat screen");
        chat.keyPressed(256, 0, 0);
        require(client.screen == null, "Native chat Escape did not close after an AI submission");
        if (Boolean.getBoolean("rodel.validation.packOnly")) {
            finish(client, true, "pack_world_skin_window_native_editor_inventory_tools");
            return;
        }
        village = dev.richasy.worldreroll.VillageSpawn.placeAsync(client.getSingleplayerServer(), client.player.getUUID());
        next(9);
    }

    private void villageCompleted(Minecraft client) {
        if (!village.isDone()) return;
        var position = village.join();
        require(client.getSingleplayerServer().overworld().getSharedSpawnPos().equals(position), "Village placement did not update the native world spawn");
        if (client.player.blockPosition().distSqr(position) > 4) return;
        next(5);
    }

    private void reroll(Minecraft client) {
        require(RerollController.available(client), "Isolated singleplayer reroll is unavailable");
        RerollController.start(client);
        next(6);
    }

    private void replacement(Minecraft client) throws Exception {
        var server = client.getSingleplayerServer();
        if (server == null || server == previousServer || client.player == null || !RerollController.available(client)) return;
        var target = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
        require(target.getParent().equals(root.resolve("saves")) && !target.equals(source), "Replacement escaped the isolated saves directory");
        if (Files.exists(source)) return;
        require(server.getWorldData().worldGenOptions().seed() != seed, "Reroll reused the old seed");
        require(server.getWorldData().worldGenOptions().generateStructures(), "Reroll lost the native world options");
        require(server.getGameRules().getBoolean(GameRules.RULE_KEEPINVENTORY), "Reroll lost the saved native game rules");
        require(Files.readString(target.resolve("datapacks/native-check.txt")).equals("retained alongside native data packs"), "Reroll lost additional data-pack files");
        finish(client, true, "native_editor_tools_recovery_village_reroll");
    }

    private void next(int value) {
        stage = value; ticks = 0;
        org.slf4j.LoggerFactory.getLogger("worldreroll-native-check").info("Reached native validation stage {}", value);
    }
    private static void require(boolean value, String message) { if (!value) throw new AssertionError(message); }
    private static int heldSlot(net.minecraft.world.entity.player.Player player) {
        return player.inventoryMenu.slots.stream().filter(slot -> slot.container == player.getInventory() && slot.getContainerSlot() == 0)
            .findFirst().orElseThrow().index;
    }
    private void finish(Minecraft client, boolean passed, String message) {
        stage = 99;
        try {
            var report = new com.google.gson.JsonObject();
            report.addProperty("passed", passed);
            report.addProperty("customSkin", customSkin);
            report.addProperty("evidence", message);
            Files.writeString(root.resolve("native-result.json"), report.toString());
        } catch (Exception error) { error.printStackTrace(); }
        client.stop();
    }
}
