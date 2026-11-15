package io.github.richasy.bettertools.checks;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;
import io.github.richasy.bettertools.CubeState;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Screenshot;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.block.Blocks;

/** Packaged-JAR acceptance in a disposable native-loader instance, independent of Fabric's test runtime. */
public final class NativeClientCheck {
    private static int stage;
    private static int stageTicks;
    private static int previewTicks;
    private static final long START = System.nanoTime();
    private static CompletableFuture<Void> pending;
    private static boolean finished;
    private static String lastScreen;
    private static volatile boolean extractionCommitted;
    private static volatile boolean selectionCommitted;
    private static volatile boolean airCommitted;
    private static ItemStack beforePriority;
    private NativeClientCheck() { }

    /** Advances only after the actual native server, inventory packets and rendered screen reach each checkpoint. */
    public static void tick() {
        if (finished) return;
        var client = Minecraft.getInstance();
        try {
            var screen = client.gui.screen();
            String screenName = screen == null ? "none" : screen.getClass().getName();
            if (!screenName.equals(lastScreen)) {
                lastScreen = screenName;
                System.out.println("BetterTools native check stage=" + stage + " screen=" + screenName);
            }
            if (System.nanoTime() - START > 180_000_000_000L) throw new AssertionError("Native check timed out at stage " + stage);
            if (pending != null) {
                if (!pending.isDone()) return;
                pending.join();
                pending = null;
            }
            if (client.player == null || client.getSingleplayerServer() == null) return;
            stageTicks++;
            switch (stage) {
                case 0 -> {
                    if (client.gui.screen() != null) return;
                    client.options.pauseOnLostFocus = false;
                    client.options.guiScale().set(3);
                    client.gui.setScreen(new InventoryScreen(client.player));
                    onServer(client, player -> {
                        player.getInventory().clearContent();
                        player.getInventory().setSelectedSlot(0);
                        CubeStorageChecks.run(player);
                        player.getInventory().clearContent();
                        player.getInventory().setItem(0, new ItemStack(BetterTools.CUBE));
                        var primary = new ItemStack(Items.IRON_PICKAXE);
                        primary.setDamageValue(100);
                        player.getInventory().setItem(1, primary);
                        var reserve = new ItemStack(Items.DIAMOND_PICKAXE);
                        reserve.setDamageValue(17);
                        player.getInventory().setItem(2, reserve);
                        player.inventoryMenu.broadcastChanges();
                    });
                    advance();
                }
                case 1 -> {
                    if (!client.player.getInventory().getItem(2).is(Items.DIAMOND_PICKAXE)) return;
                    hoverCube(client);
                    if (++previewTicks < 8) return;
                    Screenshot.grab(client, false);
                    click(client, 37, 0);
                    click(client, 36, 0);
                    advance();
                }
                case 2 -> {
                    if (!stored(client, 0, Items.IRON_PICKAXE)) return;
                    click(client, 38, 0);
                    click(client, 36, 0);
                    advance();
                }
                case 3 -> {
                    if (!stored(client, 5, Items.DIAMOND_PICKAXE)) return;
                    check(client.gui.screen() instanceof InventoryScreen, "Inserting tools replaced native inventory");
                    hoverCube(client);
                    advance();
                }
                case 4 -> {
                    // Keep native hover stable across window focus/resize events while waiting for a rendered frame.
                    hoverCube(client);
                    if (stageTicks < 8) return;
                    Screenshot.grab(client, false);
                    var inventoryScreen = (AbstractContainerScreen<?>)client.gui.screen();
                    for (int i = 0; i < 2; i++) check(inventoryScreen.mouseScrolled(0, 0, 0, -1), "Cube wheel handler missing from original inventory");
                    advance();
                }
                case 5 -> {
                    hoverCube(client);
                    if (io.github.richasy.bettertools.CubeInventory.state(client.player.getInventory().getItem(0)).picked() != 5) return;
                    if (!selectionCommitted) {
                        onServer(client, player -> selectionCommitted = CubeInventory.state(player.getInventory().getItem(0)).picked() == 5);
                        return;
                    }
                    Screenshot.grab(client, false);
                    beforePriority = client.player.getInventory().getItem(0).copy();
                    check(client.gui.screen().keyPressed(new KeyEvent(InputConstants.KEY_TAB, 0, 0)), "Native inventory did not consume selected-tool Tab");
                    selectionCommitted = false;
                    stage = 50; stageTicks = 0;
                }
                // Review: each native loader owns its payload registration. Fabric's successful round trip
                // cannot detect a missing Forge/NeoForge channel; compare actual server originals before extraction.
                case 50 -> {
                    hoverCube(client);
                    if (CubeInventory.state(client.player.getInventory().getItem(0)).picked() != 0) return;
                    if (!selectionCommitted) {
                        onServer(client, player -> {
                            var state = CubeInventory.state(player.getInventory().getItem(0));
                            if (state.picked() != 0) return;
                            var expected = CubeInventory.state(beforePriority).unpack();
                            var actual = state.unpack();
                            check(state.display() == 5 && state.identity().equals(CubeInventory.state(beforePriority).identity()), "Native priority swap lost displayed original or identity");
                            for (int slot = 0; slot <= CubeState.SHELL_SLOT; slot++) {
                                int source = slot == 0 ? 5 : slot == 5 ? 0 : slot;
                                check(ItemStack.matches(expected.get(source), actual.get(slot)), "Native swap changed original " + slot);
                            }
                            selectionCommitted = true;
                        });
                        return;
                    }
                    Screenshot.grab(client, false);
                    check(client.gui.screen().keyPressed(new KeyEvent(InputConstants.KEY_TAB, 0, 0)), "Native second Tab lost selection");
                    selectionCommitted = false;
                    stage = 51; stageTicks = 0;
                }
                case 51 -> {
                    hoverCube(client);
                    if (CubeInventory.state(client.player.getInventory().getItem(0)).picked() != 5) return;
                    if (!selectionCommitted) {
                        onServer(client, player -> {
                            var cube = player.getInventory().getItem(0);
                            if (CubeInventory.state(cube).picked() != 5) return;
                            check(ItemStack.matches(beforePriority, cube), "Native repeated Tab changed complete cube state");
                            selectionCommitted = true;
                        });
                        return;
                    }
                    click(client, 36, 1);
                    stage = 6; stageTicks = 0;
                }
                case 6 -> {
                    var carried = client.player.containerMenu.getCarried();
                    if (!carried.is(Items.DIAMOND_PICKAXE)) return;
                    check(carried.getDamageValue() == 17, "Selected reserve inherited primary damage");
                    check(client.gui.screen() instanceof InventoryScreen, "Right-click replaced native inventory");
                    click(client, 38, 0);
                    advance();
                }
                case 7 -> {
                    if (!client.player.getInventory().getItem(2).is(Items.DIAMOND_PICKAXE)) return;
                    if (!extractionCommitted) {
                        onServer(client, player -> extractionCommitted = CubeInventory.state(player.getInventory().getItem(0)).unpack().get(5).isEmpty()
                                && player.getInventory().getItem(2).is(Items.DIAMOND_PICKAXE)
                                && player.containerMenu == player.inventoryMenu);
                        return;
                    }
                    client.gui.screen().onClose();
                    onServer(client, NativeClientCheck::prepareTarget);
                    advance();
                }
                case 8 -> {
                    client.player.setYRot(0);
                    client.player.setXRot(0);
                    if (!client.player.getMainHandItem().is(Items.IRON_PICKAXE)) return;
                    client.player.setXRot(-80);
                    advance();
                }
                case 9 -> {
                    // Wait for the actual server rotation/target boundary, rather than assuming five client ticks
                    // have delivered its movement and inventory packets on every native loader.
                    if (!airCommitted) {
                        onServer(client, player -> {
                            if (player.getXRot() > -70 || CubeSelection.target(player, CubeInventory.state(player.getMainHandItem()).equipment(player.getMainHandItem())) >= 0) return;
                            if (!player.getMainHandItem().is(Items.IRON_PICKAXE)) return;
                            airCommitted = true;
                        });
                        return;
                    }
                    if (!client.player.getMainHandItem().is(Items.IRON_PICKAXE)) return;
                    Screenshot.grab(client, false);
                    onServer(client, player -> {
                        player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.AIR.defaultBlockState());
                        var mob = (Mob)BuiltInRegistries.ENTITY_TYPE.getValue(net.minecraft.resources.Identifier
                                .fromNamespaceAndPath("minecraft", "villager")).create(player.level(), EntitySpawnReason.TRIGGERED);
                        mob.setNoAi(true);
                        mob.setPos(0.5, 100, 2.5);
                        player.level().addFreshEntity(mob);
                    });
                    client.player.setXRot(0);
                    advance();
                }
                case 10 -> {
                    if (!client.player.getMainHandItem().is(Items.IRON_AXE)) return;
                    onServer(client, player -> {
                        var dropped = new net.minecraft.world.entity.item.ItemEntity(player.level(), 0, 100, 0, player.getMainHandItem().copy());
                        check(dropped.getItem().is(BetterTools.CUBE), "Dropped transformed tool did not restore shell");
                        check(CubeInventory.state(dropped.getItem()).unpack().get(1).is(Items.IRON_AXE), "Dropping detached actual active axe");
                        // Actual death must not let an active tool's curse consume the whole storage item.
                        player.getMainHandItem().enchant(player.registryAccess().lookupOrThrow(net.minecraft.core.registries.Registries.ENCHANTMENT)
                                .getOrThrow(net.minecraft.world.item.enchantment.Enchantments.VANISHING_CURSE), 1);
                        player.die(player.damageSources().genericKill());
                        var cubes = player.level().getEntitiesOfClass(net.minecraft.world.entity.item.ItemEntity.class,
                                player.getBoundingBox().inflate(8), item -> item.getItem().is(BetterTools.CUBE));
                        check(cubes.size() == 1, "Actual player death lost or duplicated the cube");
                        var contents = CubeInventory.state(cubes.getFirst().getItem()).unpack();
                        check(contents.get(0).is(Items.IRON_PICKAXE) && contents.get(1).is(Items.IRON_AXE)
                                && contents.get(5).is(Items.DIAMOND_PICKAXE), "Actual death lost contained tools");
                    });
                    advance();
                    // Forge does not keep issuing gameplay tick events on the paused death screen.
                    // Complete from the real server assertion's future, through the render-thread task queue.
                    pending.whenComplete((unused, error) -> client.execute(() -> finish(client, error)));
                }
                case 11 -> {
                    finish(client, null);
                }
                default -> throw new AssertionError("Unexpected native test stage");
            }
        } catch (Throwable error) { finish(client, error); }
    }

    private static void prepareTarget(ServerPlayer player) {
        var bag = player.getInventory();
        check(CubeInventory.state(bag.getItem(0)).unpack().get(5).isEmpty(), "Selected reserve extraction failed on native server");
        check(bag.getItem(2).is(Items.DIAMOND_PICKAXE), "Extracted reserve missing from native bag");
        var cube = bag.getItem(0);
        check(CubeInventory.insert(cube, bag.getItem(2)), "Cannot reinsert original reserve");
        CubeInventory.insert(cube, new ItemStack(Items.IRON_AXE));
        for (int x = -2; x <= 2; x++) for (int z = -2; z <= 5; z++)
            player.level().setBlockAndUpdate(new BlockPos(x, 99, z), Blocks.STONE.defaultBlockState());
        player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.STONE.defaultBlockState());
        player.teleportTo(0.5, 100, 0.5);
        // The source is an isolated saved test world; teleporting does not clear previously accumulated fall damage.
        player.resetFallDistance();
        player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
        player.setHealth(player.getMaxHealth());
        player.setYRot(0);
        player.setXRot(0);
        player.inventoryMenu.broadcastChanges();
    }

    private static void hoverCube(Minecraft client) {
        var screen = client.gui.screen();
        var slot = client.player.containerMenu.getSlot(36);
        double x = ((screen.width - 176) / 2.0 + slot.x + 8) * client.getWindow().getGuiScale();
        double y = ((screen.height - 166) / 2.0 + slot.y + 8) * client.getWindow().getGuiScale();
        client.mouseHandler.onMove(client.getWindow().handle(), x, y, 0, 0);
    }

    private static void onServer(Minecraft client, Consumer<ServerPlayer> action) {
        var server = client.getSingleplayerServer();
        pending = new CompletableFuture<>();
        var completion = pending;
        server.execute(() -> {
            try { action.accept(server.getPlayerList().getPlayers().getFirst()); completion.complete(null); }
            catch (Throwable error) { completion.completeExceptionally(error); }
        });
    }

    private static boolean stored(Minecraft client, int index, net.minecraft.world.item.Item item) {
        var cube = client.player.getInventory().getItem(0);
        var state = cube.get(BetterTools.STATE);
        return state != null && state.unpack().get(index).is(item);
    }

    private static void click(Minecraft client, int slot, int button) {
        client.gameMode.handleContainerInput(client.player.containerMenu.containerId, slot, button, ContainerInput.PICKUP, client.player);
    }

    private static void advance() { stage++; stageTicks = 0; }

    private static void finish(Minecraft client, Throwable error) {
        finished = true;
        try {
            var result = new com.google.gson.JsonObject();
            result.addProperty("succeeded", error == null);
            result.addProperty("stage", stage);
            if (error != null) { error.printStackTrace(); result.addProperty("error", error.toString()); }
            var path = Path.of(System.getProperty("bettertools.test.result"));
            Files.createDirectories(path.toAbsolutePath().getParent());
            Files.writeString(path, result.toString());
        } catch (Exception writeError) { writeError.printStackTrace(); }
        client.stop();
    }

    private static void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
}
