package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.MenuTabSurface;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.TabButton;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.friends.FriendsOverlayTabButton",
    "net.minecraft.client.gui.components.tabs.MenuTabBar$MenuTabButton"
})
public abstract class MenuTabMixin {
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$tab(GuiGraphicsExtractor graphics, int x, int y, float tick, CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null) return;
        TabButton tab = (TabButton) (Object) this;
        MenuTabSurface.draw(page, graphics, tab, tab.getMessage(), tab.isSelected());
        callback.cancel();
    }
}
