package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.client.CubeMouseActions;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.inventory.Slot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Older inventories have no bundle mouse-action registry; retain their screen, slots and click callbacks. */
@Mixin(AbstractContainerScreen.class)
abstract class CubeInventoryScreenMixin extends Screen {
    @Shadow protected Slot hoveredSlot;
    @Unique private final CubeMouseActions bettertools$mouse = new CubeMouseActions();
    @Unique private Slot bettertools$previous;
    protected CubeInventoryScreenMixin(Component title) { super(title); }

    @Override public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        if (hoveredSlot != null && bettertools$mouse.matches(hoveredSlot)
                && bettertools$mouse.onMouseScrolled(horizontal, vertical, hoveredSlot.index, hoveredSlot.getItem())) return true;
        return super.mouseScrolled(x, y, horizontal, vertical);
    }
    @Inject(method = "render", at = @At("RETURN"))
    private void bettertools$hover(GuiGraphics graphics, int x, int y, float tick, CallbackInfo ci) {
        if (bettertools$previous != hoveredSlot && bettertools$previous != null && bettertools$mouse.matches(bettertools$previous))
            bettertools$mouse.onStopHovering(bettertools$previous);
        bettertools$previous = hoveredSlot;
    }
    @Inject(method = "slotClicked", at = @At("HEAD"))
    private void bettertools$click(Slot slot, int index, int button, ClickType input, CallbackInfo ci) {
        if (slot != null && bettertools$mouse.matches(slot)) bettertools$mouse.onSlotClicked(slot, input);
    }
    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void bettertools$swap(int key, int scanCode, int modifiers, CallbackInfoReturnable<Boolean> cir) {
        if (key == 258 && CubeMouseActions.swapSelected(hoveredSlot)) cir.setReturnValue(true);
    }
    @Inject(method = "renderTooltip", at = @At("HEAD"), cancellable = true)
    private void bettertools$carriedTooltip(GuiGraphics graphics, int x, int y, CallbackInfo ci) {
        if (minecraft.player == null) return;
        var carried = minecraft.player.containerMenu.getCarried();
        var cube = hoveredSlot != null && CubeInventory.isCube(hoveredSlot.getItem()) ? hoveredSlot.getItem() : carried;
        if (!CubeInventory.isCube(cube)) return;
        graphics.renderTooltip(font, java.util.List.of(Component.empty()), cube.getTooltipImage(), x, y);
        ci.cancel();
    }
}
