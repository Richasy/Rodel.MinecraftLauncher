package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.social.PlayerEntry",
    "net.minecraft.client.gui.screens.friends.AbstractFriendsEntryContainerWidget"
})
public abstract class OnlineRowSurfaceMixin {
    @Redirect(method = {"extractContent", "extractWidgetRenderState"}, at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;fill(IIIII)V"), require = 0)
    private void rodel$row(GuiGraphicsExtractor graphics, int left, int top, int right, int bottom, int color) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null) graphics.fill(left, top, right, bottom, color);
        else {
            graphics.fill(left, top, right, bottom, page.theme().surface());
            graphics.outline(left, top, right - left, bottom - top, page.theme().edge());
        }
    }
}
