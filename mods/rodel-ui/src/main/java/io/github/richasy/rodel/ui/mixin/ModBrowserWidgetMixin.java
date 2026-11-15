package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ModBrowserPresentation;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(AbstractWidget.class)
public abstract class ModBrowserWidgetMixin {
    @Shadow
    protected abstract void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick);

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/components/AbstractWidget;extractWidgetRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private void rodel$browserWidget(AbstractWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        if (!(Minecraft.getInstance().gui.screen() instanceof ModBrowserPresentation browser)
            || !browser.rodel$drawWidget(widget, graphics, mouseX, mouseY)) {
            extractWidgetRenderState(graphics, mouseX, mouseY, partialTick);
        }
    }
}
