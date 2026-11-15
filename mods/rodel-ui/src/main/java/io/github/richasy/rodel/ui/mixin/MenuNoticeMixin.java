package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.FocusableTextWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(FocusableTextWidget.class)
public abstract class MenuNoticeMixin {
    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;fill(IIIII)V"))
    private void rodel$surface(GuiGraphicsExtractor graphics, int left, int top, int right, int bottom, int color) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        graphics.fill(left, top, right, bottom, page != null && page.rendering() ? page.theme().surface() : color);
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;outline(IIIII)V"))
    private void rodel$edge(GuiGraphicsExtractor graphics, int x, int y, int width, int height, int color) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        graphics.outline(x, y, width, height, page != null && page.rendering() ? page.theme().edge() : color);
    }
}
