package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.DialogHost;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.GenericMessageScreen",
    "net.minecraft.client.gui.screens.LevelLoadingScreen"
})
public abstract class WorldLoadingBackgroundMixin {
    @Inject(method = "extractBackground", at = @At("HEAD"), cancellable = true)
    private void rodel$background(CallbackInfo callback) {
        if (((DialogHost) this).rodel$dialog() != null) callback.cancel();
    }
}
