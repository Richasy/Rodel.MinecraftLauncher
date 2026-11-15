package io.github.richasy.bettertools.client;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeState;
import io.github.richasy.bettertools.SelectionPayload;
import io.github.richasy.bettertools.SwapPayload;
import net.minecraft.client.Minecraft;
import net.minecraft.client.ScrollWheelHandler;
import net.minecraft.client.gui.ItemSlotMouseAction;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.item.ItemStack;

/** Uses the native bundle wheel accumulator and the current inventory's existing slot callbacks. */
public final class CubeMouseActions implements ItemSlotMouseAction {
    private final ScrollWheelHandler wheel = new ScrollWheelHandler();
    @Override public boolean matches(Slot slot) { return CubeInventory.isCube(slot.getItem()); }
    @Override public boolean onMouseScrolled(double scrollX, double scrollY, int slotIndex, ItemStack stack) {
        var state = CubeInventory.state(stack);
        var contents = state.unpack();
        var occupied = new java.util.ArrayList<Integer>();
        for (int i = 0; i < CubeState.TOOL_SLOTS; i++) if (!contents.get(i).isEmpty()) occupied.add(i);
        if (occupied.isEmpty()) return false;
        var delta = wheel.onMouseScroll(scrollX, scrollY);
        int amount = delta.y == 0 ? -delta.x : delta.y;
        if (amount != 0) select(stack, slotIndex, occupied.get(ScrollWheelHandler.getNextScrollWheelSelection(amount,
                occupied.indexOf(state.picked()), occupied.size())));
        return true;
    }
    @Override public void onStopHovering(Slot slot) { select(slot.getItem(), slot.index, -1); }
    @Override public void onSlotClicked(Slot slot, ContainerInput input) {
        if (input == ContainerInput.QUICK_MOVE || input == ContainerInput.SWAP) select(slot.getItem(), slot.index, -1);
    }

    /** Consumes Tab only for an explicitly selected stored tool; other inventory focus navigation stays native. */
    public static boolean swapSelected(Slot slot) {
        var client = Minecraft.getInstance();
        if (slot == null || client.player == null || client.getConnection() == null
                || !client.player.containerMenu.getCarried().isEmpty() || !slot.allowModification(client.player)) return false;
        var stack = slot.getItem();
        var state = stack.get(BetterTools.STATE);
        if (state == null || !CubeInventory.swapPicked(stack)) return false;
        BetterToolsClient.send(new SwapPayload(client.player.containerMenu.containerId, slot.index, state.picked(), state.identity()));
        return true;
    }
    private static void select(ItemStack stack, int slot, int picked) {
        var client = Minecraft.getInstance();
        var state = stack.get(BetterTools.STATE);
        if (client.player == null || client.getConnection() == null || (state == null ? picked == -1 : state.picked() == picked)) return;
        CubeInventory.pick(stack, picked);
        BetterToolsClient.send(new SelectionPayload(client.player.containerMenu.containerId, slot, picked));
    }
}
