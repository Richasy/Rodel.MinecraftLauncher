package dev.richasy.worldreroll.checks.mixin;

import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.world.inventory.Slot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.gen.Invoker;

/** Supplies the actual native tooltip/render boundary without moving the desktop pointer. */
@Mixin(AbstractContainerScreen.class)
public interface InventoryAccess {
    /** Selects this test's real hotbar slot for the native tooltip and scroll callbacks. */
    @Accessor("hoveredSlot") void check$hover(Slot slot);
    /** Draws through the normal measured native tooltip path on the actual render thread. */
    @Invoker("renderTooltip") void check$tooltip(GuiGraphics graphics, int x, int y);
}
