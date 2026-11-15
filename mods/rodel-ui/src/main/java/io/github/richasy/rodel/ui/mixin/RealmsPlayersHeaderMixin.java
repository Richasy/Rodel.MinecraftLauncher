package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.FocusableTextWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(targets = "com.mojang.realmsclient.gui.screens.configuration.RealmsPlayersTab$HeaderEntry")
public abstract class RealmsPlayersHeaderMixin {
    @Redirect(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/components/FocusableTextWidget;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private void rodel$count(FocusableTextWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) {
            widget.extractRenderState(graphics, mouseX, mouseY, tick);
            return;
        }
        var row = (AbstractSelectionList.Entry<?>) (Object) this;
        var label = widget.getMessage().copy().withStyle(style -> style.withUnderlined(false));
        int size = page.density().detail();
        int width = Math.min(row.getContentWidth(), NetworkRowLayout.textWidth(label, size));
        widget.setRectangle(width, row.getHeight(), row.getContentX(), row.getY());
        NetworkRowLayout.text(page, graphics, label, widget.getX(), row.getY() + (row.getHeight() - size) / 2,
            width, size, page.theme().muted(), mouseX, mouseY);
        if (widget.isFocused() && page.focusVisible())
            graphics.outline(widget.getX(), widget.getY(), widget.getWidth(), widget.getHeight(), page.theme().focus());
    }
}
