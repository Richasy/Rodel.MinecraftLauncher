package io.github.richasy.rodel.ui.workstation.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.AbstractFurnaceScreen;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(AbstractFurnaceScreen.class)
abstract class FurnaceProgressMixin {
    @Redirect(method = "extractBackground", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIIIIIII)V"))
    private void rodel$useProceduralIndicators(GuiGraphicsExtractor g, RenderPipeline pipeline, Identifier sprite,
                                       int spriteWidth, int spriteHeight, int u, int v, int x, int y, int w, int h) {
        if (WorkstationSurface.forScreen(this) == null) {
            g.blitSprite(pipeline, sprite, spriteWidth, spriteHeight, u, v, x, y, w, h);
        }
    }
}
