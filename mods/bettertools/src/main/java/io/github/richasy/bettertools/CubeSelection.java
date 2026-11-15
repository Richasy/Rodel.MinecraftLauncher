package io.github.richasy.bettertools;

import io.github.richasy.bettertools.mixin.EquipmentUpdateAccess;
import java.util.List;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.tags.BlockTags;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.ProjectileUtil;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;

/** Shared prediction; the server independently traces targets and commits real inventory changes. */
public final class CubeSelection {
    private CubeSelection() { }

    /** Returns the first surviving item in a column, never preferring an unused reserve over a usable primary. */
    public static int first(List<ItemStack> items, int column) {
        for (int row = 0; row < 2; row++) {
            int slot = row * CubeState.COLUMNS + column;
            if (usable(items, slot)) return slot;
        }
        return -1;
    }

    private static boolean usable(List<ItemStack> items, int slot) {
        return slot >= 0 && slot < CubeState.TOOL_SLOTS && !items.get(slot).isEmpty() && !items.get(slot).isBroken();
    }

    /** Keeps a usable current/remembered original, otherwise takes the first surviving tool. */
    public static int fallback(List<ItemStack> items, int active, int display) {
        if (usable(items, active)) return active;
        if (usable(items, display)) return display;
        for (int column = 0; column < CubeState.COLUMNS; column++) {
            int slot = first(items, column);
            if (slot >= 0) return slot;
        }
        return -1;
    }

    /** Sword, axe, pickaxe, shovel, then hoe; each category consumes its primary before its reserve. */
    public static int weapon(List<ItemStack> items) {
        for (int column : new int[]{3, 1, 0, 2, 4}) {
            int slot = first(items, column);
            if (slot >= 0) return slot;
        }
        return -1;
    }

    /** Selects the native block's tool category, then another genuinely effective available tool. */
    public static int block(List<ItemStack> items, BlockState block) {
        int column = block.is(BlockTags.MINEABLE_WITH_PICKAXE) ? 0
                : block.is(BlockTags.MINEABLE_WITH_AXE) ? 1
                : block.is(BlockTags.MINEABLE_WITH_SHOVEL) ? 2
                : block.is(BlockTags.MINEABLE_WITH_HOE) ? 4
                : block.is(Blocks.COBWEB) || block.is(Blocks.BAMBOO) ? 3 : -1;
        if (column >= 0) {
            for (int row = 0; row < 2; row++) {
                int slot = row * CubeState.COLUMNS + column;
                if (usable(items, slot) && (!block.requiresCorrectToolForDrops() || items.get(slot).isCorrectToolForDrops(block))) return slot;
            }
        }
        int best = -1;
        float speed = 1;
        for (int slot = 0; slot < CubeState.TOOL_SLOTS; slot++) {
            if (!usable(items, slot)) continue;
            int primary = first(items, slot % CubeState.COLUMNS);
            if (slot != primary && (!block.requiresCorrectToolForDrops() || items.get(primary).isCorrectToolForDrops(block))) continue;
            var item = items.get(slot);
            if (block.requiresCorrectToolForDrops() && !item.isCorrectToolForDrops(block)) continue;
            if (item.getDestroySpeed(block) > speed) {
                best = slot;
                speed = item.getDestroySpeed(block);
            }
        }
        return best;
    }

    /** Recognizes a visible living target up to 32 blocks away without extending attack reach or seeing through walls. */
    public static LivingEntity aimedEntity(Player player) {
        double range = Math.max(32, player.entityInteractionRange());
        var eye = player.getEyePosition();
        var end = eye.add(player.getViewVector(1).scale(range));
        var block = player.pick(range, 1, false);
        double entityDistance = range * range;
        if (block.getType() != HitResult.Type.MISS) entityDistance = Math.min(entityDistance, eye.distanceToSqr(block.getLocation()));
        var entity = ProjectileUtil.getEntityHitResult(player, eye, end,
                player.getBoundingBox().expandTowards(end.subtract(eye)).inflate(1),
                candidate -> candidate instanceof LivingEntity living && living.isAlive() && candidate.isPickable() && !candidate.isSpectator(), entityDistance);
        return entity == null ? null : (LivingEntity)entity.getEntity();
    }

    /** Chooses combat before block work, falling back to the last usable original on a miss or unsuitable block. */
    public static int target(Player player, List<ItemStack> items) {
        if (CubeCombat.active(player) || aimedEntity(player) != null) return weapon(items);
        var block = player.pick(player.blockInteractionRange(), 1, false);
        return block instanceof BlockHitResult hit && block.getType() == HitResult.Type.BLOCK
                ? block(items, player.level().getBlockState(hit.getBlockPos())) : -1;
    }

    /** Commits an original and asks vanilla to update its actual attributes before an attack uses them. */
    public static void equip(Player player, int selected) {
        var held = player.getMainHandItem();
        var next = CubeInventory.select(held, selected);
        if (next == held) return;
        player.setItemInHand(InteractionHand.MAIN_HAND, next);
        if (player instanceof ServerPlayer serverPlayer) {
            ((EquipmentUpdateAccess)player).bettertools$updateEquipment();
            var inventory = player.getInventory();
            serverPlayer.connection.send(inventory.createInventoryUpdatePacket(inventory.getSelectedSlot()));
            player.inventoryMenu.broadcastChanges();
        }
    }

    /** Predicts the held original on clients and independently chooses the same target on servers. */
    public static void update(Player player, boolean paused) {
        var held = player.getMainHandItem();
        if (!CubeInventory.isCube(held) || !player.isAlive() || player.isSpectator()) { CubeCombat.clear(player); return; }
        if (player.isUsingItem() && player.getUsedItemHand() == InteractionHand.MAIN_HAND) return;
        var state = CubeInventory.state(held);
        var items = state.equipment(held);
        int selected = paused ? -1 : target(player, items);
        if (!paused && selected < 0) selected = fallback(items, state.active(), state.display());
        equip(player, selected);
    }

    /** Stows off-slot cubes and suspends automatic transforms during native inventory editing. */
    public static void tick(ServerPlayer player, boolean editing) {
        var inventory = player.getInventory();
        for (int i = 0; i < inventory.getContainerSize(); i++) {
            var stack = inventory.getItem(i);
            if (i != inventory.getSelectedSlot() && CubeInventory.isCube(stack)) inventory.setItem(i, CubeInventory.idle(stack));
        }
        update(player, editing || player.containerMenu != player.inventoryMenu);
    }
}
