package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(GuiGraphicsExtractor.class)
public abstract class MenuViewportMixin {
    @Inject(method = "guiWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$viewportWidth(CallbackInfoReturnable<Integer> callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.rendering()) callback.setReturnValue(page.screen().width);
    }
    @Inject(method = "guiHeight", at = @At("HEAD"), cancellable = true)
    private void rodel$viewportHeight(CallbackInfoReturnable<Integer> callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.rendering()) callback.setReturnValue(page.screen().height);
    }
}
