package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.client.player.FirstPersonHandsAndItems;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Updates the visible original without waiting for the native unequip/equip animation. */
@Mixin(FirstPersonHandsAndItems.class)
abstract class CubeHeldItemMixin {
    @Inject(method = "shouldInstantlyReplaceVisibleItem", at = @At("HEAD"), cancellable = true)
    private void bettertools$instant(ItemStack previous, ItemStack current, LocalPlayer player, CallbackInfoReturnable<Boolean> callback) {
        if (CubeInventory.sameCube(previous, current)) callback.setReturnValue(true);
    }
}
