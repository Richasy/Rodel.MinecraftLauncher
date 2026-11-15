package io.github.richasy.rodel.ui.workstation.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.screens.recipebook.RecipeButton;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(value = RecipeButton.class, targets = "net.minecraft.client.gui.screens.recipebook.OverlayRecipeComponent$OverlayRecipeButton")
abstract class WorkstationRecipeTileMixin {
    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V"))
    private void rodel$drawRecipeTile(GuiGraphicsExtractor g, RenderPipeline pipeline, Identifier sprite,
                                      int x, int y, int w, int h) {
        WorkstationSurface surface = WorkstationSurface.forScreen(Minecraft.getInstance().gui.screen());
        if (surface == null) {
            g.blitSprite(pipeline, sprite, x, y, w, h);
            return;
        }
        AbstractWidget widget = (AbstractWidget) (Object) this;
        String state = sprite.getPath();
        surface.recipeTile(g, x, y, w, h, state.endsWith("_selected"), state.contains("uncraftable") || state.contains("disabled"),
            state.startsWith("recipe_book/slot_many_"), widget.isHovered(),
            widget.isFocused() && Minecraft.getInstance().getLastInputType().isKeyboard());
    }
}
