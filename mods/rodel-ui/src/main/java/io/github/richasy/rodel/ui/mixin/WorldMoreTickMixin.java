package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldMoreNavigation;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Minecraft.class)
public abstract class WorldMoreTickMixin {
    @Inject(method = "tick", at = @At("TAIL"))
    private void rodel$openMoreDetail(CallbackInfo callback) {
        WorldMoreNavigation.tick(((Minecraft) (Object) this).gui.screen());
    }
}
