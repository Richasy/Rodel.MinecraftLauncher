package io.github.richasy.bettertools;

import com.mojang.blaze3d.platform.InputConstants;
import io.github.richasy.bettertools.checks.CubeStorageChecks;
import io.github.richasy.bettertools.client.CubeClientConfig;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.world.item.enchantment.Enchantments;
import net.minecraft.world.level.block.Blocks;

/** Real integrated-server storage, native durability and synchronized inventory interactions, using only a new isolated world. */
public final class BetterToolsGameTest implements FabricClientGameTest {
    @Override public void runTest(ClientGameTestContext context) {
        var world = context.worldBuilder().create();
        var save = world.getWorldSave();
        final ItemStack[] persisted = {null};
        try {
            context.runOnClient(m -> {
                m.options.pauseOnLostFocus = false;
                m.options.guiScale().set(3);
                m.getWindow().setWindowed(1280, 800);
                m.gui.setScreen(new InventoryScreen(m.player));
            });
            context.waitTicks(5);
            world.getServer().runOnServer(server -> {
                var player = server.getPlayerList().getPlayers().getFirst();
                player.getInventory().clearContent();
                player.getInventory().setSelectedSlot(0);
                CubeStorageChecks.run(player);
                player.getInventory().setItem(0, new ItemStack(BetterTools.CUBE));
                var pick = new ItemStack(Items.IRON_PICKAXE);
                pick.setDamageValue(1);
                player.getInventory().setItem(1, pick);
                var reserve = new ItemStack(Items.DIAMOND_PICKAXE);
                reserve.setDamageValue(17);
                var data = new CompoundTag();
                data.putString("foreign_mod_value", "preserve-original");
                reserve.set(DataComponents.CUSTOM_DATA, CustomData.of(data));
                reserve.enchant(player.registryAccess().lookupOrThrow(Registries.ENCHANTMENT).getOrThrow(Enchantments.FORTUNE), 3);
                player.getInventory().setItem(2, reserve);
                player.getInventory().setItem(3, new ItemStack(Items.STONE_PICKAXE));
                player.inventoryMenu.broadcastChanges();
            });
            context.waitFor(m -> m.player.getInventory().getItem(3).is(Items.STONE_PICKAXE));
            nativeInsertion(context, 37);
            nativeInsertion(context, 38);
            // A third tool must remain on the cursor; vanilla swap would replace or detach the whole cube.
            nativeInsertion(context, 39);
            context.runOnClient(m -> {
                check(m.player.containerMenu.getCarried().is(Items.STONE_PICKAXE), "Full column consumed or swapped third pickaxe");
                m.gameMode.handleContainerInput(0, 39, 0, ContainerInput.PICKUP, m.player);
            });
            context.runOnClient(m -> check(m.gui.screen() instanceof InventoryScreen, "Native inventory was replaced"));
            // The fixture uses native InventoryScreen's default layout; no OS cursor or global screen coordinates.
            final double[] hover = new double[2];
            context.runOnClient(m -> {
                var screen = m.gui.screen();
                double gx = (screen.width - 176) / 2.0 + m.player.containerMenu.getSlot(36).x + 8;
                double gy = (screen.height - 166) / 2.0 + m.player.containerMenu.getSlot(36).y + 8;
                hover[0] = gx * m.getWindow().getGuiScale();
                hover[1] = gy * m.getWindow().getGuiScale();
            });
            context.getInput().setCursorPos(hover[0], hover[1]);
            context.waitTicks(5);
            context.takeScreenshot("bettertools-bundle-preview");
            context.runOnClient(m -> {
                var screen = (AbstractContainerScreen<?>)m.gui.screen();
                for (int i = 0; i < 2; i++) check(screen.mouseScrolled(0, 0, 0, -1), "Cube did not consume native bundle scrolling");
            });
            context.waitFor(m -> CubeInventory.state(m.player.getInventory().getItem(0)).picked() == 5);
            context.waitTicks(4);
            world.getServer().runOnServer(server -> check(CubeInventory.state(server.getPlayerList().getPlayers().getFirst()
                    .getInventory().getItem(0)).picked() == 5, "Wheel selection never reached the actual server"));
            context.takeScreenshot("bettertools-bundle-reserve-selected");
            swapPriority(context, world);
            context.runOnClient(m -> m.gameMode.handleContainerInput(0, 36, 1, ContainerInput.PICKUP, m.player));
            context.waitFor(m -> m.player.containerMenu.getCarried().is(Items.DIAMOND_PICKAXE));
            context.runOnClient(m -> {
                check(m.gui.screen() instanceof InventoryScreen, "Right-click opened a separate screen");
                m.gameMode.handleContainerInput(0, 38, 0, ContainerInput.PICKUP, m.player);
            });
            context.waitFor(m -> m.player.getInventory().getItem(2).is(Items.DIAMOND_PICKAXE));
            context.runOnClient(m -> m.gameMode.handleContainerInput(0, 36, 1, ContainerInput.PICKUP, m.player));
            context.waitFor(m -> m.player.containerMenu.getCarried().is(Items.IRON_PICKAXE));
            context.runOnClient(m -> m.gameMode.handleContainerInput(0, 37, 0, ContainerInput.PICKUP, m.player));
            context.waitFor(m -> m.player.getInventory().getItem(1).is(Items.IRON_PICKAXE));
            context.waitTicks(4);
            context.takeScreenshot("bettertools-bundle-empty");
            context.runOnClient(m -> m.gui.screen().onClose());
            context.waitTicks(5);
            world.getServer().runOnServer(server -> {
                var player = server.getPlayerList().getPlayers().getFirst();
                var cube = player.getInventory().getItem(0);
                check(cube.is(BetterTools.CUBE), "Native inventory clicks lost cube");
                check(CubeInventory.state(cube).unpack().get(5).isEmpty(), "Selected reserve was not extracted");
                int iron = 0, diamond = 0, stone = 0;
                for (var item : player.getInventory().getNonEquipmentItems()) {
                    if (item.is(Items.IRON_PICKAXE)) iron += item.getCount();
                    if (item.is(Items.DIAMOND_PICKAXE)) diamond += item.getCount();
                    if (item.is(Items.STONE_PICKAXE)) stone += item.getCount();
                }
                check(iron == 1 && diamond == 1 && stone == 1, "Native insertion/extraction violated item conservation");
            });
            hoeSelection(context, world);
            targetTransitions(context, world);
            context.runOnClient(m -> m.player.setXRot(-80));
            context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_AXE));
            world.getServer().runOnServer(server -> persisted[0] = CubeInventory.idle(server.getPlayerList().getPlayers().getFirst().getMainHandItem()).copy());
        } finally { world.close(); }
        // Reopen the real saved world/player file; a codec-only round trip cannot establish persistence wiring.
        try (var reopened = save.open()) {
            reopened.getServer().runOnServer(server -> {
                var held = CubeInventory.idle(server.getPlayerList().getPlayers().getFirst().getMainHandItem());
                check(ItemStack.isSameItemSameComponents(persisted[0], held), "Saved player inventory changed cube or contained equipment after rejoin");
            });
        }
    }

    /** Review: extend the actual wheel/extraction journey to detect a missing key hook or server channel.
     * Distinct original snapshots detect copying damage/components, swapping another column or losing selection.
     * The second Tab must restore the complete cube; direct storage calls cannot establish this network behavior. */
    private static void swapPriority(ClientGameTestContext context,
            net.fabricmc.fabric.api.client.gametest.v1.context.TestSingleplayerContext world) {
        final ItemStack[] before = {null};
        context.runOnClient(m -> {
            before[0] = m.player.getInventory().getItem(0).copy();
            check(m.gui.screen().keyPressed(new KeyEvent(InputConstants.KEY_TAB, 0, 0)), "Selected cube did not consume Tab");
        });
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            var cube = player.getInventory().getItem(0);
            var state = CubeInventory.state(cube);
            var unchanged = cube.copy();
            // Requests bound to another menu, cube identity or selection must never reorder these originals.
            new SwapPayload(player.containerMenu.containerId + 1, 36, 5, state.identity()).apply(player);
            new SwapPayload(player.containerMenu.containerId, 36, 5, java.util.UUID.randomUUID()).apply(player);
            new SwapPayload(player.containerMenu.containerId, 36, 9, state.identity()).apply(player);
            check(ItemStack.matches(unchanged, cube), "Stale swap request changed equipment");
        });
        context.waitFor(m -> CubeInventory.state(m.player.getInventory().getItem(0)).picked() == 0);
        context.waitTicks(4);
        world.getServer().runOnServer(server -> {
            var cube = server.getPlayerList().getPlayers().getFirst().getInventory().getItem(0);
            var state = CubeInventory.state(cube);
            var originals = CubeInventory.state(before[0]).unpack();
            var actual = state.unpack();
            check(state.picked() == 0 && ItemStack.matches(originals.get(5), actual.get(0))
                    && ItemStack.matches(originals.get(0), actual.get(5)), "Tab did not exchange the two complete originals on the server");
            check(ItemStack.matches(CubeInventory.displayedTool(before[0]), CubeInventory.displayedTool(cube)), "Tab changed the remembered original or its durability");
        });
        context.takeScreenshot("bettertools-tab-primary-selected");
        context.runOnClient(m -> check(m.gui.screen().keyPressed(new KeyEvent(InputConstants.KEY_TAB, 0, 0)), "Second Tab lost the selected original"));
        context.waitFor(m -> CubeInventory.state(m.player.getInventory().getItem(0)).picked() == 5);
        context.waitTicks(4);
        world.getServer().runOnServer(server -> check(ItemStack.matches(before[0], server.getPlayerList().getPlayers().getFirst()
                .getInventory().getItem(0)), "Two Tab swaps did not restore the exact cube"));
        context.runOnClient(m -> ((AbstractContainerScreen<?>)m.gui.screen()).mouseScrolled(0, 0, 0, -1));
        previewThemes(context);
        context.runOnClient(m -> ((AbstractContainerScreen<?>)m.gui.screen()).mouseScrolled(0, 0, 0, -1));
        context.waitFor(m -> CubeInventory.state(m.player.getInventory().getItem(0)).picked() == 5);
        context.waitTicks(4);
    }

    /** Review: the accepted paired palettes must use this mod's real saved setting and return to the same native inventory.
     * Screenshots inspect the actual card/icons in both themes; recreating settings and reading its file detects an in-memory-only toggle. */
    private static void previewThemes(ClientGameTestContext context) {
        setTheme(context, false);
        context.takeScreenshot("bettertools-approved-light");
        setTheme(context, true);
        context.takeScreenshot("bettertools-approved-dark");
        setTheme(context, false);
    }

    private static void setTheme(ClientGameTestContext context, boolean dark) {
        context.runOnClient(m -> {
            var parent = m.gui.screen();
            var settings = new CubeClientConfig(parent);
            m.gui.setScreen(settings);
            var buttons = settings.children().stream().filter(child -> child instanceof Button).map(child -> (Button)child).toList();
            if (CubeClientConfig.dark() != dark) buttons.getFirst().onPress(new KeyEvent(InputConstants.KEY_RETURN, 0, 0));
            buttons.get(1).onPress(new KeyEvent(InputConstants.KEY_RETURN, 0, 0));
            check(m.gui.screen() == parent, "Theme settings did not return to the original inventory");
            var persisted = new java.util.Properties();
            try (var input = java.nio.file.Files.newInputStream(m.gameDirectory.toPath().resolve("config/bettertools-client.properties"))) { persisted.load(input); }
            catch (java.io.IOException error) { throw new AssertionError("Theme preference was not saved", error); }
            check(Boolean.parseBoolean(persisted.getProperty("darkTheme")) == dark, "Theme toggle was not persisted");
        });
        context.waitTicks(4);
        final double[] hover = new double[2];
        context.runOnClient(m -> {
            var slot = m.player.containerMenu.getSlot(36);
            var screen = (AbstractContainerScreen<?>)m.gui.screen();
            hover[0] = ((screen.width - 176) / 2.0 + slot.x + 8) * m.getWindow().getGuiScale();
            hover[1] = ((screen.height - 166) / 2.0 + slot.y + 8) * m.getWindow().getGuiScale();
        });
        context.getInput().setCursorPos(hover[0], hover[1]);
        context.waitTicks(4);
        context.runOnClient(m -> {
            var screen = (AbstractContainerScreen<?>)m.gui.screen();
            for (int attempt = 0; attempt < 3 && CubeInventory.state(m.player.getInventory().getItem(0)).picked() != 0; attempt++)
                check(screen.mouseScrolled(0, 0, 0, -1), "Theme change lost native cube hover");
            check(CubeInventory.state(m.player.getInventory().getItem(0)).picked() == 0, "Theme change lost the original selection");
        });
        context.waitTicks(4);
    }

    /** Review: the new last cell must cross the actual wheel-selection packet and return the correct original.
     * Local storage checks cannot detect a server payload bound still rejecting slots eight/nine. */
    private static void hoeSelection(ClientGameTestContext context,
            net.fabricmc.fabric.api.client.gametest.v1.context.TestSingleplayerContext world) {
        context.runOnClient(m -> m.gui.setScreen(new InventoryScreen(m.player)));
        context.waitTicks(4);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            player.getInventory().setItem(4, new ItemStack(Items.IRON_HOE));
            var reserve = new ItemStack(Items.DIAMOND_HOE);
            reserve.setDamageValue(29);
            player.getInventory().setItem(5, reserve);
            player.inventoryMenu.broadcastChanges();
        });
        context.waitFor(m -> m.player.getInventory().getItem(5).is(Items.DIAMOND_HOE));
        nativeInsertion(context, 40);
        nativeInsertion(context, 41);
        final double[] hover = new double[2];
        context.runOnClient(m -> {
            var screen = m.gui.screen();
            hover[0] = ((screen.width - 176) / 2.0 + m.player.containerMenu.getSlot(36).x + 8) * m.getWindow().getGuiScale();
            hover[1] = ((screen.height - 166) / 2.0 + m.player.containerMenu.getSlot(36).y + 8) * m.getWindow().getGuiScale();
        });
        context.getInput().setCursorPos(hover[0], hover[1]);
        context.waitTicks(4);
        context.runOnClient(m -> {
            var screen = (AbstractContainerScreen<?>)m.gui.screen();
            for (int i = 0; i < 2; i++) check(screen.mouseScrolled(0, 0, 0, -1), "Hoe wheel selection was not consumed");
        });
        context.waitFor(m -> CubeInventory.state(m.player.getInventory().getItem(0)).picked() == 9);
        context.waitTicks(4);
        world.getServer().runOnServer(server -> check(CubeInventory.state(server.getPlayerList().getPlayers().getFirst()
                .getMainHandItem()).picked() == 9, "Reserve hoe selection never reached server"));
        context.takeScreenshot("bettertools-ten-slots-hoe");
        context.runOnClient(m -> m.gameMode.handleContainerInput(0, 36, 1, ContainerInput.PICKUP, m.player));
        context.waitFor(m -> m.player.containerMenu.getCarried().is(Items.DIAMOND_HOE));
        context.runOnClient(m -> {
            check(m.player.containerMenu.getCarried().getDamageValue() == 29, "Last cell returned the wrong hoe or changed its damage");
            m.gameMode.handleContainerInput(0, 41, 0, ContainerInput.PICKUP, m.player);
            m.gui.screen().onClose();
        });
        context.waitTicks(4);
    }

    /** Real server ticks and ray traces must drive the visible held item; direct selector calls cannot satisfy this case. */
    private static void targetTransitions(ClientGameTestContext context,
            net.fabricmc.fabric.api.client.gametest.v1.context.TestSingleplayerContext world) {
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            player.getInventory().clearContent();
            var cube = new ItemStack(BetterTools.CUBE);
            var pick = new ItemStack(Items.IRON_PICKAXE);
            pick.setDamageValue(100);
            var axe = new ItemStack(Items.IRON_AXE);
            axe.setDamageValue(180);
            CubeInventory.insert(cube, pick);
            CubeInventory.insert(cube, axe);
            player.getInventory().setItem(0, cube);
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 5; z++)
                player.level().setBlockAndUpdate(new net.minecraft.core.BlockPos(x, 99, z), Blocks.STONE.defaultBlockState());
            player.level().setBlockAndUpdate(new net.minecraft.core.BlockPos(0, 101, 3), Blocks.STONE.defaultBlockState());
            player.teleportTo(0.5, 100, 0.5);
            player.setYRot(0);
            player.setXRot(0);
            player.inventoryMenu.broadcastChanges();
        });
        context.runOnClient(m -> { m.player.setYRot(0); m.player.setXRot(0); });
        context.waitTicks(5);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            check(player.containerMenu == player.inventoryMenu, "Target test still has an open server container");
            var cube = player.getMainHandItem();
            check(CubeInventory.isCube(cube), "Target test no longer holds cube: " + cube);
            check(CubeSelection.target(player, CubeInventory.state(cube).equipment(cube)) == 0,
                    "Target fixture ray misses stone: " + player.getEyePosition() + " / " + player.getViewVector(1) + " / " + player.pick(5, 1, false));
        });
        context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_PICKAXE));
        context.waitTicks(12);
        context.takeScreenshot("bettertools-auto-pickaxe");
        context.runOnClient(m -> m.player.setXRot(-80));
        context.waitTicks(12);
        context.runOnClient(m -> check(m.player.getMainHandItem().is(Items.IRON_PICKAXE), "Looking into air stowed the last tool"));
        context.takeScreenshot("bettertools-retained-pickaxe");
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            player.level().setBlockAndUpdate(new net.minecraft.core.BlockPos(0, 101, 3), Blocks.AIR.defaultBlockState());
            var target = (net.minecraft.world.entity.Mob) net.minecraft.core.registries.BuiltInRegistries.ENTITY_TYPE
                    .getValue(net.minecraft.resources.Identifier.fromNamespaceAndPath("minecraft", "villager"))
                    .create(player.level(), net.minecraft.world.entity.EntitySpawnReason.TRIGGERED);
            target.setNoAi(true);
            target.setPos(0.5, 100, 2.5);
            player.level().addFreshEntity(target);
        });
        context.runOnClient(m -> m.player.setXRot(0));
        context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_AXE));
        context.waitTicks(12);
        context.takeScreenshot("bettertools-combat-fallback-axe");
        context.runOnClient(m -> m.gui.setScreen(new InventoryScreen(m.player)));
        context.waitFor(m -> m.player.getMainHandItem().is(BetterTools.CUBE));
        context.runOnClient(m -> check(m.gui.screen() instanceof InventoryScreen, "Inventory transition replaced native screen"));
        context.runOnClient(m -> m.gui.screen().onClose());
    }

    private static void nativeInsertion(ClientGameTestContext context, int sourceSlot) {
        context.runOnClient(m -> {
            m.gameMode.handleContainerInput(0, sourceSlot, 0, ContainerInput.PICKUP, m.player);
            m.gameMode.handleContainerInput(0, 36, 0, ContainerInput.PICKUP, m.player);
        });
        context.waitTicks(3);
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
