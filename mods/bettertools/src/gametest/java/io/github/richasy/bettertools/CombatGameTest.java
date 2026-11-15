package io.github.richasy.bettertools;

import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.fabricmc.fabric.api.client.gametest.v1.context.TestSingleplayerContext;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Blocks;

/** Real client input, server ray traces and native combat in a new disposable world; no target or damage mocks. */
public final class CombatGameTest implements FabricClientGameTest {
    @Override public void runTest(ClientGameTestContext context) {
        try (var world = context.worldBuilder().create()) {
            context.runOnClient(m -> m.options.pauseOnLostFocus = false);
            rangedAimAndFallback(context, world);
            swordMissTimeout(context, world);
            incomingHitAndNativeDamage(context, world);
            swordBreakFallback(context, world);
        }
    }

    /** Review: the old reach-limited trace cannot equip a sword at twelve blocks, and a wall must still occlude it.
     * Extend observable held-item checks with missing-category/tier recovery; do not duplicate the selector as oracle. */
    private static void rangedAimAndFallback(ClientGameTestContext context, TestSingleplayerContext world) {
        world.getServer().runOnServer(server -> prepare(server.getPlayerList().getPlayers().getFirst()));
        look(context, 0, 0);
        context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_PICKAXE));
        context.waitTicks(30);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.AIR.defaultBlockState());
            spawn(player, 0.5, 12.5);
            check(player.getAttackStrengthScale(0) == 1, "Fixture never reached a charged attack");
        });
        context.waitFor(m -> m.player.getMainHandItem().is(Items.DIAMOND_SWORD));
        context.waitTick();
        // Native visible-item cache must change before the old three-tick lower/raise sequence could finish.
        context.runOnClient(m -> {
            var hands = m.player.firstPersonHandsAndItems();
            try {
                var visible = hands.getClass().getDeclaredField("mainHandItem");
                visible.setAccessible(true);
                check(((ItemStack)visible.get(hands)).is(Items.DIAMOND_SWORD), "Visible sword still waited for equip animation");
            } catch (ReflectiveOperationException error) { throw new AssertionError("Cannot inspect native visible item", error); }
        });
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            check(player.getMainHandItem().is(Items.DIAMOND_SWORD), "Distant aim never selected server sword");
            check(player.getAttackStrengthScale(0) == 1, "Transform reset native attack charge");
            player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.STONE.defaultBlockState());
        });
        context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_PICKAXE));
        look(context, 0, -80);
        context.waitTicks(8);
        context.runOnClient(m -> check(m.player.getMainHandItem().is(Items.IRON_PICKAXE), "Air target stowed tool"));
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.OAK_LOG.defaultBlockState());
        });
        look(context, 0, 0);
        context.waitTicks(8);
        context.runOnClient(m -> check(m.player.getMainHandItem().is(Items.IRON_PICKAXE), "Missing axe left a stocked cube empty"));
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            var cube = new ItemStack(BetterTools.CUBE);
            CubeInventory.insert(cube, new ItemStack(Items.WOODEN_PICKAXE));
            CubeInventory.insert(cube, new ItemStack(Items.DIAMOND_PICKAXE));
            player.getInventory().setItem(0, cube);
            player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.DIAMOND_ORE.defaultBlockState());
        });
        context.waitFor(m -> m.player.getMainHandItem().is(Items.DIAMOND_PICKAXE));
    }

    /** Review: a real empty-air attack packet must latch combat, refresh on another miss, then release after inactivity.
     * Selector-only checks cannot detect a missing swing hook or a refresh that never reaches the server. */
    private static void swordMissTimeout(ClientGameTestContext context, TestSingleplayerContext world) {
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            prepare(player);
            player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.AIR.defaultBlockState());
            spawn(player, 0.5, 12.5);
        });
        look(context, 0, 0);
        context.waitFor(m -> m.player.getMainHandItem().is(Items.DIAMOND_SWORD));
        look(context, 0, -80);
        context.waitTicks(4);
        context.getInput().pressKey(options -> options.keyAttack);
        context.waitTicks(4);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            check(CubeCombat.active(player), "Empty-air slash did not reach server combat intent");
            player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.STONE.defaultBlockState());
        });
        look(context, 0, 0);
        context.waitTicks(40);
        context.runOnClient(m -> check(m.player.getMainHandItem().is(Items.DIAMOND_SWORD), "Ground target interrupted combat"));
        look(context, 0, -80);
        context.getInput().pressKey(options -> options.keyAttack);
        context.waitTicks(4);
        final long[] refreshed = {0};
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            check(CubeCombat.active(player), "Second slash did not refresh combat");
            refreshed[0] = player.level().getGameTime();
        });
        look(context, 0, 0);
        context.waitTicks(75);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            check(CubeCombat.active(player) && player.getMainHandItem().is(Items.DIAMOND_SWORD), "Combat expired before refreshed deadline");
        });
        context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_PICKAXE));
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            check(!CubeCombat.active(player) && player.level().getGameTime() - refreshed[0] >= 95,
                    "Combat released without reaching five seconds of inactivity");
        });
    }

    /** Review: real hurt/attack hooks must equip before damage, preserve ordinary attack cooldown and release on death.
     * Checking the displayed item alone would miss an attack still using the pickaxe's old native attributes. */
    private static void incomingHitAndNativeDamage(ClientGameTestContext context, TestSingleplayerContext world) {
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            prepare(player);
            server.setDifficulty(net.minecraft.world.Difficulty.NORMAL, true);
        });
        look(context, 0, 0);
        context.waitFor(m -> m.player.getMainHandItem().is(Items.IRON_PICKAXE));
        context.waitTicks(30);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            var attacker = spawn(player, 8.5, 0.5);
            check(player.hurtServer(player.level(), player.damageSources().mobAttack(attacker), 1), "Fixture hit was rejected");
            check(player.getMainHandItem().is(Items.DIAMOND_SWORD) && CubeCombat.active(player), "Incoming attack did not equip sword immediately");
            attacker.hurtServer(player.level(), attacker.damageSources().genericKill(), Float.MAX_VALUE);
            CubeSelection.update(player, false);
            check(player.getMainHandItem().is(Items.IRON_PICKAXE) && !CubeCombat.active(player), "Dead target kept combat locked");
            // The real incoming hit applies knockback; reset its motion/position before the independent attack check.
            prepare(player);
        });
        context.waitTicks(30);
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            var victim = spawn(player, 2.5, 0.5);
            float health = victim.getHealth();
            player.attack(victim);
            check(player.getMainHandItem().is(Items.DIAMOND_SWORD), "Actual attack used a mining tool");
            check(health - victim.getHealth() >= 6.9F, "Sword was displayed but native damage still used pickaxe attributes: " + (health - victim.getHealth()));
            check(player.getAttackStrengthScale(0) < 0.2F, "Actual attack bypassed native cooldown");
            victim.hurtServer(player.level(), victim.damageSources().genericKill(), Float.MAX_VALUE);
            // Native attack and swing are separate packets; the finishing swing must not reopen combat after death.
            player.swing(net.minecraft.world.InteractionHand.MAIN_HAND, player.getMainHandItem().getAttackAnimation(), false);
            CubeSelection.update(player, false);
            check(player.getMainHandItem().is(Items.IRON_PICKAXE), "Kill did not release combat to current block tool");
            CubeSelection.equip(player, 3);
            player.swing(net.minecraft.world.InteractionHand.MAIN_HAND, player.getMainHandItem().getAttackAnimation(), false);
            check(CubeCombat.active(player), "Trailing kill swing suppression swallowed a later independent slash");
        });
    }

    /** Review: native attack cleanup runs after durability recovery, unlike direct hurtAndBreak checks.
     * Two broken swords must leave the original axe equipped and the independent offhand item untouched. */
    private static void swordBreakFallback(ClientGameTestContext context, TestSingleplayerContext world) {
        world.getServer().runOnServer(server -> {
            var player = server.getPlayerList().getPlayers().getFirst();
            prepare(player);
            var cube = new ItemStack(BetterTools.CUBE);
            for (int row = 0; row < 2; row++) {
                var sword = new ItemStack(Items.IRON_SWORD);
                sword.setDamageValue(sword.getMaxDamage() - 1);
                CubeInventory.insert(cube, sword);
            }
            var axe = new ItemStack(Items.IRON_AXE);
            axe.setDamageValue(37);
            var expectedAxe = axe.copy();
            CubeInventory.insert(cube, axe);
            var offhand = new ItemStack(Items.TORCH, 23);
            player.setItemInHand(net.minecraft.world.InteractionHand.OFF_HAND, offhand.copy());
            player.getInventory().setItem(0, CubeInventory.select(cube, 3));
            for (int broken : new int[]{3, 8}) {
                var victim = spawn(player, 2.5, 0.5);
                player.attack(victim);
                victim.discard();
                var held = player.getMainHandItem();
                check(CubeInventory.isCube(held), "Native attack cleanup destroyed the cube after sword " + broken);
                var items = CubeInventory.state(held).equipment(held);
                check(items.get(broken).isEmpty(), "Broken sword reappeared after native attack cleanup");
                check(ItemStack.matches(expectedAxe, items.get(1)), "Sword break lost or damaged the original fallback axe");
                check(ItemStack.matches(offhand, player.getOffhandItem()), "Sword break deleted the unrelated offhand item");
            }
            check(player.getMainHandItem().is(Items.IRON_AXE), "Exhausted swords did not equip the original fallback axe");
        });
    }

    private static void prepare(ServerPlayer player) {
        CubeCombat.clear(player);
        player.setGameMode(GameType.SURVIVAL);
        player.setHealth(20);
        player.getFoodData().setFoodLevel(20);
        player.getInventory().clearContent();
        player.getInventory().setSelectedSlot(0);
        for (var entity : player.level().getEntitiesOfClass(Mob.class, player.getBoundingBox().inflate(40))) entity.discard();
        for (int z = -2; z <= 34; z++) for (int x = -2; x <= 10; x++) {
            player.level().setBlockAndUpdate(new BlockPos(x, 99, z), Blocks.STONE.defaultBlockState());
            for (int y = 100; y <= 103; y++) player.level().setBlockAndUpdate(new BlockPos(x, y, z), Blocks.AIR.defaultBlockState());
        }
        player.teleportTo(0.5, 100, 0.5);
        player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
        player.connection.send(new net.minecraft.network.protocol.game.ClientboundSetEntityMotionPacket(player));
        player.setYRot(0);
        player.setXRot(0);
        var cube = new ItemStack(BetterTools.CUBE);
        CubeInventory.insert(cube, new ItemStack(Items.IRON_PICKAXE));
        CubeInventory.insert(cube, new ItemStack(Items.DIAMOND_SWORD));
        player.getInventory().setItem(0, cube);
        player.level().setBlockAndUpdate(new BlockPos(0, 101, 3), Blocks.STONE.defaultBlockState());
        player.inventoryMenu.broadcastChanges();
    }

    private static Mob spawn(ServerPlayer player, double x, double z) {
        var mob = (Mob)net.minecraft.core.registries.BuiltInRegistries.ENTITY_TYPE
                .getValue(net.minecraft.resources.Identifier.fromNamespaceAndPath("minecraft", "villager"))
                .create(player.level(), EntitySpawnReason.TRIGGERED);
        mob.setNoAi(true);
        mob.setPos(x, 100, z);
        player.level().addFreshEntity(mob);
        return mob;
    }

    private static void look(ClientGameTestContext context, float yaw, float pitch) {
        context.runOnClient(m -> { m.player.setYRot(yaw); m.player.setXRot(pitch); });
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
