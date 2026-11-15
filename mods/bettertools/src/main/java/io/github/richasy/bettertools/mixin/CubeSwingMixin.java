package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeCombat;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.component.SwingAnimation;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Native main-hand swings include misses, independent of hit confirmation. */
@Mixin(LivingEntity.class)
abstract class CubeSwingMixin {
    @Inject(method = "swing", at = @At("HEAD"))
    private void bettertools$swing(InteractionHand hand, SwingAnimation animation, boolean send, CallbackInfoReturnable<Boolean> callback) {
        if (hand == InteractionHand.MAIN_HAND && (Object)this instanceof Player player) CubeCombat.swing(player);
    }
}
