package io.github.richasy.rodel.ui.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.compat.ModBrowserPresentation;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GuiGraphicsExtractor.class)
public abstract class ModBrowserInputMixin {
    @Inject(method = "blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V",
        at = @At("HEAD"), cancellable = true)
    private void rodel$browserInput(RenderPipeline pipeline, Identifier sprite, int x, int y, int width, int height,
                                   CallbackInfo callback) {
        if (Minecraft.getInstance().gui.screen() instanceof ModBrowserPresentation browser
            && browser.rodel$drawInput(null, (GuiGraphicsExtractor) (Object) this, x, y, width, height)) {
            callback.cancel();
        }
    }
}
