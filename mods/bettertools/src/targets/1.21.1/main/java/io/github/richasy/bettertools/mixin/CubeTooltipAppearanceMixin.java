package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.client.ClientCubeTooltip;
import io.github.richasy.bettertools.client.CubeTooltipScope;
import java.util.List;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipComponent;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipPositioner;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** This version has no tooltip-style argument, so the exact native component list owns the frame scope. */
@Mixin(GuiGraphics.class)
abstract class CubeTooltipAppearanceMixin implements CubeTooltipScope {
    @Unique private boolean bettertools$cube;
    @Override public boolean bettertools$cubeTooltip() { return bettertools$cube; }
    @Inject(method = "renderTooltipInternal", at = @At("HEAD"))
    private void bettertools$begin(Font font, List<ClientTooltipComponent> components, int x, int y, ClientTooltipPositioner positioner, CallbackInfo ci) {
        bettertools$cube = components.stream().anyMatch(ClientCubeTooltip.class::isInstance);
    }
    @Inject(method = "renderTooltipInternal", at = @At("RETURN"))
    private void bettertools$end(Font font, List<ClientTooltipComponent> components, int x, int y, ClientTooltipPositioner positioner, CallbackInfo ci) {
        bettertools$cube = false;
    }
}
