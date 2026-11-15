package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.DialogHost;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Pseudo
@Mixin(targets = "net.fabricmc.fabric.impl.client.registry.sync.validate.DetailsScreen")
public abstract class MissingContentDetailsMixin {
    @Inject(method = {"init", "repositionElements"}, at = @At("HEAD"))
    private void rodel$layout(CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        if (dialog != null) dialog.prepare();
    }
    @Inject(method = "extractBackground", at = @At("HEAD"), cancellable = true)
    private void rodel$background(CallbackInfo callback) {
        if (((DialogHost) this).rodel$dialog() != null) callback.cancel();
    }
}
