package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.client.CubeTooltipScope;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.inventory.tooltip.TooltipRenderUtil;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Leaves positioning and layering native; only the cube image paints its own surfaces. */
@Mixin(TooltipRenderUtil.class)
abstract class CubeTooltipFrameMixin {
    @Inject(method = "renderTooltipBackground", at = @At("HEAD"), cancellable = true)
    private static void bettertools$frame(GuiGraphics graphics, int x, int y, int width, int height, int z, CallbackInfo ci) {
        if (((CubeTooltipScope) graphics).bettertools$cubeTooltip()) ci.cancel();
    }
}
