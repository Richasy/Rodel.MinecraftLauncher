package io.github.richasy.rodel.ui.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.mojang.realmsclient.gui.RealmsWorldSlotButton;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.gui.Font;
import net.minecraft.network.chat.Component;
import net.minecraft.client.gui.components.StringWidget;

@Mixin(RealmsWorldSlotButton.class)
public abstract class RealmsMenuImageTextMixin {
    @WrapMethod(method = "extractContents")
    private void rodel$imageCaption(GuiGraphicsExtractor graphics, int x, int y, float partialTick, Operation<Void> original) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) {
            original.call(graphics, x, y, partialTick);
            return;
        }
        page.beginNativeText();
        try {
            original.call(graphics, x, y, partialTick);
            var slot = (RealmsWorldSlotButton) (Object) this;
            int border = slot.getState().activeSlot || slot.isHovered()
                || slot.isFocused() && page.focusVisible() ? page.theme().focus() : page.theme().edge();
            graphics.outline(slot.getX(), slot.getY(), slot.getWidth(), slot.getHeight(), border);
        } finally {
            page.endNativeText();
        }
    }

    @Redirect(method = "extractContents", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;centeredText(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$version(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) graphics.centeredText(font, text, x, y, color);
        else {
            Component label = MenuText.secondary(text);
            int size = page.density().detail();
            MenuText.extract(graphics, font, label, x - font.width(label) * size / (2f * font.lineHeight), y, size, page.theme().muted());
        }
    }

    @Redirect(method = "extractContents", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/components/StringWidget;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private void rodel$slotName(StringWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) {
            widget.extractRenderState(graphics, mouseX, mouseY, tick);
            return;
        }
        var slot = (RealmsWorldSlotButton) (Object) this;
        int inset = page.density().inset();
        int size = page.density().body();
        int width = Math.max(0, slot.getWidth() - inset * 2);
        int textWidth = Math.min(width, NetworkRowLayout.textWidth(widget.getMessage(), size));
        widget.setMaxWidth(0);
        widget.setRectangle(textWidth, size, slot.getX() + (slot.getWidth() - textWidth) / 2,
            slot.getBottom() - inset - size);
        NetworkRowLayout.text(page, graphics, widget.getMessage(), widget.getX(), widget.getY(), textWidth, size,
            0xFFFFFFFF, mouseX, mouseY);
    }
}
