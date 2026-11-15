package io.github.richasy.bettertools;

import java.util.Optional;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.world.entity.SlotAccess;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.ClickAction;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.inventory.tooltip.TooltipComponent;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.ItemContainerContents;

/** Idle shell. Tools are never emulated by this item: morphing equips the stored original stack. */
public final class CubeItem extends Item {
    public CubeItem(Properties properties) { super(properties); }

    /** Shows the remembered original's actual durability while inventory editing has restored the shell. */
    @Override public boolean isBarVisible(ItemStack stack) {
        var tool = CubeInventory.displayedTool(stack);
        return !tool.isEmpty() && tool.isBarVisible();
    }

    /** Uses the original item's own bar calculation, including other mods' item overrides. */
    @Override public int getBarWidth(ItemStack stack) {
        var tool = CubeInventory.displayedTool(stack);
        return tool.isEmpty() ? 0 : tool.getBarWidth();
    }

    /** Uses the original item's native durability color without copying damage onto the shell. */
    @Override public int getBarColor(ItemStack stack) {
        var tool = CubeInventory.displayedTool(stack);
        return tool.isEmpty() ? 0 : tool.getBarColor();
    }

    /** Like a bundle, a cube on the cursor can collect equipment or right-click it into an empty native slot. */
    @Override public boolean overrideStackedOnOther(ItemStack self, Slot slot, ClickAction action, Player player) {
        var other = slot.getItem();
        if (!other.isEmpty()) {
            if (slot.mayPickup(player) && CubeInventory.canInsert(self, other)) {
                var taken = slot.safeTake(1, 1, player);
                if (!taken.isEmpty()) { CubeInventory.insert(self, taken); player.playSound(SoundEvents.BUNDLE_INSERT, .8f, 1); }
            } else player.playSound(SoundEvents.BUNDLE_INSERT_FAIL, .8f, 1);
            player.containerMenu.broadcastChanges();
            return true;
        }
        if (action != ClickAction.SECONDARY) return false;
        if (slot.allowModification(player)) {
            var before = CubeInventory.state(self);
            var taken = CubeInventory.take(self);
            if (!taken.isEmpty()) {
                var remainder = slot.safeInsert(taken);
                if (!remainder.isEmpty()) self.set(BetterTools.STATE, before);
                else player.playSound(SoundEvents.BUNDLE_REMOVE_ONE, .8f, 1);
            }
        }
        player.containerMenu.broadcastChanges();
        return true;
    }

    /** Native bundle click hooks keep the current inventory open and return the selected original to the cursor. */
    @Override public boolean overrideOtherStackedOnMe(ItemStack self, ItemStack other, Slot slot,
            ClickAction action, Player player, SlotAccess carried) {
        if (!other.isEmpty()) {
            if (slot.allowModification(player) && CubeInventory.insert(self, other))
                player.playSound(SoundEvents.BUNDLE_INSERT, .8f, 1);
            else player.playSound(SoundEvents.BUNDLE_INSERT_FAIL, .8f, 1);
            player.containerMenu.broadcastChanges();
            return true;
        }
        if (action == ClickAction.PRIMARY) { CubeInventory.pick(self, -1); return false; }
        if (slot.allowModification(player)) {
            var taken = CubeInventory.take(self);
            if (!taken.isEmpty()) { carried.set(taken); player.playSound(SoundEvents.BUNDLE_REMOVE_ONE, .8f, 1); }
        }
        player.containerMenu.broadcastChanges();
        return true;
    }

    /** Supplies the same native tooltip-image path used by bundles, including when carrying another item. */
    @Override public Optional<TooltipComponent> getTooltipImage(ItemStack stack) {
        var state = stack.get(BetterTools.STATE);
        return Optional.of(new CubeTooltip(state == null ? ItemContainerContents.EMPTY : state.contents(),
                state == null ? -1 : state.picked(), stack.getHoverName().copy()));
    }

    /** Prevents recursive container trees in bundles and other vanilla container items. */
    @Override public boolean canFitInsideContainerItems() { return false; }
}
