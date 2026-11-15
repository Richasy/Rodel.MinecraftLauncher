package io.github.richasy.bettertools;

import java.util.List;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.ItemContainerContents;

/** Lossless insertion, extraction and physical stack exchange; the server owns all committed changes. */
public final class CubeInventory {
    private CubeInventory() { }

    /** Recognizes both the idle shell and a real tool currently carrying the other items. */
    public static boolean isCube(ItemStack stack) {
        return !stack.isEmpty() && (stack.is(BetterTools.CUBE) || stack.has(BetterTools.STATE));
    }

    /** Identifies transformations of the same storage item, excluding ordinary hotbar/item changes. */
    public static boolean sameCube(ItemStack previous, ItemStack current) {
        var before = previous.get(BetterTools.STATE);
        var after = current.get(BetterTools.STATE);
        return before != null && after != null && before.identity().equals(after.identity());
    }

    /** Gets a stable snapshot, assigning an identity only on first use. */
    public static CubeState state(ItemStack stack) {
        var state = stack.get(BetterTools.STATE);
        if (state == null) {
            state = CubeState.empty();
            stack.set(BetterTools.STATE, state);
        }
        return state;
    }

    /** Selects a stored real stack, keeping every other stack and the original shell in its component. */
    public static ItemStack select(ItemStack held, int selected) {
        var state = state(held);
        if (selected == state.active()) return held;
        var items = state.equipment(held);
        int destination = selected < 0 ? CubeState.SHELL_SLOT : selected;
        var next = items.get(destination);
        if (next.isEmpty()) return held;
        items.set(destination, ItemStack.EMPTY);
        next.set(BetterTools.STATE, new CubeState(ItemContainerContents.fromItems(items), selected,
                selected < 0 ? state.display() : selected, -1, state.identity()));
        return next;
    }

    /** Restores the cube appearance while preserving the current tool's latest native components. */
    public static ItemStack idle(ItemStack held) {
        return isCube(held) ? select(held, -1) : held;
    }

    /** Inserts one whole original tool into primary, then reserve; rejection never consumes the source. */
    public static boolean insert(ItemStack cube, ItemStack source) {
        var kind = ToolKind.of(source);
        if (kind == null) return false;
        var state = state(cube);
        if (state.active() >= 0) return false;
        var items = state.unpack();
        for (int row = 0; row < 2; row++) {
            int index = row * CubeState.COLUMNS + kind.ordinal();
            if (items.get(index).isEmpty()) {
                items.set(index, source.copyWithCount(1));
                cube.set(BetterTools.STATE, new CubeState(ItemContainerContents.fromItems(items), -1,
                        state.display() < 0 ? index : state.display(), -1, state.identity()));
                source.shrink(1);
                return true;
            }
        }
        return false;
    }

    /** Replaces only this cube's ten editable slots, keeping its identity and shell intact. */
    public static void save(ItemStack cube, List<ItemStack> equipment) {
        var state = state(cube);
        var items = state.unpack();
        for (int i = 0; i < CubeState.TOOL_SLOTS; i++) items.set(i, equipment.get(i).copy());
        int display = state.display();
        if (display >= 0 && items.get(display).isEmpty()) display = -1;
        cube.set(BetterTools.STATE, new CubeState(ItemContainerContents.fromItems(items), -1, display, -1, state.identity()));
    }

    /** Returns a detached original for GUI presentation; the idle shell remembers its last selected tool. */
    public static ItemStack displayedTool(ItemStack held) {
        var state = held.get(BetterTools.STATE);
        if (state == null) return ItemStack.EMPTY;
        if (state.active() >= 0) {
            var original = held.copy();
            original.remove(BetterTools.STATE);
            return original;
        }
        return state.display() < 0 ? ItemStack.EMPTY : state.unpack().get(state.display());
    }

    /** Changes only the bundle-style hovered selection; empty typed slots can also be inspected. */
    public static void pick(ItemStack cube, int picked) {
        if (!isCube(cube) || picked < -1 || picked >= CubeState.TOOL_SLOTS) return;
        var state = state(cube);
        if (state.active() >= 0 || state.picked() == picked) return;
        cube.set(BetterTools.STATE, new CubeState(state.contents(), -1, state.display(), picked, state.identity()));
    }

    /** Exchanges the selected original with its same-kind primary/reserve cell, keeping selection and icon on that original. */
    public static boolean swapPicked(ItemStack cube) {
        var state = cube.get(BetterTools.STATE);
        if (!isCube(cube) || state == null || state.active() >= 0 || state.picked() < 0) return false;
        var items = state.unpack();
        int picked = state.picked();
        if (items.get(picked).isEmpty()) return false;
        int other = (picked + CubeState.COLUMNS) % CubeState.TOOL_SLOTS;
        var selected = items.get(picked);
        items.set(picked, items.get(other));
        items.set(other, selected);
        int display = state.display() == picked ? other : state.display() == other ? picked : state.display();
        cube.set(BetterTools.STATE, new CubeState(ItemContainerContents.fromItems(items), -1, display, other, state.identity()));
        return true;
    }

    /** Uses the highlighted cell, or the first occupied cell when the pointer has no explicit selection. */
    public static int pickedSlot(ItemStack cube) {
        var state = state(cube);
        if (state.picked() >= 0) return state.picked();
        var items = state.unpack();
        for (int i = 0; i < CubeState.TOOL_SLOTS; i++) if (!items.get(i).isEmpty()) return i;
        return -1;
    }

    /** Removes one complete original to the cursor/target slot, without opening another container. */
    public static ItemStack take(ItemStack cube) {
        int selected = pickedSlot(cube);
        if (selected < 0) return ItemStack.EMPTY;
        var items = state(cube).unpack();
        var result = items.get(selected);
        if (result.isEmpty()) return result;
        items.set(selected, ItemStack.EMPTY);
        save(cube, items);
        return result;
    }

    /** Checks a slot transfer before removing its source item; a full column never swaps the cube. */
    public static boolean canInsert(ItemStack cube, ItemStack source) {
        var kind = ToolKind.of(source);
        if (kind == null) return false;
        var state = state(cube);
        var items = state.unpack();
        return state.active() < 0 && (items.get(kind.ordinal()).isEmpty() || items.get(kind.ordinal() + CubeState.COLUMNS).isEmpty());
    }

    /** Recovers the shell and remaining tools after native durability consumes the active real tool. */
    public static void broken(ItemStack broken, CubeState snapshot, ServerPlayer player, EquipmentSlot slot) {
        if (slot != EquipmentSlot.MAINHAND || !broken.isEmpty() || snapshot == null) return;
        var items = snapshot.unpack();
        var shell = items.get(CubeState.SHELL_SLOT);
        if (shell.isEmpty()) return;
        items.set(CubeState.SHELL_SLOT, ItemStack.EMPTY);
        shell.set(BetterTools.STATE, new CubeState(ItemContainerContents.fromItems(items), -1, -1, -1, snapshot.identity()));
        int replacement = CubeSelection.first(items, snapshot.active() % CubeState.COLUMNS);
        if (replacement < 0) replacement = CubeSelection.fallback(items, snapshot.active(), snapshot.display());
        var next = replacement < 0 ? shell : select(shell, replacement);
        player.setItemSlot(slot, next);
        ((io.github.richasy.bettertools.mixin.EquipmentUpdateAccess)player).bettertools$updateEquipment();
        player.connection.send(player.getInventory().createInventoryUpdatePacket(player.getInventory().getSelectedSlot()));
        player.inventoryMenu.broadcastChanges();
    }
}
