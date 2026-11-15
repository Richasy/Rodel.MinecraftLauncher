package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldCreateAccess;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.tabs.MenuTabBar;
import net.minecraft.client.gui.components.tabs.TabManager;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import net.minecraft.client.input.KeyEvent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(CreateWorldScreen.class)
public abstract class CreateWorldMixin implements WorldCreateAccess {
    @Shadow private MenuTabBar tabNavigationBar;
    @Shadow @Final private TabManager tabManager;
    @Override public MenuTabBar rodel$tabs() { return tabNavigationBar; }
    @Override public TabManager rodel$tabManager() { return tabManager; }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int x, int y, float partialTick, CallbackInfo callback) {
        var page = WorldPage.get((Screen) (Object) this);
        if (page != null) {
            page.render(graphics, x, y, partialTick);
            callback.cancel();
        }
    }
    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void rodel$choiceKey(KeyEvent event, CallbackInfoReturnable<Boolean> callback) {
        var page = WorldPage.get((Screen) (Object) this);
        if (page != null && page.choices().visible) callback.setReturnValue(page.choices().keyPressed(event));
    }
}
