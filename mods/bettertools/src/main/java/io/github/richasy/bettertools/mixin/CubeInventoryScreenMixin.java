package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.client.CubeMouseActions;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.gui.ItemSlotMouseAction;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.world.inventory.Slot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Opts cubes into the same hover/scroll lifecycle as native bundles, without replacing the inventory screen. */
@Mixin(AbstractContainerScreen.class)
public abstract class CubeInventoryScreenMixin {
    @Shadow protected abstract void addItemSlotMouseAction(ItemSlotMouseAction action);
    @Shadow protected Slot hoveredSlot;
    @Inject(method = "init", at = @At("RETURN"))
    private void bettertools$mouseActions(CallbackInfo ci) { addItemSlotMouseAction(new CubeMouseActions()); }

    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void bettertools$swap(KeyEvent event, CallbackInfoReturnable<Boolean> cir) {
        if (event.key() == InputConstants.KEY_TAB && CubeMouseActions.swapSelected(hoveredSlot)) cir.setReturnValue(true);
    }
}
