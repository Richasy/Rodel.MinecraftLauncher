package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.ManageServerScreen",
    "net.minecraft.client.gui.screens.DirectJoinServerScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsInviteScreen"
})
public abstract class OnlineFormTextMixin {
    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"),
        require = 0)
    private void rodel$label(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        Screen screen = (Screen) (Object) this;
        if (NativeMenuPage.get(screen) == null || !NetworkMenuLayouts.replacesNativeText(screen, text))
            graphics.text(font, text, x, y, color);
    }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;centeredText(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"),
        require = 0)
    private void rodel$title(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        Screen screen = (Screen) (Object) this;
        if (NativeMenuPage.get(screen) == null || !NetworkMenuLayouts.replacesNativeText(screen, text))
            graphics.centeredText(font, text, x, y, color);
    }
}
