package io.github.richasy.bettertools;

import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.core.BlockPos;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;

/** Review: direct server durability checks miss client mining prediction and post-break hand cleanup.
 * Real attack input must exhaust primary/reserve iron axes while chopping, and picks while mining,
 * in a full cube without losing the eight other originals.
 * The independent component snapshots and client/server inventory results detect loss or stale recovery. */
public final class MiningGameTest implements FabricClientGameTest {
    @Override public void runTest(ClientGameTestContext context) {
        breakPrimaryAndReserve(context, Blocks.OAK_LOG, 1);
        breakPrimaryAndReserve(context, Blocks.STONE, 0);
    }

    private static void breakPrimaryAndReserve(ClientGameTestContext context, Block block, int column) {
        try (var world = context.worldBuilder().create()) {
            var expected = net.minecraft.core.NonNullList.withSize(CubeState.TOOL_SLOTS, ItemStack.EMPTY);
            var target = new BlockPos(0, 101, 3);
            world.getServer().runOnServer(server -> {
                var player = server.getPlayerList().getPlayers().getFirst();
                player.setGameMode(GameType.SURVIVAL);
                player.getInventory().clearContent();
                player.getInventory().setSelectedSlot(0);
                var cube = new ItemStack(BetterTools.CUBE);
                var types = new net.minecraft.world.item.Item[]{Items.IRON_PICKAXE, Items.IRON_AXE, Items.IRON_SHOVEL, Items.IRON_SWORD, Items.IRON_HOE};
                for (int slot = 0; slot < CubeState.TOOL_SLOTS; slot++) {
                    var tool = new ItemStack(types[slot % 5]);
                    tool.setDamageValue(slot % 5 == column ? tool.getMaxDamage() - 1 : 11 + slot);
                    tool.set(DataComponents.CUSTOM_NAME, Component.literal("Mining original " + slot));
                    expected.set(slot, tool.copy());
                    check(CubeInventory.insert(cube, tool), "Full mining fixture rejected tool " + slot);
                }
                player.getInventory().setItem(0, cube);
                for (int x = -2; x <= 2; x++) for (int z = -2; z <= 5; z++) {
                    player.level().setBlockAndUpdate(new BlockPos(x, 99, z), Blocks.STONE.defaultBlockState());
                    for (int y = 100; y <= 103; y++) player.level().setBlockAndUpdate(new BlockPos(x, y, z), Blocks.AIR.defaultBlockState());
                }
                player.level().setBlockAndUpdate(target, block.defaultBlockState());
                player.teleportTo(0.5, 100, 0.5);
                player.setYRot(0);
                player.setXRot(0);
                player.inventoryMenu.broadcastChanges();
            });
            context.runOnClient(m -> {
                m.options.pauseOnLostFocus = false;
                m.player.setYRot(0);
                m.player.setXRot(0);
            });
            context.waitFor(m -> CubeInventory.isCube(m.player.getMainHandItem())
                    && CubeInventory.state(m.player.getMainHandItem()).active() == column
                    && m.hitResult instanceof net.minecraft.world.phys.BlockHitResult hit && hit.getBlockPos().equals(target));
            for (int slot : new int[]{column, column + 5}) {
                context.getInput().holdKey(options -> options.keyAttack);
                try { context.waitFor(m -> m.level.getBlockState(target).isAir()); }
                finally { context.getInput().releaseKey(options -> options.keyAttack); }
                expected.set(slot, ItemStack.EMPTY);
                context.waitTicks(5);
                world.getServer().runOnServer(server -> verifyRemaining(server.getPlayerList().getPlayers().getFirst().getMainHandItem(), expected));
                context.runOnClient(m -> verifyRemaining(m.player.getMainHandItem(), expected));
                if (slot == column) {
                    world.getServer().runOnServer(server -> server.getPlayerList().getPlayers().getFirst().level()
                            .setBlockAndUpdate(target, block.defaultBlockState()));
                    context.waitFor(m -> m.level.getBlockState(target).is(block));
                }
            }
        }
    }

    private static void verifyRemaining(ItemStack held, java.util.List<ItemStack> expected) {
        check(CubeInventory.isCube(held), "Mining input destroyed the full cube");
        var actual = CubeInventory.state(held).equipment(held);
        check(actual.get(CubeState.SHELL_SLOT).is(BetterTools.CUBE), "Mining input lost the shell");
        for (int slot = 0; slot < CubeState.TOOL_SLOTS; slot++)
            check(ItemStack.matches(expected.get(slot), actual.get(slot)), "Mining input changed original " + slot);
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
