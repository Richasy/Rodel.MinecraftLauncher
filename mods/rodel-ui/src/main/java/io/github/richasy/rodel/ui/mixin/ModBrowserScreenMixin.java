package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ModBrowserScreens;
import net.minecraft.client.gui.Gui;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(Gui.class)
public abstract class ModBrowserScreenMixin {
    @ModifyVariable(method = "setScreen", at = @At("HEAD"), argsOnly = true)
    private Screen rodel$modMenuBrowser(Screen screen) {
        return ModBrowserScreens.modernize(screen);
    }
}
