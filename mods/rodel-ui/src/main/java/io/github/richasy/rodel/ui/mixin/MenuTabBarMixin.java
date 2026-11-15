package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.tabs.MenuTabBar;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MenuTabBar.class)
public abstract class MenuTabBarMixin {
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$tabs(GuiGraphicsExtractor graphics, int x, int y, float tick, CallbackInfo callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) == null) return;
        for (var child : ((MenuTabBar) (Object) this).children())
            if (child instanceof AbstractWidget widget) widget.extractRenderState(graphics, x, y, tick);
        callback.cancel();
    }
}
