package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldCreateAccess;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.tabs.MenuTabBar;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MenuTabBar.class)
public abstract class WorldTabBarMixin {
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$tabs(GuiGraphicsExtractor graphics, int x, int y, float partialTick, CallbackInfo callback) {
        var screen = Minecraft.getInstance().gui.screen();
        var page = WorldPage.get(screen);
        if (page == null || !(screen instanceof WorldCreateAccess access) || access.rodel$tabs() != (Object) this) return;
        for (var child : access.rodel$tabs().children()) ((AbstractWidget) child).extractRenderState(graphics, x, y, partialTick);
        callback.cancel();
    }
}
