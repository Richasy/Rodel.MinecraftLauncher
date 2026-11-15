package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.PlainTextButton;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PlainTextButton.class)
public abstract class OnlineProfileNameMixin {
    @Inject(method = "extractContents", at = @At("HEAD"), cancellable = true)
    private void rodel$profile(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick, CallbackInfo callback) {
        var widget = (PlainTextButton) (Object) this;
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()
            || !widget.getClass().getName().equals("net.minecraft.client.gui.screens.friends.AddFriendWidget$2")) return;
        int size = page.density().detail();
        boolean highlight = widget.isHovered() || widget.isFocused() && page.focusVisible();
        NetworkRowLayout.text(page, graphics, widget.getMessage(), widget.getX(), widget.getY() + (widget.getHeight() - size) / 2,
            widget.getWidth(), size, highlight ? page.theme().focus() : page.theme().ink(), mouseX, mouseY);
        if (highlight) graphics.horizontalLine(widget.getX(), widget.getRight(),
            widget.getY() + (widget.getHeight() + size) / 2 + 1, page.theme().focus());
        callback.cancel();
    }
}
