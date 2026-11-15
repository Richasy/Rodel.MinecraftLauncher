package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.ItemInHandRenderer;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Updates only the same cube's visible original before vanilla computes its equip animation. */
@Mixin(ItemInHandRenderer.class)
abstract class CubeHeldItemMixin {
    @Shadow private ItemStack mainHandItem;
    @Inject(method = "tick", at = @At("HEAD"))
    private void bettertools$instant(CallbackInfo ci) {
        var player = Minecraft.getInstance().player;
        if (player != null && CubeInventory.sameCube(mainHandItem, player.getMainHandItem())) mainHandItem = player.getMainHandItem();
    }
}
