package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.FocusableTextWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(FocusableTextWidget.class)
public abstract class OnlineFriendSectionMixin {
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$heading(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick, CallbackInfo callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.rendering()
            && NetworkRowLayout.drawFriendSection(page, (FocusableTextWidget) (Object) this, graphics, mouseX, mouseY))
            callback.cancel();
    }
}
