package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeCombat;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Pre-26.3 native swings use the hand/send overload, including empty-air sword slashes. */
@Mixin(LivingEntity.class)
abstract class CubeSwingMixin {
    @Inject(method = "swing(Lnet/minecraft/world/InteractionHand;Z)V", at = @At("HEAD"))
    private void bettertools$swing(InteractionHand hand, boolean send, CallbackInfo callback) {
        if (hand == InteractionHand.MAIN_HAND && (Object)this instanceof Player player) CubeCombat.swing(player);
    }
}
