package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ModBrowserPresentation;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(GuiGraphicsExtractor.class)
public abstract class ModBrowserGraphicsMixin {
    @Inject(method = "guiWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$canvasWidth(CallbackInfoReturnable<Integer> callback) {
        if (Minecraft.getInstance().gui.screen() instanceof ModBrowserPresentation browser && browser.rodel$canvasActive()) {
            callback.setReturnValue(browser.rodel$canvasWidth());
        }
    }

    @Inject(method = "guiHeight", at = @At("HEAD"), cancellable = true)
    private void rodel$canvasHeight(CallbackInfoReturnable<Integer> callback) {
        if (Minecraft.getInstance().gui.screen() instanceof ModBrowserPresentation browser && browser.rodel$canvasActive()) {
            callback.setReturnValue(browser.rodel$canvasHeight());
        }
    }

    @ModifyVariable(method = "containsPointInScissor", at = @At("HEAD"), argsOnly = true, ordinal = 0)
    private int rodel$scissorMouseX(int x) { return rodel$screenCoordinate(x); }

    @ModifyVariable(method = "containsPointInScissor", at = @At("HEAD"), argsOnly = true, ordinal = 1)
    private int rodel$scissorMouseY(int y) { return rodel$screenCoordinate(y); }

    private int rodel$screenCoordinate(int coordinate) {
        if (Minecraft.getInstance().gui.screen() instanceof ModBrowserPresentation browser && browser.rodel$canvasActive()) {
            return Math.round(coordinate * browser.rodel$canvasScale());
        }
        return coordinate;
    }
}
