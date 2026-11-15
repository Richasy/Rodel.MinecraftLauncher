package io.github.richasy.rodel.ui.workstation.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.recipebook.OverlayRecipeComponent;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(OverlayRecipeComponent.class)
abstract class WorkstationRecipeOverlayMixin {
    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V"))
    private void rodel$drawRecipeChoices(GuiGraphicsExtractor g, RenderPipeline pipeline, Identifier sprite,
                                        int x, int y, int w, int h) {
        if (WorkstationSurface.forScreen(Minecraft.getInstance().gui.screen()) == null) {
            g.blitSprite(pipeline, sprite, x, y, w, h);
            return;
        }
        g.fill(x + 1, y + 2, x + w + 1, y + h + 2, 0x50000000);
        g.fill(x, y, x + w, y + h, 0xFFC9C1B1);
        g.outline(x, y, w, h, 0xFF817665);
        g.fill(x + 1, y + 1, x + w - 1, y + 2, 0xFFE7E0D3);
    }
}
