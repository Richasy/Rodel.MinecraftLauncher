package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ModernModsScreen;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.api.ModMenuApi", remap = false)
public interface ModBrowserApiMixin {
    @Inject(method = "createModsScreen", at = @At("RETURN"), cancellable = true)
    private static void rodel$browser(Screen parent, CallbackInfoReturnable<Screen> callback) {
        callback.setReturnValue(ModernModsScreen.modernize(parent, callback.getReturnValue()));
    }
}
