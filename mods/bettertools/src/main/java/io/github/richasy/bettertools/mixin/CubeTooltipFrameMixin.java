package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.client.CubeTooltipLook;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.tooltip.TooltipRenderUtil;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Replaces the purple frame only for the explicit cube style, retaining native tooltip placement and layering. */
@Mixin(TooltipRenderUtil.class)
public abstract class CubeTooltipFrameMixin {
    @Inject(method = "extractTooltipBackground", at = @At("HEAD"), cancellable = true)
    private static void bettertools$frame(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
            Identifier style, CallbackInfo ci) {
        if (!CubeTooltipLook.STYLE.equals(style)) return;
        // The image component paints two distinct surfaces inside the measured native tooltip bounds.
        ci.cancel();
    }
}
