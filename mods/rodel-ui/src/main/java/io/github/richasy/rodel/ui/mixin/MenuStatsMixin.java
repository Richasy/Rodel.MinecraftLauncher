package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.StatsMenuAccess;
import io.github.richasy.rodel.ui.menus.StatsMenuPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.tabs.MenuTabBar;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.achievement.StatsScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(StatsScreen.class)
public abstract class MenuStatsMixin implements StatsMenuAccess {
    @Override @Accessor("tabNavigationBar") public abstract MenuTabBar rodel$statsTabs();
    @Inject(method = {"init", "repositionElements"}, at = @At("RETURN"))
    private void rodel$statsLayout(CallbackInfo callback) {
        if (NativeMenuPage.get((Screen) (Object) this) instanceof StatsMenuPage page) page.initialized();
    }
    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$statsContent(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick, CallbackInfo callback) {
        if (NativeMenuPage.get((Screen) (Object) this) instanceof StatsMenuPage page) {
            page.content(graphics, mouseX, mouseY, tick);
            callback.cancel();
        }
    }
}
