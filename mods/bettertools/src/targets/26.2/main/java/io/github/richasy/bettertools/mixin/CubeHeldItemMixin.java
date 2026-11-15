package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.client.renderer.ItemInHandRenderer;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Pre-26.3 hand renderer: replace only the same cube's visible original immediately. */
@Mixin(ItemInHandRenderer.class)
abstract class CubeHeldItemMixin {
    @Inject(method = "shouldInstantlyReplaceVisibleItem", at = @At("HEAD"), cancellable = true)
    private void bettertools$instant(ItemStack previous, ItemStack current, CallbackInfoReturnable<Boolean> callback) {
        if (CubeInventory.sameCube(previous, current)) callback.setReturnValue(true);
    }
}
