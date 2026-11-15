package io.github.richasy.bettertools;

import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.core.BlockPos;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.BlockHitResult;

/** Review: a hoe slot is not usable for farming if mining selection leaves the shovel's path action in control.
 * Real right-click packets must till soil, break only the primary and consume the reserve's own durability.
 * This adds native item-use evidence absent from storage and combat checks, without replacing soil rules. */
public final class FarmingGameTest implements FabricClientGameTest {
    @Override public void runTest(ClientGameTestContext context) {
        try (var world = context.worldBuilder().create()) {
            world.getServer().runOnServer(server -> {
                var player = server.getPlayerList().getPlayers().getFirst();
                player.setGameMode(GameType.SURVIVAL);
                player.getInventory().clearContent();
                player.getInventory().setSelectedSlot(0);
                for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++) {
                    player.level().setBlockAndUpdate(new BlockPos(x, 99, z), Blocks.DIRT.defaultBlockState());
                    for (int y = 100; y <= 103; y++) player.level().setBlockAndUpdate(new BlockPos(x, y, z), Blocks.AIR.defaultBlockState());
                }
                var cube = new ItemStack(BetterTools.CUBE);
                CubeInventory.insert(cube, new ItemStack(Items.IRON_SHOVEL));
                var primary = new ItemStack(Items.IRON_HOE);
                primary.setDamageValue(primary.getMaxDamage() - 1);
                CubeInventory.insert(cube, primary);
                var reserve = new ItemStack(Items.DIAMOND_HOE);
                reserve.setDamageValue(21);
                CubeInventory.insert(cube, reserve);
                player.getInventory().setItem(0, cube);
                player.teleportTo(0.5, 100, 0.5);
                player.setYRot(0);
                player.setXRot(80);
                player.inventoryMenu.broadcastChanges();
            });
            context.runOnClient(m -> {
                m.options.pauseOnLostFocus = false;
                m.player.setYRot(0);
                m.player.setXRot(80);
            });
            context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_SHOVEL)
                    && m.hitResult instanceof BlockHitResult hit && hit.getType() == net.minecraft.world.phys.HitResult.Type.BLOCK
                    && hit.getBlockPos().getY() == 99);
            var soil = context.computeOnClient(m -> ((BlockHitResult)m.hitResult).getBlockPos());
            context.getInput().pressKey(options -> options.keyUse);
            context.waitTicks(5);
            world.getServer().runOnServer(server -> {
                var player = server.getPlayerList().getPlayers().getFirst();
                check(player.level().getBlockState(soil).is(Blocks.FARMLAND), "Native right-click did not till soil: "
                        + soil + " / " + player.level().getBlockState(soil) + " / " + player.getMainHandItem());
                var held = player.getMainHandItem();
                check(CubeInventory.isCube(held), "Post-use result overwrote the recovered cube: " + held);
                var items = CubeInventory.state(held).equipment(held);
                check(items.get(4).isEmpty() && items.get(9).is(Items.DIAMOND_HOE) && items.get(9).getDamageValue() == 21,
                        "Tilling lost the cube or consumed reserve durability before primary broke: " + items);
                player.level().setBlockAndUpdate(soil, Blocks.DIRT.defaultBlockState());
            });
            context.waitFor(m -> m.level.getBlockState(soil).is(Blocks.DIRT));
            context.getInput().pressKey(options -> options.keyUse);
            context.waitFor(m -> m.level.getBlockState(soil).is(Blocks.FARMLAND));
            context.waitTicks(5);
            world.getServer().runOnServer(server -> {
                var held = server.getPlayerList().getPlayers().getFirst().getMainHandItem();
                var reserve = CubeInventory.state(held).equipment(held).get(9);
                check(reserve.is(Items.DIAMOND_HOE) && reserve.getDamageValue() == 22, "Reserve hoe did not receive exactly its own native wear");
            });
        }
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
