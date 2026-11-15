package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldEditPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.worldselection.EditWorldScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(EditWorldScreen.class)
public abstract class WorldEditScreenMixin {
    private WorldEditPage rodel$page() { return WorldEditPage.get((Screen) (Object) this); }
    @Inject(method = "init", at = @At("HEAD"))
    private void rodel$prepare(CallbackInfo callback) {
        if (rodel$page() != null) rodel$page().beginInit();
    }
    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$initialize(CallbackInfo callback) {
        if (rodel$page() != null) rodel$page().initialized();
    }
    @Inject(method = "repositionElements", at = @At("HEAD"))
    private void rodel$prepareResize(CallbackInfo callback) {
        if (rodel$page() != null) rodel$page().prepare();
    }
    @Inject(method = "repositionElements", at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        if (rodel$page() != null) rodel$page().layout();
    }
    @Inject(method = "setInitialFocus", at = @At("HEAD"), cancellable = true)
    private void rodel$focus(CallbackInfo callback) {
        if (rodel$page() != null) {
            rodel$page().focusName();
            callback.cancel();
        }
    }
    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int x, int y, float partialTick, CallbackInfo callback) {
        if (rodel$page() != null) {
            rodel$page().render(graphics, x, y, partialTick);
            callback.cancel();
        }
    }
}
