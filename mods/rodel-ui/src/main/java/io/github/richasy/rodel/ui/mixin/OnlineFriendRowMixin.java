package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractContainerWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.friends.FriendEntry",
    "net.minecraft.client.gui.screens.friends.IncomingEntry",
    "net.minecraft.client.gui.screens.friends.OutgoingEntry"
})
public abstract class OnlineFriendRowMixin {
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$row(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick, CallbackInfo callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) return;
        NetworkRowLayout.drawFriend(page, (AbstractContainerWidget) (Object) this, graphics, mouseX, mouseY, tick);
        callback.cancel();
    }
}
