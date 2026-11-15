package io.github.richasy.rodel.ui.workstation.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeState;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.client.gui.screens.inventory.AbstractFurnaceScreen;
import net.minecraft.client.gui.screens.inventory.CraftingScreen;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin({AbstractFurnaceScreen.class, CraftingScreen.class})
abstract class WorkstationBackgroundMixin {
    @Redirect(method = "extractBackground", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blit(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIFFIIII)V"))
    private void rodel$drawSurface(GuiGraphicsExtractor g, RenderPipeline pipeline, Identifier texture,
                                   int x, int y, float u, float v, int w, int h, int textureWidth, int textureHeight) {
        WorkstationSurface surface = WorkstationSurface.forScreen(this);
        if (surface == null) {
            g.blit(pipeline, texture, x, y, u, v, w, h, textureWidth, textureHeight);
            return;
        }
        if (!((WorkstationRecipeState) this).rodel$recipeOnly()) {
            surface.background(g, (AbstractContainerScreen<?>) (Object) this, x, y);
        }
    }
}
