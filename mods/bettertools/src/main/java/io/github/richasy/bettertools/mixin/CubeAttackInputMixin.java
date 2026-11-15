package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeCombat;
import io.github.richasy.bettertools.CubeSelection;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Resolves prediction before vanilla consumes this click, including an empty-air slash. */
@Mixin(Minecraft.class)
abstract class CubeAttackInputMixin {
    @Inject(method = "startAttack", at = @At("HEAD"))
    private void bettertools$prepareAttack(CallbackInfoReturnable<Boolean> callback) {
        var client = (Minecraft)(Object)this;
        if (client.player == null || client.gui.screen() != null) return;
        CubeCombat.swing(client.player);
        CubeSelection.update(client.player, false);
    }
}
